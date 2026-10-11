using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ModularArmor
{
    [StaticConstructorOnStartup]
    public class ArmorShieldSystem : ArmorSystem
    {
        private const float Epsilon = 0.0001f;

        private float points;
        private int resetTicks;
        private bool restartPending;

        private float capacityFactor = 1f;
        private float protectionFactor = 1f;
        private float rechargeFactor = 1f;

        private float lastRepairEnergy;
        private int lastRepairTick = -9999;

        private Vector3 impactDirection;
        private int lastImpactTick = -9999;

        private static readonly Material ShieldMaterial =
            MaterialPool.MatFrom(
                "Other/ShieldBubble",
                ShaderDatabase.Transparent);

        private ThingDef ShieldDef =>
            Host.InstalledModules.FirstOrDefault(
                d => ModuleUtility.Info(d)?.shield == true);

        private ModuleExtension Shield =>
            ModuleUtility.Info(ShieldDef);

        public bool HasShield => Shield != null;
        public float Points => points;
        public float CapacityFactor => capacityFactor;
        public float ProtectionFactor => protectionFactor;
        public float RechargeFactor => rechargeFactor;

        public float DamageFactor =>
            1f / Mathf.Max(0.01f, protectionFactor);

        private bool Enabled =>
            HasShield && Host.IsEnabled(ShieldDef);

        private bool Powered =>
            Enabled && Host.IsModuleEffective(ShieldDef);

        private bool Ready =>
            Powered && resetTicks <= 0 && !restartPending;

        public bool Active => Ready && points > 0f;

        public float Maximum =>
            HasShield
                ? Multiply(Shield.shieldMax, capacityFactor)
                : 0f;

        public float RegenerationPer60 =>
            HasShield
                ? Multiply(Shield.shieldRecharge, rechargeFactor)
                : 0f;

        public float LossPerDamage =>
            HasShield
                ? NonNegative(Shield.shieldLossPerDamage) /
                    Mathf.Max(0.01f, protectionFactor)
                : 0f;

        public float RestartPrice =>
            RestartPriceFor(capacityFactor, rechargeFactor);

        public float RestartPriceFor(float capacity, float recharge)
        {
            return Multiply(
                Shield?.shieldRestartCost ?? 0f,
                Multiply(
                    BuildRange("Capacity").Multiplier(capacity),
                    SharedRechargeCostMultiplier(recharge)));
        }

        private float SharedRechargeCostMultiplier(float recharge)
        {
            // Split the recharge cost deviation equally between both prices.
            return 1f + (BuildRange("Recharge").Multiplier(recharge) - 1f) * 0.5f;
        }

        public float RepairPricePerPoint =>
            Multiply(
                Shield?.consumption ?? 0f,
                CostMultiplierFor(
                    capacityFactor,
                    protectionFactor,
                    rechargeFactor));

        // This is actual repair spending, normalized to 60 ticks.
        public float CurrentConsumptionPer60 =>
            Enabled &&
            Find.TickManager != null &&
            lastRepairTick == Find.TickManager.TicksGame
                ? Multiply(lastRepairEnergy, 60f)
                : 0f;

        private bool FullyRepaired =>
            HasShield && points >= Maximum;

        public bool CanConfigure =>
            HasShield &&
            resetTicks <= 0 &&
            !restartPending &&
            FullyRepaired;

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : value;
        }

        private static float NonNegative(float value)
        {
            return Mathf.Max(0f, Finite(value, 0f));
        }

        private static float Multiply(float a, float b)
        {
            return (float)Math.Min(
                float.MaxValue,
                (double)NonNegative(a) * NonNegative(b));
        }

        private static float ResolveSlope(float directional, float legacy)
        {
            return NonNegative(Finite(directional, -1f) >= 0f
                ? directional : legacy);
        }

        private sealed class RangeModel
        {
            public float BaseMin;
            public float BaseMax;
            public float BaseCoefficientBelow;
            public float BaseCoefficientAbove;
            public float Min;
            public float Max;
            public List<ShieldSettingRange> Ranges;

            public float Multiplier(float value)
            {
                value = Mathf.Clamp(Finite(value, 1f), Min, Max);

                if (value == 1f)
                    return 1f;

                float low = Mathf.Min(1f, value);
                float high = Mathf.Max(1f, value);

                var cuts = new List<float>
                {
                    low, high, 1f, BaseMin, BaseMax
                };

                foreach (ShieldSettingRange range in Ranges)
                {
                    cuts.Add(range.min);
                    cuts.Add(range.max);
                }

                cuts = cuts
                    .Where(x => x >= low && x <= high)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                double integral = 0d;

                for (int i = 0; i < cuts.Count - 1; i++)
                {
                    float a = cuts[i];
                    float b = cuts[i + 1];
                    float midpoint = a + (b - a) * 0.5f;

                    bool below = midpoint < 1f;
                    float baseSlope = below
                        ? BaseCoefficientBelow : BaseCoefficientAbove;

                    float slope =
                        midpoint >= BaseMin && midpoint <= BaseMax
                            ? baseSlope
                            : Ranges
                                .Where(r =>
                                    midpoint >= r.min &&
                                    midpoint <= r.max)
                                .Select(r => ResolveSlope(
                                    below ? r.coefficientBelow : r.coefficientAbove,
                                    r.coefficient))
                                .DefaultIfEmpty(baseSlope)
                                .Min();

                    integral += (double)(b - a) * slope;
                }

                double result =
                    1d + (value < 1f ? -integral : integral);

                return (float)Math.Min(
                    float.MaxValue, Math.Max(0d, result));
            }
        }

        private RangeModel BuildRange(string setting)
        {
            ModuleExtension generator = Shield;

            float minimum = 0.5f;
            float maximum = 1.5f;
            float coefficient = 1f;
            float coefficientBelow = -1f;
            float coefficientAbove = -1f;

            if (generator != null)
            {
                switch (setting)
                {
                    case "Capacity":
                        minimum = generator.shieldCapacityMin;
                        maximum = generator.shieldCapacityMax;
                        coefficient =
                            generator.shieldCapacityCostCoefficient;
                        coefficientBelow = generator.shieldCapacityCostCoefficientBelow;
                        coefficientAbove = generator.shieldCapacityCostCoefficientAbove;
                        break;

                    case "Protection":
                        minimum = generator.shieldProtectionMin;
                        maximum = generator.shieldProtectionMax;
                        coefficient =
                            generator.shieldDamageCostCoefficient;
                        coefficientBelow = generator.shieldProtectionCostCoefficientBelow;
                        coefficientAbove = generator.shieldProtectionCostCoefficientAbove;
                        break;

                    case "Recharge":
                        minimum = generator.shieldRechargeMin;
                        maximum = generator.shieldRechargeMax;
                        coefficient =
                            generator.shieldRechargeCostCoefficient;
                        coefficientBelow = generator.shieldRechargeCostCoefficientBelow;
                        coefficientAbove = generator.shieldRechargeCostCoefficientAbove;
                        break;
                }
            }

            minimum = Mathf.Clamp(
                Finite(minimum, 0.5f), 0.01f, 1f);
            maximum = Mathf.Max(1f, Finite(maximum, 1.5f));

            var ranges = new List<ShieldSettingRange>();

            foreach (ThingDef module in Host.InstalledModules)
            {
                ModuleExtension extension = ModuleUtility.Info(module);

                if (extension == null ||
                    extension.shield ||
                    extension.shieldSettingRanges == null)
                    continue;

                if (extension.active &&
                    !Host.IsModuleEffective(module))
                    continue;

                foreach (ShieldSettingRange range
                    in extension.shieldSettingRanges)
                {
                    if (range == null ||
                        !string.Equals(
                            range.setting,
                            setting,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    float a = Mathf.Max(
                        0.01f, Finite(range.min, 1f));
                    float b = Mathf.Max(
                        0.01f, Finite(range.max, 1f));

                    if (b <= a)
                        continue;

                    ranges.Add(new ShieldSettingRange
                    {
                        setting = setting,
                        min = a,
                        max = b,
                        coefficient = NonNegative(range.coefficient),
                        coefficientBelow = ResolveSlope(
                            range.coefficientBelow, range.coefficient),
                        coefficientAbove = ResolveSlope(
                            range.coefficientAbove, range.coefficient)
                    });
                }
            }

            float availableMin = minimum;
            float availableMax = maximum;
            bool changed;

            do
            {
                changed = false;

                foreach (ShieldSettingRange range in ranges)
                {
                    if (range.min > availableMax + Epsilon ||
                        range.max < availableMin - Epsilon)
                        continue;

                    float nextMin = Mathf.Min(availableMin, range.min);
                    float nextMax = Mathf.Max(availableMax, range.max);

                    if (nextMin != availableMin ||
                        nextMax != availableMax)
                    {
                        availableMin = nextMin;
                        availableMax = nextMax;
                        changed = true;
                    }
                }
            }
            while (changed);

            return new RangeModel
            {
                BaseMin = minimum,
                BaseMax = maximum,
                BaseCoefficientBelow = ResolveSlope(coefficientBelow, coefficient),
                BaseCoefficientAbove = ResolveSlope(coefficientAbove, coefficient),
                Min = availableMin,
                Max = availableMax,
                Ranges = ranges
            };
        }

        public float CostMultiplierFor(
            float capacity,
            float protection,
            float recharge)
        {
            // Capacity affects restart only; protection affects repair only.
            // Keep the capacity argument for compatibility with existing callers.
            return Multiply(
                BuildRange("Protection").Multiplier(protection),
                SharedRechargeCostMultiplier(recharge));
        }

        public override void ModifyRunningCost(
            ThingDef module,
            ref float cost)
        {
            // Shield repairs are paid directly from the energy budget.
            // Never charge an additional continuous running cost.
            if (module == ShieldDef)
                cost = 0f;
        }

        private void SanitizeState()
        {
            RangeModel capacity = BuildRange("Capacity");
            RangeModel protection = BuildRange("Protection");
            RangeModel recharge = BuildRange("Recharge");

            capacityFactor = Mathf.Clamp(
                Finite(capacityFactor, 1f),
                capacity.Min, capacity.Max);

            protectionFactor = Mathf.Clamp(
                Finite(protectionFactor, 1f),
                protection.Min, protection.Max);

            rechargeFactor = Mathf.Clamp(
                Finite(rechargeFactor, 1f),
                recharge.Min, recharge.Max);

            points = Mathf.Clamp(Finite(points, 0f), 0f, Maximum);
            resetTicks = Math.Max(0, resetTicks);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref points, "maShieldPoints", 0f);
            Scribe_Values.Look(ref resetTicks, "maShieldReset", 0);
            Scribe_Values.Look(
                ref restartPending, "maShieldRestartPending", false);
            Scribe_Values.Look(
                ref capacityFactor, "maShieldCapacitySetting", 1f);
            Scribe_Values.Look(
                ref protectionFactor, "maShieldProtectionSetting", 1f);
            Scribe_Values.Look(
                ref rechargeFactor, "maShieldRechargeSetting", 1f);
        }

        public override void AfterLoad()
        {
            SanitizeState();
            lastRepairEnergy = 0f;
            lastRepairTick = -9999;
        }

        public override void ModulesChanged()
        {
            SanitizeState();
        }

        public override void Tick()
        {
            if (resetTicks > 0 && --resetTicks == 0)
                restartPending = true;
        }

        public override void ModuleToggled(
            ThingDef module, bool enabled)
        {
            if (module == ShieldDef)
            {
                lastRepairEnergy = 0f;
                lastRepairTick = -9999;
            }
        }

        public override string InstallBlockedReason(ThingDef module)
        {
            return ModuleUtility.Info(module)?.shield == true &&
                HasShield
                ? ModuleUtility.T(
                    "MA_ShieldAlreadyInstalled",
                    "Only one shield generator may be installed.")
                : null;
        }

        public override void BeforeRunningEnergy(
            ArmorEnergyBudget budget)
        {
            lastRepairEnergy = 0f;
            lastRepairTick = Find.TickManager.TicksGame;
            SanitizeState();

            if (!Enabled ||
                resetTicks > 0 ||
                !restartPending ||
                !budget.TrySpend(RestartPrice))
                return;

            restartPending = false;
            points = 0f;

            Pawn pawn = Host.Wearer;

            if (pawn == null || !pawn.Spawned)
                return;

            SoundDefOf.EnergyShield_Reset.PlayOneShot(
                new TargetInfo(pawn.Position, pawn.Map));

            FleckMaker.ThrowLightningGlow(
                pawn.TrueCenter(), pawn.Map, 3f);
        }

        public override void AfterRunningEnergy(
            ArmorEnergyBudget budget)
        {
            if (!Ready || FullyRepaired)
                return;

            float missing = Mathf.Max(0f, Maximum - points);
            float wanted = Mathf.Min(
                missing, RegenerationPer60 / 60f);

            if (wanted <= 0f)
                return;

            float price = RepairPricePerPoint;
            float restored = wanted;

            if (price > 0f)
            {
                // Repair only the amount affordable this tick.
                restored = Mathf.Min(
                    wanted, NonNegative(budget.Available) / price);
            }

            if (restored <= 0f)
                return;

            float target = Mathf.Min(Maximum, points + restored);
            float actual = target - points;

            // Do not charge for increments lost to float precision.
            if (actual <= 0f)
                return;

            float payment = Multiply(actual, price);

            if (!budget.TrySpend(payment))
                return;

            points = target;
            lastRepairEnergy = payment;
            lastRepairTick = Find.TickManager.TicksGame;
        }

        private void BreakShield()
        {
            points = 0f;
            resetTicks = Math.Max(
                0, Shield?.shieldResetTicks ?? 3200);
            restartPending = resetTicks == 0;
            lastRepairEnergy = 0f;
            lastRepairTick = -9999;

            Pawn pawn = Host.Wearer;

            if (pawn == null || !pawn.Spawned)
                return;

            EffecterDefOf.Shield_Break.SpawnAttached(
                pawn, pawn.Map, 1.2f);

            FleckMaker.Static(
                pawn.TrueCenter(), pawn.Map,
                FleckDefOf.ExplosionFlash, 12f);

            for (int i = 0; i < 6; i++)
                FleckMaker.ThrowDustPuff(
                    pawn.TrueCenter(), pawn.Map,
                    Rand.Range(0.8f, 1.2f));
        }

        private void Absorb(DamageInfo damage)
        {
            Pawn pawn = Host.Wearer;

            if (pawn == null || !pawn.Spawned)
                return;

            SoundDefOf.EnergyShield_AbsorbDamage.PlayOneShot(
                new TargetInfo(pawn.Position, pawn.Map));

            impactDirection =
                Vector3Utility.HorizontalVectorFromAngle(damage.Angle);

            Vector3 location = pawn.TrueCenter() +
                impactDirection.RotatedBy(180f) * 0.5f;

            float size = Mathf.Min(10f, 2f + damage.Amount / 10f);

            FleckMaker.Static(
                location, pawn.Map,
                FleckDefOf.ExplosionFlash, size);

            for (int i = 0; i < (int)size; i++)
                FleckMaker.ThrowDustPuff(
                    location, pawn.Map, Rand.Range(0.8f, 1.2f));

            lastImpactTick = Find.TickManager.TicksGame;
        }

        public override void BeforeDamage(
            ref DamageInfo damage,
            ref bool absorbed)
        {
            if (absorbed || !Ready || Host.Wearer == null)
                return;

            if (damage.Def == DamageDefOf.EMP)
            {
                BreakShield();
                return;
            }

            if (points <= 0f ||
                damage.Def.ignoreShields ||
                (!damage.Def.isRanged && !damage.Def.isExplosive))
                return;

            absorbed = true;

            points = Mathf.Max(
                0f,
                points - Multiply(damage.Amount, LossPerDamage));

            if (points <= 0f)
                BreakShield();
            else
                Absorb(damage);
        }

        public override bool AllowVerb(Verb verb)
        {
            return !(Ready &&
                Shield.blocksShooting &&
                verb is Verb_LaunchProjectile);
        }

        public override void DrawWorn()
        {
            Pawn pawn = Host.Wearer;

            if (!Active || pawn == null ||
                !pawn.Spawned || pawn.Dead || pawn.Downed)
                return;

            Vector3 position = pawn.Drawer.DrawPos;
            position.y = AltitudeLayer.MoteOverhead.AltitudeFor();

            float size = Mathf.Lerp(
                1.2f, 1.55f,
                Mathf.Clamp01(points / Mathf.Max(1f, Maximum)));

            int elapsed =
                Find.TickManager.TicksGame - lastImpactTick;

            if (elapsed >= 0 && elapsed < 8)
            {
                float jitter = (8 - elapsed) / 8f * 0.05f;
                position += impactDirection * jitter;
                size -= jitter;
            }

            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(
                    position, Quaternion.identity,
                    new Vector3(size, 1f, size)),
                ShieldMaterial, 0);
        }

        private string ResetCountdown =>
            ModuleUtility.T(
                "MA_ShieldResetCountdown",
                "Reset in: ") +
            (resetTicks / 60f).ToString("0.0") +
            ModuleUtility.T("MA_SecondsShort", " s");

        public string Status
        {
            get
            {
                if (!HasShield)
                    return "";

                string mode;

                if (resetTicks > 0)
                    mode = ResetCountdown;
                else if (!Enabled)
                    mode = ModuleUtility.T("MA_ShieldModeOff", "Off");
                else if (restartPending)
                    mode = ModuleUtility.T(
                        "MA_ShieldAwaitingEnergy",
                        "Waiting for restart energy: ") +
                        RestartPrice.ToString("0.##");
                else if (!Powered)
                    mode = ModuleUtility.T(
                        "MA_ShieldNoPower",
                        "Insufficient operating energy");
                else if (FullyRepaired)
                    mode = ModuleUtility.T(
                        "MA_ShieldModeIdle", "Idle");
                else
                    mode = ModuleUtility.T(
                        "MA_ShieldModeRegeneration", "Regeneration");

                return ModuleUtility.T("MA_Shield", "Shield: ") +
                    points.ToString("0.0") + "/" +
                    Maximum.ToString("0.0") + " | " + mode + "\n" +
                    ModuleUtility.T(
                        "MA_ShieldRepairEnergyActual",
                        "Repair drain: ") +
                    CurrentConsumptionPer60.ToString("0.##") +
                    ModuleUtility.T("MA_Per60", " / 60 ticks") +
                    " | " +
                    ModuleUtility.T(
                        "MA_ShieldRepairPricePerPoint",
                        "Energy / point: ") +
                    RepairPricePerPoint.ToString("0.##");
            }
        }

        public override ArmorStatusBar SecondaryEnergyBar =>
            !HasShield
                ? null
                : new ArmorStatusBar
                {
                    Label = resetTicks > 0
                        ? ResetCountdown
                        : restartPending
                            ? ModuleUtility.T(
                                "MA_ShieldRestartPendingLabel",
                                "Shield: waiting for restart")
                            : ModuleUtility.T(
                                "MA_ShieldBar", "Shield"),
                    Value = points,
                    Maximum = Maximum,
                    Color = new Color(0.30f, 0.68f, 0.95f)
                };

        public override void DrawSettingsHeader(
            Rect area, ref float y)
        {
            if (!HasShield)
                return;

            Widgets.Label(
                new Rect(0f, y, area.width, 70f), Status);
            y += 75f;
        }

        public override void DrawModuleActions(
            ThingDef module, Rect area)
        {
            if (module != ShieldDef)
                return;

            if (Widgets.ButtonText(
                new Rect(area.x, area.y, area.width, 30f),
                ModuleUtility.T(
                    "MA_ShieldSettings", "Shield settings")))
            {
                Find.WindowStack.Add(
                    new Dialog_ShieldConfiguration(this));
            }
        }

        public override IEnumerable<string> DescribeModule(
            ThingDef module)
        {
            ModuleExtension x = ModuleUtility.Info(module);

            if (x == null)
                yield break;

            if (x.shield)
            {
                yield return ModuleUtility.T(
                    "MA_ShieldCapacity", "Base shield capacity: ") +
                    x.shieldMax.ToString("0.##");

                yield return ModuleUtility.T(
                    "MA_ShieldBaseLossPerDamage",
                    "Base shield loss per damage: ") +
                    x.shieldLossPerDamage.ToString("0.###");

                yield return ModuleUtility.T(
                    "MA_ShieldBaseRegeneration",
                    "Base repair / 60 ticks: ") +
                    x.shieldRecharge.ToString("0.##");

                yield return ModuleUtility.T(
                    "MA_ShieldBaseRepairPricePerPoint",
                    "Base energy / repaired point: ") +
                    x.consumption.ToString("0.##");

                yield return ModuleUtility.T(
                    "MA_ShieldNoContinuousDrain",
                    "No repair spending while full or resetting.");
            }

            if (x.shieldSettingRanges == null)
                yield break;

            foreach (ShieldSettingRange range in x.shieldSettingRanges)
            {
                if (range == null)
                    continue;

                yield return range.setting + ": " +
                    range.min.ToString("0.##") + "–" +
                    range.max.ToString("0.##") + "; " +
                    ModuleUtility.T(
                        "MA_ShieldCostSlopeBelow", "cost slope below 1: ") +
                    ResolveSlope(range.coefficientBelow, range.coefficient)
                        .ToString("0.##") + "; " +
                    ModuleUtility.T(
                        "MA_ShieldCostSlopeAbove", "cost slope above 1: ") +
                    ResolveSlope(range.coefficientAbove, range.coefficient)
                        .ToString("0.##");
            }
        }

        private sealed class Dialog_ShieldConfiguration : Window
        {
            private readonly ArmorShieldSystem system;
            private float capacity;
            private float protection;
            private float recharge;

            public override Vector2 InitialSize =>
                new Vector2(620f, 700f);

            public Dialog_ShieldConfiguration(ArmorShieldSystem system)
            {
                this.system = system;
                capacity = system.capacityFactor;
                protection = system.protectionFactor;
                recharge = system.rechargeFactor;

                doCloseX = true;
                doCloseButton = true;
                forcePause = false;
                absorbInputAroundWindow = false;
                closeOnClickedOutside = false;
            }

            private static void Slider(
                float width, ref float y,
                string label, ref float value,
                RangeModel range)
            {
                value = Mathf.Clamp(value, range.Min, range.Max);

                Widgets.Label(
                    new Rect(0f, y, width, 26f),
                    label + ": " + value.ToString("0.00") +
                    " [" + range.Min.ToString("0.00") + "–" +
                    range.Max.ToString("0.00") + "]");

                y += 28f;

                value = Widgets.HorizontalSlider(
                    new Rect(0f, y, width, 24f),
                    value, range.Min, range.Max);

                value = Mathf.Clamp(
                    Mathf.Round(value * 100f) / 100f,
                    range.Min, range.Max);

                y += 34f;
            }

            public override void DoWindowContents(Rect rect)
            {
                if (system.Host.parent.Destroyed || !system.HasShield)
                {
                    Close();
                    return;
                }

                Text.Font = GameFont.Medium;
                Widgets.Label(
                    new Rect(0f, 0f, rect.width - 30f, 35f),
                    ModuleUtility.T(
                        "MA_ShieldSettings", "Shield settings"));
                Text.Font = GameFont.Small;

                float y = 45f;
                Widgets.Label(
                    new Rect(0f, y, rect.width, 70f), system.Status);
                y += 75f;

                if (Widgets.ButtonText(
                    new Rect(0f, y, rect.width, 30f),
                    system.Enabled
                        ? ModuleUtility.T("MA_SwitchOff", "Switch off")
                        : ModuleUtility.T("MA_SwitchOn", "Switch on")))
                    system.Host.Toggle(system.ShieldDef);

                y += 40f;

                bool editable = system.CanConfigure;

                if (!editable)
                {
                    capacity = system.capacityFactor;
                    protection = system.protectionFactor;
                    recharge = system.rechargeFactor;
                }

                bool previous = GUI.enabled;
                GUI.enabled = previous && editable;

                Slider(rect.width, ref y,
                    ModuleUtility.T(
                        "MA_ShieldCapacitySetting", "Capacity factor"),
                    ref capacity, system.BuildRange("Capacity"));

                Slider(rect.width, ref y,
                    ModuleUtility.T(
                        "MA_ShieldProtectionSetting", "Protection factor"),
                    ref protection, system.BuildRange("Protection"));

                Slider(rect.width, ref y,
                    ModuleUtility.T(
                        "MA_ShieldRechargeSetting", "Repair speed factor"),
                    ref recharge, system.BuildRange("Recharge"));

                GUI.enabled = previous;

                float multiplier = system.CostMultiplierFor(
                    capacity, protection, recharge);

                float price = Multiply(
                    system.Shield.consumption, multiplier);

                float rate = Multiply(
                    system.Shield.shieldRecharge, recharge);

                float loss =
                    NonNegative(system.Shield.shieldLossPerDamage) /
                    Mathf.Max(0.01f, protection);

                Widgets.Label(
                    new Rect(0f, y, rect.width, 150f),
                    ModuleUtility.T(
                        "MA_ShieldPreviewMaximum", "Configured maximum: ") +
                    Multiply(system.Shield.shieldMax, capacity)
                        .ToString("0.##") + "\n" +
                    ModuleUtility.T(
                        "MA_ShieldPreviewLossPerDamage",
                        "Shield loss per damage: ") +
                    loss.ToString("0.###") + "\n" +
                    ModuleUtility.T(
                        "MA_ShieldPreviewRate", "Repair / 60 ticks: ") +
                    rate.ToString("0.##") + "\n" +
                    ModuleUtility.T(
                        "MA_ShieldRepairPricePerPoint", "Energy / point: ") +
                    price.ToString("0.##") + "\n" +
                    ModuleUtility.T(
                        "MA_ShieldMaximumRepairDrain",
                        "Full-speed repair energy / 60 ticks: ") +
                    Multiply(rate, price).ToString("0.##") + "\n" +
                    ModuleUtility.T(
                        "MA_ShieldRestartCost", "Restart energy: ") +
                    system.RestartPriceFor(capacity, recharge).ToString("0.##"));

                y += 155f;

                if (!editable)
                    Widgets.Label(
                        new Rect(0f, y, rect.width, 40f),
                        ModuleUtility.T(
                            "MA_ShieldSettingsRepairLockedShort",
                            "Locked: fully repair the shield first."));

                y += 42f;
                GUI.enabled = previous && editable;

                if (Widgets.ButtonText(
                    new Rect(0f, y, rect.width, 30f),
                    ModuleUtility.T(
                        "MA_ShieldApplySettings", "Apply settings")))
                {
                    system.capacityFactor = capacity;
                    system.protectionFactor = protection;
                    system.rechargeFactor = recharge;
                    system.SanitizeState();
                    system.Host.NotifyStatsChanged();
                }

                GUI.enabled = previous;
            }
        }
    }
}