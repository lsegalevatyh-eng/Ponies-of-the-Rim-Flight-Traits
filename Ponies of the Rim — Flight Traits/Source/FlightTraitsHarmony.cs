using System;
using System.Reflection;
using HarmonyLib;
using PoniesOfTheRim.Flying;
using UnityEngine;
using Verse;

namespace FlightTraits
{
    [StaticConstructorOnStartup]
    public static class FlightTraitsBootstrap
    {
        static FlightTraitsBootstrap()
        {
            Harmony harmony = new Harmony("flighttraits.poniesoftherim");
            InstallPatch(harmony,
                AccessTools.PropertyGetter(typeof(CompPegasusFlightTimer), "MaxFlightDurationTicks"),
                typeof(MaxFlightDurationPatch));
            InstallPatch(harmony,
                AccessTools.Method(typeof(CompPegasusFlightTimer), "RecoveryPerTick",
                    new Type[] { typeof(Pawn), typeof(float) }),
                typeof(RecoveryPerTickPatch));

            try
            {
                harmony.CreateClassProcessor(typeof(FlightTraitsGenerationFilter)).Patch();
                Log.Message("[FlightTraits] Фильтр генерации лётных черт и предысторий подключён.");
            }
            catch (Exception exception)
            {
                Log.Error("[FlightTraits] Ошибка подключения фильтра генерации:\n" + exception);
            }
        }

        private static void InstallPatch(Harmony harmony, MethodInfo target, Type patchType)
        {
            if (target == null)
            {
                Log.Error("[FlightTraits] Target not found: " + patchType.Name
                    + ". Check the installed Ponies of the Rim version.");
                return;
            }
            MethodInfo postfix = AccessTools.Method(patchType, "Postfix");
            if (postfix == null)
            {
                Log.Error("[FlightTraits] Postfix not found: " + patchType.FullName);
                return;
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                Log.Message("[FlightTraits] Installed: " + patchType.Name);
            }
            catch (Exception exception)
            {
                Log.Error("[FlightTraits] Failed: " + patchType.Name + "\n" + exception);
            }
        }
    }

    internal static class MaxFlightDurationPatch
    {
        public static void Postfix(CompPegasusFlightTimer __instance, ref int __result)
        {
            if (__result <= 0) return;
            Pawn pawn = __instance.parent as Pawn;
            if (!FlightTraitUtility.IsEligible(pawn)) return;
            float factor = FlightTraitUtility.GetModifiers(pawn).MaxStaminaFactor;
            if (factor == 1f) return;
            double duration = (double)__result * factor;
            __result = duration >= int.MaxValue
                ? int.MaxValue
                : Mathf.Max(1, Mathf.RoundToInt((float)duration));
        }
    }

    internal static class RecoveryPerTickPatch
    {
        // __0 binds to the first original argument, independently of its name.
        public static void Postfix(Pawn __0, ref float __result)
        {
            if (__result <= 0f || !FlightTraitUtility.IsEligible(__0)) return;
            __result *= FlightTraitUtility.GetModifiers(__0).RecoveryFactor;
        }
    }
}
