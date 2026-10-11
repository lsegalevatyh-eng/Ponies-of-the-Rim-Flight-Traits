using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class ArmorReactorSystem : ArmorSystem
    {
        private int storedFuel;
        private int fuelTicksRemaining;
        private int burningFuelUnits;
        private ThingDef fuelReactorDef;

        private ThingDef CoreDef =>
            Host.InstalledModules.FirstOrDefault(d =>
                (ModuleUtility.Info(d)?.coreCapacity ?? 0f) > 0f);

        private ModuleExtension Core =>
            ModuleUtility.Info(CoreDef);

        public bool HasFuelReactor =>
            Core?.reactorFuel != null;

        public override bool IsEnergySource =>
            Host.IsCuirass && Core != null;

        public override float CapacityContribution =>
            Mathf.Max(0f, Core?.coreCapacity ?? 0f) +
            Host.InstalledModules.Sum(d =>
                Mathf.Max(
                    0f,
                    ModuleUtility.Info(d)?.batteryCapacity ?? 0f));

        private float NominalRecharge =>
            Host.IsCuirass
                ? Mathf.Max(0f, Core?.coreRecharge ?? 0f)
                : 0f;

        public override float GenerationPer60 =>
            !HasFuelReactor ||
            fuelTicksRemaining > 0 ||
            storedFuel >= Math.Max(1, Core.reactorFuelPerCycle)
                ? NominalRecharge
                : 0f;

        public int FuelCapacity =>
            HasFuelReactor
                ? Math.Max(0, Core.reactorFuelCapacity)
                : 0;

        public float FuelAmount =>
            !HasFuelReactor ? 0f :
            storedFuel + burningFuelUnits *
            fuelTicksRemaining /
            (float)Math.Max(1, Core.reactorCycleTicks);

        public string FuelStatus =>
            !HasFuelReactor ? "" :
            Core.reactorFuel.LabelCap + ": " +
            FuelAmount.ToString("0.0") + "/" + FuelCapacity +
            " | " +
            ModuleUtility.T(
                "MA_FuelOperatingTime",
                "Operating time remaining: ") +
            ModuleUtility.TimeText(
                fuelTicksRemaining +
                storedFuel / Math.Max(1, Core.reactorFuelPerCycle) *
                Math.Max(1, Core.reactorCycleTicks));

        public override void ExposeData()
        {
            Scribe_Values.Look(
                ref storedFuel,
                "maStoredFuel",
                0);

            Scribe_Values.Look(
                ref fuelTicksRemaining,
                "maFuelTicksRemaining",
                0);

            Scribe_Values.Look(
                ref burningFuelUnits,
                "maBurningFuelUnits",
                0);

            Scribe_Defs.Look(
                ref fuelReactorDef,
                "maFuelReactorDef");
        }

        public override void AfterLoad()
        {
            storedFuel = Math.Max(0, storedFuel);
            fuelTicksRemaining = Math.Max(0, fuelTicksRemaining);
            burningFuelUnits = Math.Max(0, burningFuelUnits);

            if (fuelReactorDef != CoreDef)
                ResetFuel();
        }

        public override void ModulesChanged()
        {
            if (fuelReactorDef != CoreDef)
                ResetFuel();
        }

        private void ResetFuel()
        {
            storedFuel = 0;
            fuelTicksRemaining = 0;
            burningFuelUnits = 0;
            fuelReactorDef = CoreDef;
        }

        public override float GenerateForTick()
        {
            if (!Host.IsCuirass || Core == null)
                return 0f;

            if (!HasFuelReactor)
                return NominalRecharge / 60f;

            if (fuelReactorDef != CoreDef)
                ResetFuel();

            if (fuelTicksRemaining <= 0)
            {
                int required = Math.Max(
                    1,
                    Core.reactorFuelPerCycle);

                if (storedFuel < required)
                    return 0f;

                storedFuel -= required;
                burningFuelUnits = required;

                fuelTicksRemaining = Math.Max(
                    1,
                    Core.reactorCycleTicks);
            }

            float generated = NominalRecharge / 60f;

            if (--fuelTicksRemaining == 0)
                burningFuelUnits = 0;

            return generated;
        }

        public override string RemoveBlockedReason(
            ThingDef module)
        {
            if (module != CoreDef || storedFuel <= 0)
                return null;

            return ModuleUtility.T(
                "MA_UnloadFuelFirst",
                "Unload unused fuel first");
        }

        public void OpenRefuelMenu()
        {
            if (!HasFuelReactor)
                return;

            int space = Mathf.Max(
                0,
                Mathf.FloorToInt(
                    FuelCapacity - FuelAmount + 0.0001f));

            var options = new List<FloatMenuOption>();

            if (space > 0)
            {
                foreach (Thing thing in Host.NearbyThings(
                    d => d == Core.reactorFuel,
                    Host.Wearer))
                {
                    Thing selected = thing;

                    options.Add(new FloatMenuOption(
                        selected.LabelCap + " — +" +
                        Math.Min(space, selected.stackCount),
                        () => Refuel(selected)));
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(
                    space <= 0
                        ? ModuleUtility.T(
                            "MA_FuelFull",
                            "Fuel tank full")
                        : ModuleUtility.T(
                            "MA_NoFuelNearby",
                            "No suitable fuel nearby"),
                    null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void Refuel(Thing fuel)
        {
            if (!HasFuelReactor ||
                fuel == null ||
                fuel.def != Core.reactorFuel ||
                !Host.IsAvailable(fuel, Host.Wearer))
                return;

            if (fuelReactorDef != CoreDef)
                ResetFuel();

            int amount = Math.Min(
                fuel.stackCount,
                Mathf.Max(
                    0,
                    Mathf.FloorToInt(
                        FuelCapacity - FuelAmount + 0.0001f)));

            if (amount <= 0)
                return;

            Thing consumed = fuel.SplitOff(amount);
            storedFuel += amount;
            consumed.Destroy(DestroyMode.Vanish);
        }

        public void UnloadFuel()
        {
            if (!HasFuelReactor || storedFuel <= 0)
                return;

            Thing item = ThingMaker.MakeThing(Core.reactorFuel);
            item.stackCount = storedFuel;

            if (!Host.ReturnItem(item))
            {
                item.Destroy(DestroyMode.Vanish);

                ModuleUtility.Reject(ModuleUtility.T(
                    "MA_CannotRemove",
                    "Cannot return the module to inventory or place it on the ground."));

                return;
            }

            storedFuel = 0;
        }

        public override IEnumerable<string> DescribeModule(
            ThingDef module)
        {
            ModuleExtension x = ModuleUtility.Info(module);

            if (x == null)
                yield break;

            if (x.coreCapacity > 0f)
            {
                yield return ModuleUtility.T(
                    "MA_CoreLocalCapacity",
                    "Core capacity: ") +
                    x.coreCapacity.ToString("0.#");

                yield return ModuleUtility.T(
                    "MA_Recharge",
                    "Generation / 60 ticks: ") +
                    x.coreRecharge.ToString("0.##");
            }

            if (x.batteryCapacity > 0f)
            {
                yield return ModuleUtility.T(
                    "MA_LocalBatteryCapacity",
                    "This armor piece's capacity: +") +
                    x.batteryCapacity.ToString("0.#");
            }

            if (x.reactorFuel != null)
            {
                yield return ModuleUtility.T(
                    "MA_ReactorFuelType",
                    "Fuel: ") +
                    x.reactorFuel.LabelCap;

                yield return ModuleUtility.T(
                    "MA_ReactorFuelRate",
                    "Per cycle: ") +
                    x.reactorFuelPerCycle + " / " +
                    ModuleUtility.TimeText(x.reactorCycleTicks);

                yield return ModuleUtility.T(
                    "MA_ReactorFuelCapacity",
                    "Fuel capacity: ") +
                    x.reactorFuelCapacity;
            }
        }

        public override void DrawSettingsHeader(
            Rect area,
            ref float y)
        {
            if (!HasFuelReactor)
                return;

            Widgets.Label(
                new Rect(0f, y, area.width - 300f, 45f),
                FuelStatus);

            if (Widgets.ButtonText(
                new Rect(area.width - 285f, y, 130f, 30f),
                ModuleUtility.T("MA_Refuel", "Refuel")))
                OpenRefuelMenu();

            if (Widgets.ButtonText(
                new Rect(area.width - 145f, y, 135f, 30f),
                ModuleUtility.T("MA_UnloadFuel", "Unload fuel")))
                UnloadFuel();

            y += 50f;
        }

        public override IEnumerable<Gizmo> GetWornGizmos()
        {
            if (!HasFuelReactor ||
                Find.Selector.SingleSelectedThing != Host.Wearer)
                yield break;

            yield return new Gizmo_ArmorStatus(
                ModuleUtility.T("MA_FuelPanel", "Reactor fuel"),
                FuelStatus,
                new List<ArmorStatusBar>
                {
                    new ArmorStatusBar
                    {
                        Label = ModuleUtility.T("MA_Fuel", "Fuel"),
                        Value = FuelAmount,
                        Maximum = FuelCapacity,
                        Color = new Color(0.55f, 0.80f, 0.30f)
                    }
                },
                ModuleUtility.T("MA_Refuel", "Refuel"),
                OpenRefuelMenu,
                -99.9f);
        }
    }
}