using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class CloakingModuleExtension : DefModExtension
    {
        public HediffDef hediff;

        // Four in-game hours.
        public int durationTicks = 10000;

        public int cooldownTicks = 1800;

        // Energy paid once per consumption interval.
        public float energyPerInterval = 16f;
        public int energyIntervalTicks = 60;

        public bool breakOnAttack = true;

        public string iconPath = "UI/Abilities/Invisibility";
    }

    public class ArmorCloakingSystem : ArmorSystem
    {
        private int activeUntil;
        private int cooldownUntil;
        private int nextEnergyPaymentTick;

        private Hediff effect;
        private Pawn effectPawn;
        private ThingDef activeModule;

        private int Now => Find.TickManager.TicksGame;

        private ThingDef Module =>
            Host.InstalledModules.FirstOrDefault(
                d => d.GetModExtension<CloakingModuleExtension>() != null);

        private CloakingModuleExtension Settings =>
            Module?.GetModExtension<CloakingModuleExtension>();

        private bool Active => effect != null;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref activeUntil, "activeUntil", 0);
            Scribe_Values.Look(ref cooldownUntil, "cooldownUntil", 0);

            Scribe_Values.Look(
                ref nextEnergyPaymentTick,
                "nextEnergyPaymentTick",
                0);

            Scribe_References.Look(ref effect, "effect");
            Scribe_References.Look(ref effectPawn, "effectPawn");
            Scribe_Defs.Look(ref activeModule, "activeModule");
        }

        public override void Tick()
        {
            if (!Active)
                return;

            if (effectPawn == null ||
                effectPawn.Dead ||
                Host.Wearer != effectPawn ||
                Module != activeModule ||
                Now >= activeUntil ||
                !effectPawn.health.hediffSet.hediffs.Contains(effect))
            {
                Stop();
                return;
            }

            CloakingModuleExtension settings =
                activeModule?.GetModExtension<CloakingModuleExtension>();

            if (settings == null)
            {
                Stop();
                return;
            }

            int interval = Math.Max(1, settings.energyIntervalTicks);

            // Initialize the timer for saves made before interval payments.
            if (nextEnergyPaymentTick <= 0)
            {
                nextEnergyPaymentTick = Now + interval;
                return;
            }

            if (Now < nextEnergyPaymentTick)
                return;

            float cost = Mathf.Max(0f, settings.energyPerInterval);

            // Disruption does not suspend energy consumption.
            if (cost > 0f)
            {
                IArmorEnergy energy = Host.GetSystem<ArmorEnergySystem>();

                if (energy == null || !energy.TrySpend(cost))
                {
                    Stop();
                    return;
                }
            }

            nextEnergyPaymentTick = Now + interval;
        }

        public override void ModulesChanged()
        {
            if (Active && Module != activeModule)
                Stop();
        }

        public override void ModuleRemoved(ThingDef module)
        {
            if (Active && module == activeModule)
                Stop();
        }

        private string BlockedReason()
        {
            CloakingModuleExtension settings = Settings;
            Pawn pawn = Host.Wearer;

            if (!ModsConfig.RoyaltyActive)
            {
                return ModuleUtility.T(
                    "MA_CloakRequiresRoyalty",
                    "Requires Royalty.");
            }

            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return ModuleUtility.T(
                    "MA_CloakUnavailable",
                    "The wearer must be standing on a map.");
            }

            if (settings?.hediff == null)
            {
                return ModuleUtility.T(
                    "MA_CloakMissingEffect",
                    "Cloaking effect is not configured.");
            }

            if (Now < cooldownUntil)
            {
                return string.Format(
                    ModuleUtility.T(
                        "MA_CloakCooldown",
                        "Cooldown: {0:0.0} seconds"),
                    (cooldownUntil - Now) / 60f);
            }

            // Do not stack armor cloaking with another invisibility effect.
            if (pawn.GetInvisibilityComp() != null)
            {
                return ModuleUtility.T(
                    "MA_CloakOtherEffect",
                    "Another invisibility effect is already active.");
            }

            float cost = Mathf.Max(0f, settings.energyPerInterval);

            // Require enough stored energy for one payment.
            // Activation itself does not consume energy.
            if (cost > 0f)
            {
                IArmorEnergy energy = Host.GetSystem<ArmorEnergySystem>();

                if (energy == null || energy.Energy < cost)
                {
                    return ModuleUtility.T(
                        "MA_CloakNoEnergy",
                        "Not enough stored energy.");
                }
            }

            return null;
        }

        private void Start()
        {
            if (Active || BlockedReason() != null)
                return;

            CloakingModuleExtension settings = Settings;
            Pawn pawn = Host.Wearer;

            Hediff created =
                HediffMaker.MakeHediff(settings.hediff, pawn);

            HediffComp_Disappears timer =
                created.TryGetComp<HediffComp_Disappears>();

            if (timer == null ||
                created.TryGetComp<HediffComp_Invisibility>() == null)
            {
                Log.Error(
                    "[ModularArmor] Cloaking hediff requires " +
                    "Disappears and Invisibility components.");
                return;
            }

            int duration = Math.Max(1, settings.durationTicks);

            timer.ticksToDisappear = duration;

            effect = created;
            effectPawn = pawn;
            activeModule = Module;
            activeUntil = Now + duration;

            // The first payment occurs after one full interval.
            nextEnergyPaymentTick =
                Now + Math.Max(1, settings.energyIntervalTicks);

            pawn.health.AddHediff(created);
        }

        private void Stop()
        {
            if (!Active)
                return;

            CloakingModuleExtension settings =
                activeModule?.GetModExtension<CloakingModuleExtension>();

            cooldownUntil = Math.Max(
                cooldownUntil,
                Now + Math.Max(0, settings?.cooldownTicks ?? 1800));

            Pawn pawn = effectPawn;
            Hediff oldEffect = effect;

            effect = null;
            effectPawn = null;
            activeModule = null;
            activeUntil = 0;
            nextEnergyPaymentTick = 0;

            if (pawn?.health != null &&
                pawn.health.hediffSet.hediffs.Contains(oldEffect))
            {
                pawn.health.RemoveHediff(oldEffect);
            }

            if (pawn != null)
            {
                // Preserve another invisibility effect if it appeared later.
                HediffComp_Invisibility remaining =
                    pawn.GetInvisibilityComp();

                if (remaining != null)
                    remaining.BecomeInvisible(instant: true);

                if (pawn.Spawned)
                    pawn.Map.attackTargetsCache.UpdateTarget(pawn);

                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }

        public void NotifySuccessfulAttack()
        {
            if (!Active)
                return;

            CloakingModuleExtension settings =
                activeModule?.GetModExtension<CloakingModuleExtension>();

            if (settings?.breakOnAttack == true)
                Stop();
        }

        public override IEnumerable<Gizmo> GetWornGizmos()
        {
            if (Module == null)
                yield break;

            CloakingModuleExtension settings = Settings;

            string description = ModuleUtility.T(
                "MA_CloakDescription",
                "Toggle armor cloaking. Attacking ends cloaking. " +
                "Incoming damage temporarily disrupts it.");

            description += "\n" + string.Format(
                ModuleUtility.T(
                    "MA_CloakEnergyInterval",
                    "Energy consumption: {0:0.##} every {1} ticks"),
                Mathf.Max(0f, settings.energyPerInterval),
                Math.Max(1, settings.energyIntervalTicks));

            if (Active)
            {
                description += "\n" + string.Format(
                    ModuleUtility.T(
                        "MA_CloakRemainingHours",
                        "Remaining: {0:0.00} in-game hours"),
                    Math.Max(0, activeUntil - Now) / 2500f);
            }

            Command_Toggle command = new Command_Toggle
            {
                defaultLabel = ModuleUtility.T(
                    "MA_CloakLabel",
                    "Cloaking"),

                defaultDesc = description,

                icon = ContentFinder<Texture2D>.Get(
                    settings.iconPath, false) ?? BaseContent.BadTex,

                isActive = () => Active,

                toggleAction = () =>
                {
                    if (Active)
                        Stop();
                    else
                        Start();
                }
            };

            if (!Active)
            {
                string reason = BlockedReason();

                if (reason != null)
                    command.Disable(reason);
            }

            yield return command;
        }
    }

    [StaticConstructorOnStartup]
    public static class CloakingAttackPatches
    {
        static CloakingAttackPatches()
        {
            Harmony harmony =
                new Harmony("ModularArmor.Cloaking.Attacks");

            MethodInfo postfix = AccessTools.Method(
                typeof(CloakingAttackPatches),
                nameof(AfterCastShot));

            HashSet<MethodInfo> patched = new HashSet<MethodInfo>();

            foreach (Type type in GenTypes.AllTypes)
            {
                if (!typeof(Verb).IsAssignableFrom(type))
                    continue;

                MethodInfo method = type.GetMethod(
                    "TryCastShot",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);

                if (method == null ||
                    method.IsAbstract ||
                    method.ReturnType != typeof(bool) ||
                    !patched.Add(method))
                {
                    continue;
                }

                try
                {
                    harmony.Patch(
                        method,
                        postfix: new HarmonyMethod(postfix));
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[ModularArmor] Cannot patch cloaking attack hook: " +
                        type.FullName + "\n" + exception);
                }
            }
        }

        private static void AfterCastShot(
            Verb __instance,
            bool __result)
        {
            if (!__result)
                return;

            // Utility verbs should not interrupt cloaking.
            if (!(__instance is Verb_LaunchProjectile) &&
                !(__instance is Verb_MeleeAttack))
            {
                return;
            }

            Pawn pawn = __instance.CasterPawn;

            if (pawn?.apparel == null)
                return;

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                apparel.TryGetComp<CompModuleSlots>()
                    ?.GetSystem<ArmorCloakingSystem>()
                    ?.NotifySuccessfulAttack();
            }
        }
    }
}