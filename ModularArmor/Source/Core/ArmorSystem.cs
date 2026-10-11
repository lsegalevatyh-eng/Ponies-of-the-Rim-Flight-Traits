using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class ArmorSystemDef : Def
    {
        public Type systemClass;
        public int order;

        // Used only to preserve the save layout of legacy built-in systems.
        public bool legacySaveRoot;

        public List<StatDef> affectedStats;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
                yield return error;

            if (systemClass == null ||
                !typeof(ArmorSystem).IsAssignableFrom(systemClass) ||
                systemClass.IsAbstract)
            {
                yield return "systemClass must be a concrete ArmorSystem.";
            }
            else if (systemClass.GetConstructor(Type.EmptyTypes) == null)
            {
                yield return
                    "systemClass needs a public parameterless constructor.";
            }
        }
    }

    public interface IArmorEnergy
    {
        float Energy { get; }
        float Capacity { get; }
        bool RunningPowerAvailable { get; }

        // Pays a one-time cost from stored energy.
        bool TrySpend(float amount);
    }

    public sealed class ArmorEnergyBudget
    {
        public float Available { get; private set; }
        public float Capacity { get; }

        public ArmorEnergyBudget(float available, float capacity)
        {
            Available = Mathf.Max(0f, available);
            Capacity = Mathf.Max(0f, capacity);
        }

        // Repairs, restarts and similar operations require full payment.
        // A failed payment does not consume any energy.
        public bool TrySpend(float amount)
        {
            if (float.IsNaN(amount) ||
                float.IsInfinity(amount) ||
                amount < 0f ||
                amount > Capacity + 0.000001f ||
                Available + 0.000001f < amount)
                return false;

            Available = Mathf.Max(0f, Available - amount);
            return true;
        }

        internal bool PayRunningCost(float amount)
        {
            amount = Mathf.Max(0f, amount);

            if (Available + 0.000001f < amount)
            {
                Available = 0f;
                return false;
            }

            Available = Mathf.Max(0f, Available - amount);
            return true;
        }
    }

    public abstract class ArmorSystem
    {
        public CompModuleSlots Host { get; private set; }
        public ArmorSystemDef Definition { get; private set; }

        internal void Bind(
            CompModuleSlots host,
            ArmorSystemDef definition)
        {
            Host = host;
            Definition = definition;
            Initialize();
        }

        public virtual void Initialize() { }
        public virtual void ExposeData() { }
        public virtual void AfterLoad() { }
        public virtual void Tick() { }
        public virtual void ModulesChanged() { }

        public virtual void ModuleInstalled(ThingDef module) { }
        public virtual void ModuleRemoved(ThingDef module) { }

        public virtual void ModuleToggled(
            ThingDef module,
            bool enabled) { }

        // Return null to allow the operation, or a reason to block it.
        public virtual string InstallBlockedReason(ThingDef module)
            => null;

        public virtual string RemoveBlockedReason(ThingDef module)
            => null;

        public virtual float CapacityContribution => 0f;
        public virtual bool IsEnergySource => false;
        public virtual float GenerationPer60 => 0f;
        public virtual float GenerateForTick() => 0f;

        public virtual void ModifyRunningCost(
            ThingDef module,
            ref float cost) { }

        // Priority payments, such as shield restarts.
        public virtual void BeforeRunningEnergy(
            ArmorEnergyBudget budget) { }

        // Payments performed after running costs, such as repairs.
        public virtual void AfterRunningEnergy(
            ArmorEnergyBudget budget) { }

        public virtual void PowerLost() { }
        public virtual void EnergyEmpty() { }

        public virtual float StatOffset(StatDef stat) => 0f;

        public virtual void BeforeDamage(
            ref DamageInfo damage,
            ref bool absorbed) { }

        public virtual bool AllowVerb(Verb verb) => true;
        public virtual void DrawWorn() { }

        public virtual IEnumerable<Gizmo> GetWornGizmos()
        {
            yield break;
        }

        public virtual IEnumerable<string> DescribeModule(
            ThingDef module)
        {
            yield break;
        }

        public virtual void DrawSettingsHeader(
            Rect area,
            ref float y) { }

        public virtual void DrawModuleActions(
            ThingDef module,
            Rect area) { }

        // Optional secondary bar for the energy panel.
        public virtual ArmorStatusBar SecondaryEnergyBar => null;
    }
}