using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class NightVisionModuleExtension : DefModExtension
    {
        // Energy consumed per 60 simulation ticks.
        public float energyPerSecond = 0.1f;

        public string iconPath = "UI/Commands/ForbidOff";
    }

    public class ArmorNightVisionSystem : ArmorSystem
    {
        private bool enabled;

        private ThingDef Module =>
            Host.InstalledModules.FirstOrDefault(
                d => d.GetModExtension<NightVisionModuleExtension>() != null);

        private NightVisionModuleExtension Settings =>
            Module?.GetModExtension<NightVisionModuleExtension>();

        public override void ExposeData()
        {
            Scribe_Values.Look(
                ref enabled,
                "nightVisionEnabled",
                false);
        }

        public bool IsWorking
        {
            get
            {
                if (!enabled)
                    return false;

                Pawn pawn = Host.Wearer;
                NightVisionModuleExtension settings = Settings;

                if (pawn == null ||
                    pawn.Dead ||
                    !pawn.Spawned ||
                    settings == null)
                {
                    return false;
                }

                float costPerTick =
                    Mathf.Max(0f, settings.energyPerSecond) / 60f;

                if (costPerTick <= 0f)
                    return true;

                // Host is the helmet, so this uses the helmet's own energy.
                IArmorEnergy energy =
                    Host.GetSystem<ArmorEnergySystem>();

                return energy != null &&
                    energy.Energy >= costPerTick;
            }
        }

        public override void Tick()
        {
            if (!enabled)
                return;

            Pawn pawn = Host.Wearer;
            NightVisionModuleExtension settings = Settings;

            if (pawn == null || pawn.Dead || settings == null)
            {
                enabled = false;
                return;
            }

            float costPerTick =
                Mathf.Max(0f, settings.energyPerSecond) / 60f;

            if (costPerTick <= 0f)
                return;

            IArmorEnergy energy =
                Host.GetSystem<ArmorEnergySystem>();

            if (energy == null || !energy.TrySpend(costPerTick))
                enabled = false;
        }

        public override void ModulesChanged()
        {
            if (Module == null)
                enabled = false;
        }

        public override void ModuleRemoved(ThingDef module)
        {
            if (module.GetModExtension<NightVisionModuleExtension>() != null)
                enabled = false;
        }

        private string BlockedReason()
        {
            Pawn pawn = Host.Wearer;
            NightVisionModuleExtension settings = Settings;

            if (pawn == null || pawn.Dead || !pawn.Spawned)
            {
                return ModuleUtility.T(
                    "MA_NightVisionUnavailable",
                    "The wearer must be on a map.");
            }

            if (settings == null)
            {
                return ModuleUtility.T(
                    "MA_NightVisionMissingModule",
                    "No night vision module installed.");
            }

            float costPerTick =
                Mathf.Max(0f, settings.energyPerSecond) / 60f;

            if (costPerTick > 0f)
            {
                IArmorEnergy energy =
                    Host.GetSystem<ArmorEnergySystem>();

                if (energy == null || energy.Energy < costPerTick)
                {
                    return ModuleUtility.T(
                        "MA_NightVisionNoEnergy",
                        "Not enough helmet energy.");
                }
            }

            return null;
        }

        public override IEnumerable<Gizmo> GetWornGizmos()
        {
            NightVisionModuleExtension settings = Settings;

            if (settings == null)
                yield break;

            string description = ModuleUtility.T(
                "MA_NightVisionDescription",
                "Toggle helmet night vision. Removes darkness penalties " +
                "applied by lighting stat multipliers. Continuously " +
                "consumes helmet energy, including in daylight.");

            description += "\n" + string.Format(
                ModuleUtility.T(
                    "MA_NightVisionEnergyRate",
                    "Energy consumption: {0:0.##} per second"),
                Mathf.Max(0f, settings.energyPerSecond));

            Command_Toggle command = new Command_Toggle
            {
                defaultLabel = ModuleUtility.T(
                    "MA_NightVisionLabel",
                    "Night vision"),

                defaultDesc = description,

                icon = ContentFinder<Texture2D>.Get(
                    settings.iconPath, false) ?? BaseContent.BadTex,

                isActive = () => enabled,

                toggleAction = () =>
                {
                    if (enabled)
                        enabled = false;
                    else if (BlockedReason() == null)
                        enabled = true;
                }
            };

            // Switching off must always remain possible.
            if (!enabled)
            {
                string reason = BlockedReason();

                if (reason != null)
                    command.Disable(reason);
            }

            yield return command;
        }

        public static bool WorkingFor(Pawn pawn)
        {
            if (pawn?.apparel == null)
                return false;

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                ArmorNightVisionSystem system =
                    apparel.TryGetComp<CompModuleSlots>()
                        ?.GetSystem<ArmorNightVisionSystem>();

                if (system != null && system.IsWorking)
                    return true;
            }

            return false;
        }
    }

    [StaticConstructorOnStartup]
    public static class NightVisionPatches
    {
        static NightVisionPatches()
        {
            Harmony harmony =
                new Harmony("ModularArmor.NightVision");

            harmony.Patch(
                AccessTools.Method(
                    typeof(StatPart_Glow),
                    "FactorFromGlow"),

                postfix: new HarmonyMethod(
                    typeof(NightVisionPatches),
                    nameof(AfterFactorFromGlow)));
        }

        private static void AfterFactorFromGlow(
            Thing t,
            ref float __result)
        {
            // Preserve neutral multipliers and bonuses.
            if (__result >= 1f)
                return;

            if (t is Pawn pawn &&
                ArmorNightVisionSystem.WorkingFor(pawn))
            {
                __result = 1f;
            }
        }
    }
}