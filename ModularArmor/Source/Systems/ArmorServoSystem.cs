using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class ArmorServoSystem : ArmorSystem
    {
        private int overloadTicks;
        private int cooldownTicks;
        private ThingDef overloadDef;

        private bool criticalMode;
        private int criticalWearTicks;

        public bool OverloadActive => overloadTicks > 0;

        public bool CriticalOverloadActive =>
            OverloadActive && criticalMode;

        public string Status =>
            overloadTicks > 0
                ? ModuleUtility.T(
                    criticalMode
                        ? "MA_CriticalOverloadRemaining"
                        : "MA_OverloadRemaining",
                    criticalMode
                        ? "Critical overload: "
                        : "Overload: ") +
                  ModuleUtility.TimeText(overloadTicks)
                : cooldownTicks > 0
                    ? ModuleUtility.T(
                        "MA_Cooldown",
                        "Cooldown: ") +
                      ModuleUtility.TimeText(cooldownTicks)
                    : ModuleUtility.T(
                        "MA_OverloadReady",
                        "Overload ready");

        public override void ExposeData()
        {
            // Preserve the original ordinary overload save keys.
            Scribe_Values.Look(
                ref overloadTicks,
                "maOverloadTicks",
                0);

            Scribe_Values.Look(
                ref cooldownTicks,
                "maCooldownTicks",
                0);

            Scribe_Defs.Look(
                ref overloadDef,
                "maOverloadDef");

            Scribe_Values.Look(
                ref criticalMode,
                "maServoCriticalMode",
                false);

            Scribe_Values.Look(
                ref criticalWearTicks,
                "maServoCriticalWearTicks",
                0);
        }

        public override void AfterLoad()
        {
            overloadTicks = Math.Max(0, overloadTicks);
            cooldownTicks = Math.Max(0, cooldownTicks);
            criticalWearTicks = Math.Max(0, criticalWearTicks);

            if (overloadTicks <= 0)
            {
                overloadDef = null;
                criticalMode = false;
                criticalWearTicks = 0;
                return;
            }

            ModuleExtension info = ModuleUtility.Info(overloadDef);

            bool valid = overloadDef != null &&
                info != null &&
                Host.InstalledModules.Contains(overloadDef);

            if (valid)
            {
                valid = criticalMode
                    ? info.criticalOverload
                    : info.overload;
            }

            if (!valid)
            {
                Stop();
                return;
            }

            if (criticalMode)
            {
                int interval = Math.Max(
                    1,
                    info.criticalArmorDamageInterval);

                criticalWearTicks = Math.Min(
                    criticalWearTicks,
                    interval - 1);
            }
            else
            {
                criticalWearTicks = 0;
            }
        }

        private void Stop()
        {
            if (overloadTicks <= 0)
                return;

            ModuleExtension info = ModuleUtility.Info(overloadDef);

            int cooldown = criticalMode
                ? info?.criticalOverloadCooldown ?? 7500
                : info?.overloadCooldown ?? 7500;

            cooldownTicks = Math.Max(
                cooldownTicks,
                Math.Max(0, cooldown));

            overloadTicks = 0;
            overloadDef = null;
            criticalMode = false;
            criticalWearTicks = 0;
        }

        private bool ArmorTooDamaged(ModuleExtension info)
        {
            if (info == null ||
                info.criticalArmorDamage <= 0)
                return false;

            return Host.parent.HitPoints <= Math.Max(
                1,
                info.criticalMinimumHitPoints);
        }

        public override void Tick()
        {
            if (cooldownTicks > 0)
                cooldownTicks--;

            if (overloadTicks <= 0)
                return;

            ModuleExtension info = ModuleUtility.Info(overloadDef);

            if (Host.Wearer == null ||
                overloadDef == null ||
                info == null ||
                !Host.IsEnabled(overloadDef) ||
                !Host.InstalledModules.Contains(overloadDef))
            {
                Stop();
                return;
            }

            if (criticalMode &&
                (!info.criticalOverload ||
                 ArmorTooDamaged(info)))
            {
                Stop();
                return;
            }

            if (!criticalMode && !info.overload)
            {
                Stop();
                return;
            }

            if (overloadTicks == 1)
                Stop();
            else
                overloadTicks--;
        }

        public override void PowerLost() => Stop();

        public override void EnergyEmpty() => Stop();

        public override void ModuleRemoved(ThingDef module)
        {
            if (overloadDef == module)
                Stop();
        }

        public override void ModuleToggled(
            ThingDef module,
            bool enabled)
        {
            if (!enabled && overloadDef == module)
                Stop();
        }

        public override void ModifyRunningCost(
            ThingDef module,
            ref float cost)
        {
            if (overloadTicks <= 0 || overloadDef != module)
                return;

            ModuleExtension info = ModuleUtility.Info(module);

            if (info == null)
                return;

            cost += Mathf.Max(
                0f,
                criticalMode
                    ? info.criticalOverloadExtraConsumption
                    : info.overloadExtraConsumption);
        }

        public override void AfterRunningEnergy(
            ArmorEnergyBudget budget)
        {
            if (!CriticalOverloadActive ||
                overloadDef == null ||
                !Host.IsModuleEffective(overloadDef))
                return;

            ModuleExtension info = ModuleUtility.Info(overloadDef);

            if (info == null ||
                !info.criticalOverload ||
                info.criticalArmorDamage <= 0)
                return;

            if (Host.parent.Destroyed ||
                !Host.parent.def.useHitPoints)
            {
                Stop();
                return;
            }

            int minimum = Math.Max(
                1,
                info.criticalMinimumHitPoints);

            if (Host.parent.HitPoints <= minimum)
            {
                Stop();
                return;
            }

            int interval = Math.Max(
                1,
                info.criticalArmorDamageInterval);

            criticalWearTicks++;

            if (criticalWearTicks < interval)
                return;

            criticalWearTicks = 0;

            int damage = Math.Max(
                0,
                info.criticalArmorDamage);

            int removableHitPoints =
                Host.parent.HitPoints - minimum;

            int appliedDamage = Math.Min(
                damage,
                removableHitPoints);

            // Apply direct wear instead of combat damage.
            // Armor ratings and energy shields do not reduce this wear.
            Host.parent.HitPoints -= appliedDamage;

            if (Host.parent.HitPoints <= minimum)
            {
                Stop();

                Messages.Message(
                    ModuleUtility.T(
                        "MA_CriticalOverloadSafetyStop",
                        "Critical overload stopped: armor durability is too low."),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
        }

        public override float StatOffset(StatDef stat)
        {
            if (stat != StatDefOf.MoveSpeed)
                return 0f;

            float result = 0f;

            foreach (ThingDef module in Host.InstalledModules)
            {
                ModuleExtension info = ModuleUtility.Info(module);

                if (info == null ||
                    !Host.IsModuleEffective(module))
                    continue;

                float bonus = info.moveSpeed;

                if (overloadTicks > 0 && overloadDef == module)
                {
                    bonus = criticalMode
                        ? info.criticalOverloadMoveSpeed
                        : info.overloadMoveSpeed;
                }

                result += bonus;
            }

            return result;
        }

        // Preserve the original ordinary overload API.
        public string DisabledReason(ThingDef module)
        {
            return ModeDisabledReason(module, false);
        }

        public string CriticalDisabledReason(ThingDef module)
        {
            return ModeDisabledReason(module, true);
        }

        private string ModeDisabledReason(
            ThingDef module,
            bool critical)
        {
            ModuleExtension info = ModuleUtility.Info(module);

            bool available = info != null &&
                Host.InstalledModules.Contains(module);

            if (available)
            {
                available = critical
                    ? info.criticalOverload
                    : info.overload;
            }

            if (!available)
            {
                return critical
                    ? ModuleUtility.T(
                        "MA_NoCriticalOverloadModule",
                        "No critical overload module installed")
                    : ModuleUtility.T(
                        "MA_NoOverloadModule",
                        "No overload module installed");
            }

            if (Host.Wearer == null ||
                Host.Wearer.Dead ||
                Host.Wearer.Downed)
            {
                return ModuleUtility.T(
                    "MA_WearerUnavailable",
                    "Wearer is unavailable");
            }

            // Both modes share one running state and cooldown.
            if (overloadTicks > 0 || cooldownTicks > 0)
                return Status;

            if (!Host.IsEnabled(module))
            {
                return ModuleUtility.T(
                    "MA_EnableServosFirst",
                    "Enable servo drives first");
            }

            if (!Host.IsModuleEffective(module) ||
                (Host.EnergyService?.Energy ?? 0f) <= 0f)
            {
                return ModuleUtility.T(
                    "MA_NoPower",
                    "No power");
            }

            if (critical && info.criticalArmorDamage > 0)
            {
                if (!Host.parent.def.useHitPoints)
                {
                    return ModuleUtility.T(
                        "MA_CriticalArmorNoDurability",
                        "This armor does not support durability wear.");
                }

                if (ArmorTooDamaged(info))
                {
                    return ModuleUtility.T(
                        "MA_CriticalArmorTooDamaged",
                        "Armor durability is too low for critical overload.");
                }
            }

            return null;
        }

        public void Start(ThingDef module)
        {
            StartMode(module, false);
        }

        public void StartCritical(ThingDef module)
        {
            StartMode(module, true);
        }

        private void StartMode(
            ThingDef module,
            bool critical)
        {
            string reason = ModeDisabledReason(module, critical);

            if (reason != null)
            {
                ModuleUtility.Reject(reason);
                return;
            }

            ModuleExtension info = ModuleUtility.Info(module);

            overloadDef = module;
            criticalMode = critical;
            criticalWearTicks = 0;

            overloadTicks = Math.Max(
                1,
                critical
                    ? info.criticalOverloadDuration
                    : info.overloadDuration);

            Host.NotifyStatsChanged();
        }

        private Command_Action CreateCommand(
            ThingDef module,
            bool critical)
        {
            var command = new Command_Action
            {
                defaultLabel = critical
                    ? ModuleUtility.T(
                        "MA_CriticalOverload",
                        "Critical overload")
                    : ModuleUtility.T(
                        "MA_Overload",
                        "Overload"),

                defaultDesc =
                    Host.ModuleDescription(module) +
                    "\n\n" + Status,

                icon = module.uiIcon,

                action = () => StartMode(module, critical)
            };

            string reason = ModeDisabledReason(module, critical);

            if (reason != null)
                command.Disable(reason);

            return command;
        }

        public override IEnumerable<Gizmo> GetWornGizmos()
        {
            foreach (ThingDef module in Host.InstalledModules)
            {
                ModuleExtension info = ModuleUtility.Info(module);

                if (info == null)
                    continue;

                if (info.overload)
                    yield return CreateCommand(module, false);

                if (info.criticalOverload)
                    yield return CreateCommand(module, true);
            }
        }

        public override void DrawModuleActions(
            ThingDef module,
            Rect area)
        {
            ModuleExtension info = ModuleUtility.Info(module);

            if (info == null ||
                (!info.overload && !info.criticalOverload))
                return;

            float y = area.y;

            if (info.overload)
            {
                Rect button = new Rect(
                    area.x,
                    y,
                    area.width,
                    30f);

                if (Widgets.ButtonText(
                    button,
                    ModuleUtility.T(
                        "MA_Overload",
                        "Overload")))
                    Start(module);

                TooltipHandler.TipRegion(
                    button,
                    DisabledReason(module) ?? Status);

                y += 38f;
            }

            if (info.criticalOverload)
            {
                Rect button = new Rect(
                    area.x,
                    y,
                    area.width,
                    30f);

                if (Widgets.ButtonText(
                    button,
                    ModuleUtility.T(
                        "MA_CriticalOverload",
                        "Critical overload")))
                    StartCritical(module);

                TooltipHandler.TipRegion(
                    button,
                    CriticalDisabledReason(module) ??
                    ModuleUtility.T(
                        "MA_CriticalOverloadWarning",
                        "Critical overload damages the armor while running."));

                y += 38f;
            }

            string reason = info.overload
                ? DisabledReason(module)
                : CriticalDisabledReason(module);

            float statusHeight = Mathf.Min(
                85f,
                Mathf.Max(0f, area.yMax - y));

            Widgets.Label(
                new Rect(
                    area.x,
                    y,
                    area.width,
                    statusHeight),
                Status + (reason != null ? "\n" + reason : ""));

            y += statusHeight + 6f;

            if (info.criticalOverload &&
                area.yMax > y)
            {
                string text =
                    ModuleUtility.T(
                        "MA_ServoArmorDurability",
                        "Armor durability: ") +
                    Host.parent.HitPoints + "/" +
                    Host.parent.MaxHitPoints;

                if (info.criticalArmorDamage > 0)
                {
                    text += "\n" + ModuleUtility.T(
                        "MA_CriticalOverloadWarning",
                        "Critical overload damages the armor while running.");
                }

                Widgets.Label(
                    new Rect(
                        area.x,
                        y,
                        area.width,
                        area.yMax - y),
                    text);
            }
        }

        public override IEnumerable<string> DescribeModule(
            ThingDef module)
        {
            ModuleExtension info = ModuleUtility.Info(module);

            if (info == null)
                yield break;

            if (info.moveSpeed != 0f)
            {
                yield return StatDefOf.MoveSpeed.LabelCap + ": " +
                    ModuleUtility.Bonus(
                        StatDefOf.MoveSpeed,
                        info.moveSpeed);
            }

            if (info.overload)
            {
                yield return ModuleUtility.T(
                    "MA_OverloadBonus",
                    "Overload — movement: ") +
                    ModuleUtility.Bonus(
                        StatDefOf.MoveSpeed,
                        info.overloadMoveSpeed);

                yield return ModuleUtility.T(
                    "MA_OverloadDrain",
                    "Additional consumption: ") +
                    info.overloadExtraConsumption.ToString("0.##");
            }

            if (info.criticalOverload)
            {
                yield return ModuleUtility.T(
                    "MA_CriticalOverloadBonus",
                    "Critical overload — movement: ") +
                    ModuleUtility.Bonus(
                        StatDefOf.MoveSpeed,
                        info.criticalOverloadMoveSpeed);

                yield return ModuleUtility.T(
                    "MA_CriticalOverloadDrain",
                    "Critical additional consumption: ") +
                    info.criticalOverloadExtraConsumption.ToString("0.##");

                yield return ModuleUtility.T(
                    "MA_CriticalOverloadDuration",
                    "Critical duration: ") +
                    ModuleUtility.TimeText(
                        info.criticalOverloadDuration);

                yield return ModuleUtility.T(
                    "MA_CriticalOverloadCooldown",
                    "Critical cooldown: ") +
                    ModuleUtility.TimeText(
                        info.criticalOverloadCooldown);

                if (info.criticalArmorDamage > 0)
                {
                    yield return ModuleUtility.T(
                        "MA_CriticalArmorWear",
                        "Armor wear: ") +
                        info.criticalArmorDamage +
                        ModuleUtility.T(
                            "MA_CriticalArmorWearEvery",
                            " HP every ") +
                        ModuleUtility.TimeText(
                            Math.Max(
                                1,
                                info.criticalArmorDamageInterval));

                    yield return ModuleUtility.T(
                        "MA_CriticalMinimumDurability",
                        "Critical mode stops at armor HP: ") +
                        Math.Max(
                            1,
                            info.criticalMinimumHitPoints);
                }
            }
        }

        public override void DrawSettingsHeader(
            Rect area,
            ref float y)
        {
            float moduleBonus =
                Host.StatOffset(StatDefOf.MoveSpeed);

            float baseOffset =
                Host.parent.def.equippedStatOffsets
                    .GetStatOffsetFromList(StatDefOf.MoveSpeed);

            Widgets.Label(
                new Rect(
                    0f,
                    y,
                    area.width,
                    34f),
                ModuleUtility.T(
                    "MA_CurrentServoBonus",
                    "Module movement bonus: ") +
                ModuleUtility.Bonus(
                    StatDefOf.MoveSpeed,
                    moduleBonus) +
                " | " +
                ModuleUtility.T(
                    "MA_ArmorMovementTotal",
                    "Total from armor: ") +
                ModuleUtility.Bonus(
                    StatDefOf.MoveSpeed,
                    baseOffset + moduleBonus));

            y += 40f;
        }
    }
}