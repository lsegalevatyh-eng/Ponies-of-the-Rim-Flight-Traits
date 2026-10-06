using HarmonyLib;
using RimWorld;
using Verse;

namespace FlightTraits
{
    // Регистрируется Harmony через существующий Bootstrap аддона.
    [HarmonyPatch]
    public static class FlightTraitsGenerationFilter
    {
        [System.ThreadStatic]
        private static Pawn currentTraitPawn;

        [System.ThreadStatic]
        private static Pawn currentBioPawn;

        private static bool CanReceiveFlightDef(Pawn pawn)
        {
            return FlightTraitUtility.IsEligible(pawn);
        }

        private static bool IsFlightDef(Def def)
        {
            return def != null &&
                   def.GetModExtension<FlightTraitExtension>() != null;
        }

        // PawnGenerator.GenerateTraitsFor выбирает случайный TraitDef
        // с весом TraitDef.GetGenderSpecificCommonality().
        [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GenerateTraitsFor))]
        [HarmonyPrefix]
        private static void BeforeGenerateTraitsFor(Pawn pawn, out Pawn __state)
        {
            __state = currentTraitPawn;
            currentTraitPawn = pawn;
        }

        [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GenerateTraitsFor))]
        [HarmonyFinalizer]
        private static System.Exception AfterGenerateTraitsFor(
            System.Exception __exception, Pawn __state)
        {
            currentTraitPawn = __state;
            return __exception;
        }

        [HarmonyPatch(typeof(TraitDef), nameof(TraitDef.GetGenderSpecificCommonality))]
        [HarmonyPostfix]
        private static void FilterTraitWeight(TraitDef __instance, ref float __result)
        {
            if (currentTraitPawn != null &&
                IsFlightDef(__instance) &&
                !CanReceiveFlightDef(currentTraitPawn))
            {
                __result = 0f;
            }
        }

        // Охватывает как перемешанные, так и готовые биографии.
        [HarmonyPatch(typeof(PawnBioAndNameGenerator),
            nameof(PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo))]
        [HarmonyPrefix]
        private static void BeforeGenerateBio(Pawn pawn, out Pawn __state)
        {
            __state = currentBioPawn;
            currentBioPawn = pawn;
        }

        [HarmonyPatch(typeof(PawnBioAndNameGenerator),
            nameof(PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo))]
        [HarmonyFinalizer]
        private static System.Exception AfterGenerateBio(
            System.Exception __exception, Pawn __state)
        {
            currentBioPawn = __state;
            return __exception;
        }

        // Отсекает BackstoryDef до его попадания в список случайных кандидатов.
        [HarmonyPatch(typeof(BackstoryCategoryFilter), nameof(BackstoryCategoryFilter.Matches),
            new[] { typeof(BackstoryDef) })]
        [HarmonyPostfix]
        private static void FilterShuffledBackstory(
            BackstoryDef __0, ref bool __result)
        {
            if (__result &&
                currentBioPawn != null &&
                IsFlightDef(__0) &&
                !CanReceiveFlightDef(currentBioPawn))
            {
                __result = false;
            }
        }

        // Отсекает готовую биографию, если любая из её предысторий
        // предназначена для летающей расы.
        [HarmonyPatch(typeof(PawnBioAndNameGenerator), "IsBioUseable")]
        [HarmonyPostfix]
        private static void FilterSolidBio(PawnBio bio, ref bool __result)
        {
            if (__result &&
                currentBioPawn != null &&
                !CanReceiveFlightDef(currentBioPawn) &&
                (IsFlightDef(bio.childhood) || IsFlightDef(bio.adulthood)))
            {
                __result = false;
            }
        }
    }
}