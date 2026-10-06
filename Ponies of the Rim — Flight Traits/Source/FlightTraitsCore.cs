using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using PoniesOfTheRim.Flying;

namespace FlightTraits
{
    public sealed class FlightTraitExtension : DefModExtension
    {
        public int degree = 0;
        public float maxStaminaFactor = 1f;
        public float recoveryFactor = 1f;
        public float flightSpeedFactor = 1f;
        public float generationChance = 0f;
        public float injuryChancePerFlight = 0f;
        public HediffDef wingInjury;
        public List<BodyPartDef> wingBodyParts;
        public float injurySeverity = 1f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!Positive(maxStaminaFactor)) yield return "maxStaminaFactor must be finite and > 0.";
            if (!Positive(recoveryFactor)) yield return "recoveryFactor must be finite and > 0.";
            if (!Positive(flightSpeedFactor)) yield return "flightSpeedFactor must be finite and > 0.";
            if (!Probability(generationChance)) yield return "generationChance must be between 0 and 1.";
            if (!Probability(injuryChancePerFlight)) yield return "injuryChancePerFlight must be between 0 and 1.";
            if (injuryChancePerFlight > 0f)
            {
                if (wingInjury == null) yield return "wingInjury is required.";
                if (wingBodyParts == null || wingBodyParts.Count == 0) yield return "wingBodyParts is required.";
                if (!Positive(injurySeverity)) yield return "injurySeverity must be finite and > 0.";
            }
        }

        private static bool Positive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Probability(float value)
        {
            return !float.IsNaN(value) && value >= 0f && value <= 1f;
        }
    }

    public struct FlightModifiers
    {
        public float MaxStaminaFactor;
        public float RecoveryFactor;
        public float FlightSpeedFactor;

        public static FlightModifiers Identity
        {
            get
            {
                return new FlightModifiers
                {
                    MaxStaminaFactor = 1f,
                    RecoveryFactor = 1f,
                    FlightSpeedFactor = 1f
                };
            }
        }
    }

    public static class FlightTraitUtility
    {
        public static bool IsEligible(Pawn pawn)
        {
            return pawn != null && pawn.def != null && pawn.story != null
                && pawn.TryGetComp<CompPegasusFlightTimer>() != null
                && pawn.TryGetComp<CompPegasusFlightToggle>() != null;
        }

        public static FlightTraitExtension GetExtension(Trait trait)
        {
            if (trait == null || trait.def == null) return null;
            FlightTraitExtension extension = trait.def.GetModExtension<FlightTraitExtension>();
            return extension != null && extension.degree == trait.Degree ? extension : null;
        }

        public static FlightModifiers GetModifiers(Pawn pawn)
        {
            FlightModifiers result = FlightModifiers.Identity;
            if (!IsEligible(pawn)) return result;
            if (pawn.story.traits != null)
            {
                List<Trait> traits = pawn.story.traits.allTraits;
                for (int i = 0; i < traits.Count; i++)
                {
                    ApplyExtension(ref result, GetExtension(traits[i]));
                }
            }
            // BackstoryDef хранит modExtensions на самом Def, не внутри degreeDatas.
            if (pawn.story.Childhood != null)
                ApplyExtension(ref result, pawn.story.Childhood.GetModExtension<FlightTraitExtension>());
            if (pawn.story.Adulthood != null)
                ApplyExtension(ref result, pawn.story.Adulthood.GetModExtension<FlightTraitExtension>());
            result.MaxStaminaFactor = SafeFactor(result.MaxStaminaFactor);
            result.RecoveryFactor = SafeFactor(result.RecoveryFactor);
            result.FlightSpeedFactor = SafeFactor(result.FlightSpeedFactor);
            return result;
        }

        private static void ApplyExtension(ref FlightModifiers result, FlightTraitExtension extension)
        {
            if (extension == null) return;
            result.MaxStaminaFactor *= SafeFactor(extension.maxStaminaFactor);
            result.RecoveryFactor *= SafeFactor(extension.recoveryFactor);
            result.FlightSpeedFactor *= SafeFactor(extension.flightSpeedFactor);
        }

        private static float SafeFactor(float factor)
        {
            return factor > 0f && !float.IsNaN(factor) && !float.IsInfinity(factor) ? factor : 1f;
        }
    }
}
