using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ModularArmor
{
    // A range added by an upgrade.
    // setting: Capacity, Protection, Recharge.
    public class ShieldSettingRange
    {
        public string setting = "Capacity";
        public float min = 1.5f;
        public float max = 3f;
        public float coefficient = 1f;

        // Negative values use the legacy coefficient as a fallback.
        public float coefficientBelow = -1f;
        public float coefficientAbove = -1f;
    }

    public class ModuleExtension : DefModExtension
    {
        public bool forCuirass;
        public bool forHelmet;

        public int slots = 1;
        public int maxCopies = 1;
        public string exclusiveGroup;

        public int installTicks = 2500;
        public int removeTicks = 2500;

        public bool active;
        public float consumption;
        public float consumptionIdle = -1f;

        public float coreCapacity;
        public float coreRecharge;
        public float batteryCapacity;

        public ThingDef reactorFuel;
        public int reactorFuelCapacity = 60;
        public int reactorFuelPerCycle = 1;
        public int reactorCycleTicks = 2500;

        public bool shield;
        public float shieldMax = 1000f;
        public float shieldLossPerDamage = 30f;
        public float shieldRecharge = 118.1818f;

        // Legacy batch fields retained for XML compatibility.
        // Shield repair now uses consumption as the price per restored point.
        public float shieldRepairBatch = 50f;
        public float shieldRepairBatchCost = 1.1f;

        public float shieldRestartCost = 50f;
        public int shieldResetTicks = 3200;
        public float shieldResetFraction = 0.181818f;
        public bool blocksShooting = true;

        // Generator setting ranges. Value 1 is always the baseline.
        public float shieldCapacityMin = 0.5f;
        public float shieldCapacityMax = 1.5f;
        public float shieldProtectionMin = 0.5f;
        public float shieldProtectionMax = 1.5f;
        public float shieldRechargeMin = 0.5f;
        public float shieldRechargeMax = 1.5f;

        // Linear cost slopes inside the generator ranges.
        public float shieldCapacityCostCoefficient = 1f;
        public float shieldDamageCostCoefficient = 1f;
        public float shieldRechargeCostCoefficient = 1f;

        // Independent slopes below and above the baseline value 1.
        // Negative values use the corresponding legacy coefficient.
        public float shieldCapacityCostCoefficientBelow = -1f;
        public float shieldCapacityCostCoefficientAbove = -1f;
        public float shieldProtectionCostCoefficientBelow = -1f;
        public float shieldProtectionCostCoefficientAbove = -1f;
        public float shieldRechargeCostCoefficientBelow = -1f;
        public float shieldRechargeCostCoefficientAbove = -1f;

        // Each upgrade can add one or more independently priced ranges.
        public List<ShieldSettingRange> shieldSettingRanges;

        // Legacy fields retained for XML compatibility.
        public float shieldCapacityFactor = 1f;
        public float shieldConsumptionFactor = 1f;
        public float shieldRepairCostFactor = 1f;
        public float shieldRestartCostFactor = 1f;
        public float shieldDamageFactor = 1f;

        public float moveSpeed;

        public bool overload;
        public float overloadMoveSpeed = 1f;
        public float overloadExtraConsumption = 4f;
        public int overloadDuration = 2500;
        public int overloadCooldown = 7500;

        public bool criticalOverload;
        public float criticalOverloadMoveSpeed = 4f;
        public float criticalOverloadExtraConsumption = 8f;
        public int criticalOverloadDuration = 2500;
        public int criticalOverloadCooldown = 7500;

        public int criticalArmorDamage = 1;
        public int criticalArmorDamageInterval = 250;
        public int criticalMinimumHitPoints = 1;

        public List<StatModifier> statOffsets =
            new List<StatModifier>();
    }

    public class CompProperties_ModuleSlots : CompProperties
    {
        public int slotCount = 4;
        public bool allowArmorReinforcement;
        public List<int> slotsByQuality;

        public CompProperties_ModuleSlots()
            : base(typeof(CompModuleSlots))
        {
        }

        public int SlotsFor(QualityCategory quality)
        {
            int index = (int)quality;

            return slotsByQuality != null &&
                   index >= 0 &&
                   index < slotsByQuality.Count
                ? Math.Max(0, slotsByQuality[index])
                : Math.Max(0, slotCount);
        }

        public override IEnumerable<string> ConfigErrors(
            ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;

            if (slotsByQuality == null)
                yield break;

            if (slotsByQuality.Count != 7)
                yield return "ModularArmor: slotsByQuality needs 7 values.";

            if (slotsByQuality.Any(v => v < 0))
                yield return "ModularArmor: negative slot count.";
        }
    }
}