using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public partial class CompModuleSlots : ThingComp
    {
        private List<ThingDef> modules = new List<ThingDef>();
        private List<ThingDef> enabled = new List<ThingDef>();

        private List<ArmorSystem> systems;
        private Pawn previousWearer;
        private float previousMovementBonus = float.NaN;

        private CompProperties_ModuleSlots Props =>
            (CompProperties_ModuleSlots)props;

        public Pawn Wearer => (parent as Apparel)?.Wearer;
        public bool IsCuirass => Props.allowArmorReinforcement;
        public string ArmorLabel => parent.LabelCap.ToString();

        public Map CurrentMap =>
            Wearer != null && Wearer.Spawned
                ? Wearer.Map
                : parent.Spawned ? parent.Map : null;

        public IntVec3 CurrentPosition =>
            Wearer != null && Wearer.Spawned
                ? Wearer.Position
                : parent.Position;

        public IReadOnlyList<ThingDef> InstalledModules =>
            modules.AsReadOnly();

        public IReadOnlyList<ArmorSystem> Systems
        {
            get
            {
                EnsureSystems();
                return systems.AsReadOnly();
            }
        }

        public T GetSystem<T>() where T : class
        {
            EnsureSystems();
            return systems.OfType<T>().FirstOrDefault();
        }

        public IArmorEnergy EnergyService =>
            GetSystem<IArmorEnergy>();

        private void EnsureSystems()
        {
            if (systems != null)
                return;

            systems = new List<ArmorSystem>();

            foreach (ArmorSystemDef def in
                DefDatabase<ArmorSystemDef>.AllDefsListForReading
                    .OrderBy(d => d.order)
                    .ThenBy(d => d.defName))
            {
                if (def.systemClass == null ||
                    def.systemClass.IsAbstract ||
                    !typeof(ArmorSystem).IsAssignableFrom(def.systemClass))
                    continue;

                ArmorSystem system =
                    (ArmorSystem)Activator.CreateInstance(def.systemClass);

                systems.Add(system);
                system.Bind(this, def);
            }
        }

        public int SlotCount
        {
            get
            {
                QualityCategory quality;

                if (!parent.TryGetQuality(out quality))
                    quality = QualityCategory.Normal;

                return Props.SlotsFor(quality);
            }
        }

        public int InstalledCount =>
            modules.Sum(d => Math.Max(
                1,
                ModuleUtility.Info(d)?.slots ?? 1));

        public int FreeSlots =>
            Math.Max(0, SlotCount - InstalledCount);

        public string SlotStatus
        {
            get
            {
                string text = string.Format(
                    ModuleUtility.T("MA_Slots", "Slots: {0}/{1}"),
                    InstalledCount,
                    SlotCount);

                if (InstalledCount > SlotCount)
                {
                    text += "\n" + ModuleUtility.T(
                        "MA_SlotOverflow",
                        "Quality slot limit exceeded. Remove excess modules.");
                }

                return text;
            }
        }

        public bool IsEnabled(ThingDef module) =>
            enabled.Contains(module);

        public bool IsModuleEffective(ThingDef module)
        {
            ModuleExtension info = ModuleUtility.Info(module);

            if (info == null || !modules.Contains(module))
                return false;

            return !info.active ||
                (Wearer != null &&
                 EnergyService != null &&
                 EnergyService.Capacity > 0f &&
                 EnergyService.RunningPowerAvailable &&
                 IsEnabled(module));
        }

        public List<CompModuleSlots> GetWornComponents()
        {
            var result = new List<CompModuleSlots>();

            if (Wearer?.apparel != null)
            {
                foreach (Apparel apparel in Wearer.apparel.WornApparel)
                {
                    CompModuleSlots comp =
                        apparel.GetComp<CompModuleSlots>();

                    if (comp != null)
                        result.Add(comp);
                }
            }

            return result;
        }

        public float StatOffset(StatDef stat)
        {
            EnsureSystems();
            return systems.Sum(s => s.StatOffset(stat));
        }

        public float RunningConsumption
        {
            get
            {
                EnsureSystems();
                float result = 0f;

                foreach (ThingDef module in modules)
                {
                    ModuleExtension info = ModuleUtility.Info(module);

                    if (info == null ||
                        !info.active ||
                        !IsEnabled(module))
                        continue;

                    float cost = Mathf.Max(0f, info.consumption);

                    foreach (ArmorSystem system in systems)
                        system.ModifyRunningCost(module, ref cost);

                    result += Mathf.Max(0f, cost);
                }

                return result;
            }
        }

        public string ModuleDescription(ThingDef module)
        {
            ModuleExtension info = ModuleUtility.Info(module);

            if (info == null)
                return "";

            var lines = new List<string>
            {
                ModuleUtility.T("MA_SlotCost", "Slots: ") +
                Math.Max(1, info.slots),

                ModuleUtility.T("MA_InstallationTime", "Installation: ") +
                ModuleUtility.TimeText(info.installTicks)
            };

            if (info.active)
            {
                lines.Add(ModuleUtility.T(
                    "MA_Consumption",
                    "Base consumption / 60 ticks: ") +
                    info.consumption.ToString("0.##"));
            }
            else
            {
                lines.Add(ModuleUtility.T(
                    "MA_Passive",
                    "Passive — no energy required"));
            }

            foreach (ArmorSystem system in Systems)
                lines.AddRange(system.DescribeModule(module));

            return string.Join("\n", lines);
        }

        public bool CanInstall(ThingDef module)
        {
            ModuleExtension info = ModuleUtility.Info(module);

            if (parent.Destroyed || info == null)
                return false;

            // Identical modules are limited per armor piece.
            // Missing, zero or negative limits are treated as one.
            int maxCopies = Math.Max(1, info.maxCopies);

            if (modules.Count(d => d == module) >= maxCopies)
                return false;

            if (!(IsCuirass ? info.forCuirass : info.forHelmet))
                return false;

            if (InstalledCount + Math.Max(1, info.slots) > SlotCount)
                return false;

            // Identical copies may share their exclusive group.
            // Different module definitions in that group still conflict.
            if (!string.IsNullOrEmpty(info.exclusiveGroup) &&
                modules.Any(d =>
                    d != module &&
                    ModuleUtility.Info(d)?.exclusiveGroup ==
                    info.exclusiveGroup))
                return false;

            return Systems.All(s =>
                s.InstallBlockedReason(module) == null);
        }

        public void Toggle(ThingDef module)
        {
            ModuleExtension info = ModuleUtility.Info(module);

            if (info == null ||
                !info.active ||
                !modules.Contains(module))
                return;

            bool value = !IsEnabled(module);

            if (value && (EnergyService?.Capacity ?? 0f) <= 0f)
            {
                ModuleUtility.Reject(ModuleUtility.T(
                    "MA_NoLocalCapacity",
                    "No local energy storage"));
                return;
            }

            if (value)
                enabled.Add(module);
            else
                enabled.Remove(module);

            foreach (ArmorSystem system in Systems)
                system.ModuleToggled(module, value);

            NotifyStatsChanged();
        }

        private void NotifyModulesChanged()
        {
            foreach (ArmorSystem system in Systems)
                system.ModulesChanged();

            NotifyStatsChanged();
        }

        public void NotifyStatsChanged()
        {
            Pawn pawn = Wearer;

            foreach (StatDef stat in
                DefDatabase<StatDef>.AllDefsListForReading)
            {
                stat.Worker.ClearCacheForThing(parent);

                if (pawn != null)
                    stat.Worker.ClearCacheForThing(pawn);

                if (previousWearer != null && previousWearer != pawn)
                    stat.Worker.ClearCacheForThing(previousWearer);
            }

            previousWearer = pawn;
            previousMovementBonus = StatOffset(StatDefOf.MoveSpeed);
        }

        public override void CompTick()
        {
            base.CompTick();
            CheckInstallation();

            foreach (ArmorSystem system in Systems)
                system.Tick();

            float movement = StatOffset(StatDefOf.MoveSpeed);

            if (Wearer != previousWearer ||
                float.IsNaN(previousMovementBonus) ||
                !Mathf.Approximately(movement, previousMovementBonus))
                NotifyStatsChanged();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            EnsureSystems();

            Scribe_Collections.Look(
                ref modules,
                "maInstalledModules",
                LookMode.Def);

            Scribe_Collections.Look(
                ref enabled,
                "maEnabledModules",
                LookMode.Def);

            ExposeInstallation();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                modules = modules ?? new List<ThingDef>();
                enabled = enabled ?? new List<ThingDef>();

                modules.RemoveAll(d => d == null);
                enabled.RemoveAll(d => d == null || !modules.Contains(d));
            }

            foreach (ArmorSystem system in systems)
            {
                if (system.Definition.legacySaveRoot)
                {
                    system.ExposeData();
                }
                else
                {
                    Scribe.EnterNode(
                        "maSystem_" + system.Definition.defName);

                    try
                    {
                        system.ExposeData();
                    }
                    finally
                    {
                        Scribe.ExitNode();
                    }
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                foreach (ArmorSystem system in systems)
                    system.AfterLoad();

                previousWearer = null;
                previousMovementBonus = float.NaN;
            }
        }

        public override void PostPreApplyDamage(
            ref DamageInfo damage,
            out bool absorbed)
        {
            absorbed = false;

            foreach (ArmorSystem system in Systems)
            {
                system.BeforeDamage(ref damage, ref absorbed);

                if (absorbed)
                    break;
            }
        }

        public override bool CompAllowVerbCast(Verb verb)
        {
            return Systems.All(s => s.AllowVerb(verb));
        }

        public override void CompDrawWornExtras()
        {
            base.CompDrawWornExtras();

            foreach (ArmorSystem system in Systems)
                system.DrawWorn();
        }

        public override string CompInspectStringExtra()
        {
            return SlotStatus +
                (InstallationPending ? "\n" + InstallationStatus : "");
        }

        private Command_Action SettingsCommand()
        {
            return new Command_Action
            {
                defaultLabel = ModuleUtility.T(
                    "MA_Settings",
                    "Armor settings"),

                defaultDesc = ArmorLabel + "\n\n" + SlotStatus,
                icon = parent.def.uiIcon,

                action = () => Find.WindowStack.Add(
                    new Dialog_ArmorSettings(this))
            };
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Spawned)
                yield return SettingsCommand();
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            if (Wearer == null ||
                Wearer.Faction != Faction.OfPlayer)
                yield break;

            yield return SettingsCommand();

            foreach (ArmorSystem system in Systems)
            {
                foreach (Gizmo gizmo in system.GetWornGizmos())
                    yield return gizmo;
            }
        }
    }
}