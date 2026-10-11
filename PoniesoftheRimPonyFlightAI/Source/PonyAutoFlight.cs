using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PonyAutoFlight
{
    public class AutoFlightConfigDef : Def
    {
        public int evaluationInterval = 60;
        public int landingCheckInterval = 5;
        public float landingDistance = 5f;
        public int defaultPathLength = 50;
        public int minPathLength = 30;
        public int maxPathLength = 300;
        public float defaultStartStamina = 0.90f;
        public float minStartStamina = 0.80f;
        public float maxStartStamina = 0.99f;
        public float defaultStopStamina = 0.50f;
        public float minStopStamina = 0.30f;
        public float maxStopStamina = 0.60f;
    }

    public static class Config
    {
        private static AutoFlightConfigDef fallback;
        public static AutoFlightConfigDef Current =>
            DefDatabase<AutoFlightConfigDef>.GetNamedSilentFail("PAF_Defaults")
            ?? (fallback ?? (fallback = new AutoFlightConfigDef()));
    }

    public class PawnFlightSettings : IExposable
    {
        public Pawn pawn;
        public bool flightAllowed = true;
        public int pathLength;
        public float startStamina;
        public float stopStamina;
        public bool autoStarted;
        public bool landingPending;

        // Runtime-only identity of the movement segment. A rebuilt path to
        // the same destination does not clear the landing latch.
        internal Job trackedJob;
        internal LocalTargetInfo trackedDestination;
        internal bool hasMovement;
        internal bool landedForSegment;

        public PawnFlightSettings() { }
        public PawnFlightSettings(Pawn pawn)
        {
            this.pawn = pawn;
            ResetThresholds();
        }
        public void ResetThresholds()
        {
            pathLength = Config.Current.defaultPathLength;
            startStamina = Config.Current.defaultStartStamina;
            stopStamina = Config.Current.defaultStopStamina;
        }
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref flightAllowed, "flightAllowed", true);
            Scribe_Values.Look(ref pathLength, "pathLength", Config.Current.defaultPathLength);
            Scribe_Values.Look(ref startStamina, "startStamina", Config.Current.defaultStartStamina);
            Scribe_Values.Look(ref stopStamina, "stopStamina", Config.Current.defaultStopStamina);
            Scribe_Values.Look(ref autoStarted, "autoStarted", false);
            Scribe_Values.Look(ref landingPending, "landingPending", false);
        }
    }

    public static class FlightBridge
    {
        private static readonly Dictionary<Type, PropertyInfo> enabledProperties =
            new Dictionary<Type, PropertyInfo>();
        private static readonly MethodInfo repathMethod = AccessTools.Method(
            typeof(Pawn_PathFollower), "SetNewPathRequest", Type.EmptyTypes);
        private static bool warnedRepath;

        public static ThingComp Toggle(Pawn pawn) => pawn?.AllComps?.FirstOrDefault(
            c => c.GetType().Name == "CompPegasusFlightToggle");
        public static ThingComp Timer(Pawn pawn) => pawn?.AllComps?.FirstOrDefault(
            c => c.GetType().Name == "CompPegasusFlightTimer");
        private static PropertyInfo EnabledProperty(ThingComp toggle)
        {
            if (toggle == null) return null;
            Type type = toggle.GetType();
            if (!enabledProperties.TryGetValue(type, out PropertyInfo property))
            {
                property = AccessTools.Property(type, "FlightEnabled");
                enabledProperties[type] = property;
            }
            return property;
        }
        public static bool Enabled(ThingComp toggle) =>
            EnabledProperty(toggle)?.GetValue(toggle, null) is bool value && value;
        public static void SetEnabled(ThingComp toggle, bool enabled) =>
            EnabledProperty(toggle)?.SetValue(toggle, enabled, null);
        public static float Stamina(ThingComp timer)
        {
            if (timer == null) return -1f;
            PropertyInfo property = AccessTools.Property(timer.GetType(), "CurrentStaminaPercent");
            FieldInfo field = AccessTools.Field(timer.GetType(), "CurrentStaminaPercent");
            object value = property != null ? property.GetValue(timer, null) : field?.GetValue(timer);
            return value == null ? -1f : Convert.ToSingle(value);
        }
        public static bool CanTakeOff(ThingComp toggle)
        {
            if (toggle == null) return false;
            MethodInfo method = AccessTools.Method(toggle.GetType(), "CanTakeOffNow", Type.EmptyTypes);
            PropertyInfo property = AccessTools.Property(toggle.GetType(), "CanTakeOffNow");
            object value = method != null ? method.Invoke(toggle, null) : property?.GetValue(toggle, null);
            return value is bool allowed && allowed;
        }
        public static void RequestRepath(Pawn pawn)
        {
            if (pawn.pather == null || !pawn.pather.Moving || !pawn.pather.Destination.IsValid)
                return;
            if (repathMethod == null)
            {
                if (!warnedRepath)
                {
                    warnedRepath = true;
                    Log.Warning("[Pony Auto Flight] SetNewPathRequest not found; automatic repath unavailable.");
                }
                return;
            }
            // Uses the current destination and path-end mode. Does not stop
            // movement, dispose the current route, or replace the current job.
            repathMethod.Invoke(pawn.pather, null);
        }
        public static void Land(Pawn pawn, ThingComp toggle)
        {
            SetEnabled(toggle, false);
            Type utility = AccessTools.TypeByName("PoniesOfTheRim.Flying.PegasusFlightUtility");
            MethodInfo safeLand = utility == null ? null : AccessTools.Method(
                utility, "SafeLand", new[] { typeof(Pawn) });
            safeLand?.Invoke(null, new object[] { pawn });
            RequestRepath(pawn);
        }
    }

    public class AutoFlightGameComponent : GameComponent
    {
        private List<PawnFlightSettings> settings = new List<PawnFlightSettings>();
        private readonly Dictionary<Pawn, PawnFlightSettings> lookup =
            new Dictionary<Pawn, PawnFlightSettings>();
        private bool reportedError;
        public AutoFlightGameComponent(Game game) { }
        public static AutoFlightGameComponent Current =>
            Verse.Current.Game?.GetComponent<AutoFlightGameComponent>();
        public PawnFlightSettings Get(Pawn pawn)
        {
            if (!lookup.TryGetValue(pawn, out PawnFlightSettings value))
            {
                value = new PawnFlightSettings(pawn);
                settings.Add(value);
                lookup[pawn] = value;
            }
            return value;
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref settings, "PAF_pawnSettings", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                settings = settings ?? new List<PawnFlightSettings>();
                lookup.Clear();
                foreach (PawnFlightSettings value in settings)
                    if (value?.pawn != null) lookup[value.pawn] = value;
            }
        }
        public override void GameComponentTick()
        {
            int evaluationInterval = Math.Max(1, Config.Current.evaluationInterval);
            int landingInterval = Math.Max(1, Config.Current.landingCheckInterval);
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
                {
                    if (!pawn.IsColonistPlayerControlled) continue;
                    bool evaluate = pawn.IsHashIntervalTick(evaluationInterval);
                    bool checkLanding = pawn.IsHashIntervalTick(landingInterval);
                    if (!evaluate && !checkLanding) continue;
                    try { Evaluate(pawn, evaluate); }
                    catch (Exception exception)
                    {
                        if (!reportedError)
                        {
                            reportedError = true;
                            Log.Error("[Pony Auto Flight] Evaluation failed:\n" + exception);
                        }
                    }
                }
            }
        }
        private static void TrackMovement(Pawn pawn, PawnFlightSettings state)
        {
            bool moving = pawn.pather != null && pawn.pather.Moving;
            if (!moving)
            {
                state.hasMovement = false;
                return;
            }
            LocalTargetInfo destination = pawn.pather.Destination;
            if (!state.hasMovement || state.trackedJob != pawn.CurJob
                || state.trackedDestination != destination)
            {
                state.trackedJob = pawn.CurJob;
                state.trackedDestination = destination;
                state.hasMovement = true;
                state.landedForSegment = false;
                // Do not discard an already pending safe landing.
            }
        }
        private static bool NearDestination(Pawn pawn)
        {
            if (pawn.pather == null || !pawn.pather.Moving) return false;
            LocalTargetInfo destination = pawn.pather.Destination;
            if (!destination.IsValid) return false;
            return pawn.Position.DistanceTo(destination.Cell)
                <= Math.Max(0f, Config.Current.landingDistance);
        }
        private void Evaluate(Pawn pawn, bool fullEvaluation)
        {
            ThingComp toggle = FlightBridge.Toggle(pawn);
            ThingComp timer = FlightBridge.Timer(pawn);
            if (toggle == null || timer == null) return;
            PawnFlightSettings state = Get(pawn);
            bool flying = FlightBridge.Enabled(toggle);
            if (pawn.Dead || pawn.Downed) return;
            TrackMovement(pawn, state);

            if (!flying)
            {
                if (state.autoStarted)
                {
                    state.landedForSegment = true;
                    state.autoStarted = false;
                }
                state.landingPending = false;
            }
            if (!state.flightAllowed)
            {
                if (flying) TryLand(pawn, toggle, state);
                return;
            }
            if (flying)
            {
                if (!state.autoStarted) return;
                bool stop = state.landingPending || NearDestination(pawn)
                    || pawn.Position.Roofed(pawn.Map)
                    || pawn.pather == null || !pawn.pather.Moving;
                if (fullEvaluation)
                {
                    float stamina = FlightBridge.Stamina(timer);
                    if (stamina >= 0f && stamina < state.stopStamina) stop = true;
                }
                if (stop)
                {
                    state.landingPending = true;
                    TryLand(pawn, toggle, state);
                }
                return;
            }
            if (!fullEvaluation || state.landedForSegment || NearDestination(pawn)) return;
            if (pawn.Position.Roofed(pawn.Map)) return;
            if (pawn.pather == null || !pawn.pather.Moving) return;
            PawnPath path = pawn.pather.curPath;
            if (path == null || !path.Found) return;
            int length = Math.Max(0, path.NodesLeftCount - 1);
            if (pawn.pather.nextCell.IsValid && pawn.pather.nextCell != pawn.Position) length++;
            if (length <= state.pathLength) return;
            float startStamina = FlightBridge.Stamina(timer);
            if (startStamina < 0f || startStamina <= state.startStamina) return;
            if (!FlightBridge.CanTakeOff(toggle)) return;
            // Prototype speed estimate; unchanged from the first version.
            if (pawn.RaceProps.flightSpeedFactor <= 1f) return;
            FlightBridge.SetEnabled(toggle, true);
            state.autoStarted = FlightBridge.Enabled(toggle);
            if (state.autoStarted) FlightBridge.RequestRepath(pawn);
        }
        private static void TryLand(Pawn pawn, ThingComp toggle, PawnFlightSettings state)
        {
            state.landingPending = true;
            if (pawn.Map == null || !pawn.Position.Walkable(pawn.Map)) return;
            FlightBridge.Land(pawn, toggle);
            if (!FlightBridge.Enabled(toggle))
            {
                state.autoStarted = false;
                state.landingPending = false;
                state.landedForSegment = true;
            }
        }
    }

    public class FlightSettingsWindow : Window
    {
        private readonly Pawn pawn;
        private readonly PawnFlightSettings settings;
        public override Vector2 InitialSize => new Vector2(460f, 400f);
        public FlightSettingsWindow(Pawn pawn)
        {
            this.pawn = pawn;
            settings = AutoFlightGameComponent.Current.Get(pawn);
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            AutoFlightConfigDef c = Config.Current;
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(rect.x, rect.y, rect.width, rect.height - 50f));
            listing.Label("PAF_Title".Translate() + ": " + pawn.LabelShortCap);
            listing.Gap();
            listing.CheckboxLabeled("PAF_Allowed".Translate(), ref settings.flightAllowed);
            listing.Gap();
            listing.Label("PAF_PathLength".Translate(settings.pathLength));
            settings.pathLength = Mathf.RoundToInt(listing.Slider(
                settings.pathLength, c.minPathLength, c.maxPathLength));
            listing.Gap();
            listing.Label("PAF_StartStamina".Translate(Mathf.RoundToInt(settings.startStamina * 100f)));
            settings.startStamina = Mathf.Round(listing.Slider(
                settings.startStamina, c.minStartStamina, c.maxStartStamina) * 100f) / 100f;
            listing.Gap();
            listing.Label("PAF_StopStamina".Translate(Mathf.RoundToInt(settings.stopStamina * 100f)));
            settings.stopStamina = Mathf.Round(listing.Slider(
                settings.stopStamina, c.minStopStamina, c.maxStopStamina) * 100f) / 100f;
            listing.Gap();
            if (listing.ButtonText("PAF_Reset".Translate())) settings.ResetThresholds();
            listing.End();
        }
    }

    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private static readonly Texture2D AutoFlightSettingsIcon =
            ContentFinder<Texture2D>.Get("UI/AutoFlightSettings", reportFailure: false);

        static Bootstrap()
        {
            Harmony harmony = new Harmony("local.ponyautoflight");
            harmony.Patch(AccessTools.Method(typeof(Pawn), "GetGizmos"),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(AddSettingsGizmo)));
            Type toggleType = AccessTools.TypeByName("PoniesOfTheRim.Flying.CompPegasusFlightToggle");
            MethodInfo setter = toggleType == null ? null :
                AccessTools.PropertySetter(toggleType, "FlightEnabled");
            if (setter != null)
                harmony.Patch(setter, prefix: new HarmonyMethod(typeof(Bootstrap), nameof(AllowFlightSwitch)));
            else
                Log.Warning("[Pony Auto Flight] Flight toggle setter not found. Check mod load order.");
        }
        public static void AddSettingsGizmo(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!__instance.IsColonistPlayerControlled || FlightBridge.Toggle(__instance) == null
                || AutoFlightGameComponent.Current == null) return;
            Pawn pawn = __instance;
            Command_Action command = new Command_Action
            {
                defaultLabel = "PAF_Command".Translate(),
                defaultDesc = "PAF_CommandDesc".Translate(),
                icon = AutoFlightSettingsIcon ?? BaseContent.WhiteTex,
                action = () => Find.WindowStack.Add(new FlightSettingsWindow(pawn))
            };
            __result = __result.Concat(new Gizmo[] { command });
        }
        public static bool AllowFlightSwitch(ThingComp __instance, bool __0)
        {
            if (!__0) return true;
            Pawn pawn = __instance.parent as Pawn;
            if (pawn == null || !pawn.IsColonistPlayerControlled
                || AutoFlightGameComponent.Current == null) return true;
            return AutoFlightGameComponent.Current.Get(pawn).flightAllowed;
        }
    }
}
