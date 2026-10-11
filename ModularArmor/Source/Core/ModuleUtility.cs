using System;
using RimWorld;
using Verse;

namespace ModularArmor
{
    public static class ModuleUtility
    {
        public static ModuleExtension Info(ThingDef def)
        {
            return def?.GetModExtension<ModuleExtension>();
        }

        public static string T(string key, string englishFallback)
        {
            return key.CanTranslate()
                ? key.Translate().ToString()
                : englishFallback;
        }

        // Compatibility overload for extensions using the previous API.
        // The old language-specific fallback is intentionally ignored.
        public static string T(
            string key,
            string unusedFallback,
            string englishFallback)
        {
            return T(key, englishFallback);
        }

        public static string Bonus(StatDef stat, float value)
        {
            string sign = value >= 0f ? "+" : "";

            if (stat == StatDefOf.ArmorRating_Sharp ||
                stat == StatDefOf.ArmorRating_Blunt ||
                stat == StatDefOf.ArmorRating_Heat)
            {
                return sign + (value * 100f).ToString("0.#") +
                    T("MA_PP", " percentage points");
            }

            string suffix =
                stat == StatDefOf.Insulation_Cold ||
                stat == StatDefOf.Insulation_Heat
                    ? " °C"
                    : "";

            return sign + value.ToString("0.##") + suffix;
        }

        public static string TimeText(int ticks)
        {
            return (Math.Max(0, ticks) / 2500f).ToString("0.00") +
                T("MA_GameHoursShort", " h");
        }

        public static void Reject(string text)
        {
            Messages.Message(
                text,
                MessageTypeDefOf.RejectInput,
                false);
        }
    }
}