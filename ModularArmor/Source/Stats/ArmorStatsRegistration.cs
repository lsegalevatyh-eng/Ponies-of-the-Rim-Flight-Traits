using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class StatPart_ModuleArmor : StatPart
    {
        private float Bonus(StatRequest req)
        {
            if (!req.HasThing)
                return 0f;

            CompModuleSlots comp =
                (req.Thing as ThingWithComps)
                    ?.GetComp<CompModuleSlots>();

            return comp?.StatOffset(parentStat) ?? 0f;
        }

        public override void TransformValue(
            StatRequest req,
            ref float val)
        {
            val += Bonus(req);
        }

        public override string ExplanationPart(
            StatRequest req)
        {
            float bonus = Bonus(req);

            if (Mathf.Approximately(bonus, 0f))
                return null;

            return string.Format(
                ModuleUtility.T(
                    "MA_StatExplanation",
                    "Installed modules: {0}"),
                ModuleUtility.Bonus(
                    parentStat,
                    bonus));
        }
    }

    [StaticConstructorOnStartup]
    public static class ArmorStatsRegistration
    {
        static ArmorStatsRegistration()
        {
            var stats = new HashSet<StatDef>();

            foreach (ArmorSystemDef system in
                DefDatabase<ArmorSystemDef>.AllDefsListForReading)
            {
                if (system.affectedStats != null)
                {
                    foreach (StatDef stat in system.affectedStats)
                    {
                        if (stat != null)
                            stats.Add(stat);
                    }
                }
            }

            // Register stat offsets declared directly in module XML.
            foreach (ThingDef def in
                DefDatabase<ThingDef>.AllDefsListForReading)
            {
                ModuleExtension info = ModuleUtility.Info(def);

                if (info?.statOffsets != null)
                {
                    foreach (StatModifier modifier in info.statOffsets)
                    {
                        if (modifier.stat != null)
                            stats.Add(modifier.stat);
                    }
                }
            }

            foreach (StatDef stat in stats)
                Add(stat);
        }

        private static void Add(StatDef stat)
        {
            if (stat.parts == null)
                stat.parts = new List<StatPart>();

            if (!stat.parts.Any(p => p is StatPart_ModuleArmor))
            {
                stat.parts.Add(new StatPart_ModuleArmor
                {
                    parentStat = stat,
                    priority = 1000f
                });
            }

            stat.immutable = false;
            stat.Worker.SetCacheability(false);
            stat.Worker.TryClearCache();
        }
    }
}