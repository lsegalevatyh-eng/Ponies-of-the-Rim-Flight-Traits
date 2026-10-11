using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class ArmorEnergySystem : ArmorSystem, IArmorEnergy
    {
        private float energy;
        private bool runningPowerAvailable;
        private int lastEnergyTick = -1;
        private float receivedPer60;
        private float sentPer60;

        public float Energy => energy;

        public float Capacity =>
            Host.Systems.Sum(s =>
                Mathf.Max(0f, s.CapacityContribution));

        public bool RunningPowerAvailable =>
            runningPowerAvailable;

        public float GenerationPer60Total =>
            Host.Systems.Sum(s =>
                Mathf.Max(0f, s.GenerationPer60));

        private bool HasSource =>
            Host.IsCuirass &&
            Host.Systems.Any(s => s.IsEnergySource);

        public bool TrySpend(float amount)
        {
            if (float.IsNaN(amount) ||
                float.IsInfinity(amount) ||
                amount < 0f ||
                energy + 0.000001f < amount)
                return false;

            energy = Mathf.Max(0f, energy - amount);

            if (energy <= 0f)
            {
                foreach (ArmorSystem system in Host.Systems)
                    system.EnergyEmpty();
            }

            Host.NotifyStatsChanged();
            return true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(
                ref energy,
                "maEnergy",
                0f);
        }

        public override void AfterLoad()
        {
            energy = Mathf.Clamp(energy, 0f, Capacity);
            runningPowerAvailable = false;
            lastEnergyTick = -1;
        }

        public override void ModulesChanged()
        {
            energy = Mathf.Clamp(energy, 0f, Capacity);

            if (Capacity <= 0f)
            {
                runningPowerAvailable = false;

                foreach (ArmorSystem system in Host.Systems)
                    system.PowerLost();
            }
        }

        public override void Tick()
        {
            if (Host.Wearer == null)
            {
                energy = Mathf.Clamp(energy, 0f, Capacity);
                runningPowerAvailable = false;
                receivedPer60 = 0f;
                sentPer60 = 0f;

                foreach (ArmorSystem system in Host.Systems)
                    system.PowerLost();

                return;
            }

            ArmorEnergySystem charger =
                Host.GetWornComponents()
                    .Select(c => c.GetSystem<ArmorEnergySystem>())
                    .FirstOrDefault(e =>
                        e != null && e.HasSource);

            if (charger != null)
                charger.TickWearerEnergy();
            else
                ProcessLocalEnergy(0f);
        }

        private void TickWearerEnergy()
        {
            if (lastEnergyTick == Find.TickManager.TicksGame)
                return;

            List<CompModuleSlots> worn =
                Host.GetWornComponents();

            float surplus = ProcessLocalEnergy(0f);
            float sent = 0f;

            foreach (CompModuleSlots comp in worn)
            {
                if (comp == Host || comp.IsCuirass)
                    continue;

                ArmorEnergySystem target =
                    comp.GetSystem<ArmorEnergySystem>();

                if (target == null)
                    continue;

                float offered = surplus;
                surplus = target.ProcessLocalEnergy(offered);

                sent += Mathf.Max(0f, offered - surplus);
            }

            sentPer60 = sent * 60f;

            foreach (CompModuleSlots comp in worn)
            {
                if (comp != Host && comp.IsCuirass)
                {
                    comp.GetSystem<ArmorEnergySystem>()
                        ?.ProcessLocalEnergy(0f);
                }
            }
        }

        private float ProcessLocalEnergy(float incoming)
        {
            int tick = Find.TickManager.TicksGame;

            if (lastEnergyTick == tick)
                return Mathf.Max(0f, incoming);

            lastEnergyTick = tick;
            receivedPer60 = 0f;
            sentPer60 = 0f;

            incoming = Mathf.Max(0f, incoming);
            energy = Mathf.Clamp(energy, 0f, Capacity);

            if (Host.Wearer == null || Capacity <= 0f)
            {
                runningPowerAvailable = false;

                foreach (ArmorSystem system in Host.Systems)
                    system.PowerLost();

                return incoming;
            }

            float generated = Host.Systems.Sum(s =>
                Mathf.Max(0f, s.GenerateForTick()));

            var budget = new ArmorEnergyBudget(
                energy + generated + incoming,
                Capacity);

            runningPowerAvailable = true;

            foreach (ArmorSystem system in Host.Systems)
                system.BeforeRunningEnergy(budget);

            float cost = Host.RunningConsumption / 60f;

            if (!budget.PayRunningCost(cost))
            {
                runningPowerAvailable = false;

                foreach (ArmorSystem system in Host.Systems)
                    system.PowerLost();
            }
            else
            {
                runningPowerAvailable =
                    cost > 0f || budget.Available > 0f;

                if (runningPowerAvailable)
                {
                    foreach (ArmorSystem system in Host.Systems)
                        system.AfterRunningEnergy(budget);
                }

                if (budget.Available <= 0f)
                {
                    foreach (ArmorSystem system in Host.Systems)
                        system.EnergyEmpty();
                }
            }

            energy = Mathf.Clamp(
                budget.Available,
                0f,
                Capacity);

            float overflow = Mathf.Max(
                0f,
                budget.Available - Capacity);

            receivedPer60 = Mathf.Clamp(
                incoming - overflow,
                0f,
                incoming) * 60f;

            return overflow;
        }

        public string Status =>
            ModuleUtility.T("MA_LocalEnergy", "Local energy: ") +
            energy.ToString("0.0") + "/" +
            Capacity.ToString("0.#") + "\n" +
            ModuleUtility.T("MA_LocalGeneration", "Generation: ") +
            GenerationPer60Total.ToString("0.##") + " | " +
            ModuleUtility.T("MA_LocalDrain", "Running drain: ") +
            Host.RunningConsumption.ToString("0.##") +
            ModuleUtility.T("MA_Per60", " / 60 ticks") +
            "\n" +
            (Host.IsCuirass
                ? ModuleUtility.T(
                    "MA_SentToHelmet",
                    "Sent to helmet: ") +
                  sentPer60.ToString("0.##")
                : ModuleUtility.T(
                    "MA_ReceivedFromCuirass",
                    "Received from cuirass: ") +
                  receivedPer60.ToString("0.##"));

        public override void DrawSettingsHeader(
            Rect area,
            ref float y)
        {
            if (Capacity <= 0f)
                return;

            Widgets.Label(
                new Rect(0f, y, area.width, 75f),
                Status);

            y += 80f;
        }

        public override IEnumerable<Gizmo> GetWornGizmos()
        {
            if (Capacity <= 0f ||
                Find.Selector.SingleSelectedThing != Host.Wearer)
                yield break;

            var bars = new List<ArmorStatusBar>
            {
                new ArmorStatusBar
                {
                    Label = ModuleUtility.T("MA_EnergyBar", "Energy"),
                    Value = energy,
                    Maximum = Capacity,
                    Color = new Color(0.92f, 0.75f, 0.22f)
                }
            };

            ArmorStatusBar secondary =
                Host.Systems
                    .Select(s => s.SecondaryEnergyBar)
                    .FirstOrDefault(b => b != null);

            if (secondary != null)
                bars.Add(secondary);

            yield return new Gizmo_ArmorStatus(
                Host.IsCuirass
                    ? ModuleUtility.T(
                        "MA_CuirassEnergyPanel",
                        "Cuirass — energy")
                    : ModuleUtility.T(
                        "MA_HelmetEnergyPanel",
                        "Helmet — energy"),
                Status,
                bars,
                null,
                null,
                Host.IsCuirass ? -100f : -98f);
        }
    }
}