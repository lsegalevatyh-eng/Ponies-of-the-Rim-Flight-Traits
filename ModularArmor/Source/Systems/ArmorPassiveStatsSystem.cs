using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ModularArmor
{
    public class ArmorPassiveStatsSystem : ArmorSystem
    {
        public override float StatOffset(StatDef stat)
        {
            float result = 0f;

            foreach (ThingDef module in Host.InstalledModules)
            {
                ModuleExtension x = ModuleUtility.Info(module);

                if (x == null || x.statOffsets == null ||
                    !Host.IsModuleEffective(module))
                    continue;

                foreach (StatModifier modifier in x.statOffsets)
                    if (modifier.stat == stat)
                        result += modifier.value;
            }

            return result;
        }

        public override IEnumerable<string> DescribeModule(ThingDef module)
        {
            ModuleExtension x = ModuleUtility.Info(module);

            if (x?.statOffsets == null)
                yield break;

            foreach (StatModifier modifier in x.statOffsets)
                if (modifier.stat != null)
                    yield return modifier.stat.LabelCap + ": " +
                        ModuleUtility.Bonus(
                            modifier.stat, modifier.value);
        }
    }
}