using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularArmor
{
    public class JobDriver_InstallModule : JobDriver
    {
        private Thing Armor =>
            job.GetTarget(TargetIndex.A).Thing;

        private Thing Module =>
            job.GetTarget(TargetIndex.B).Thing;

        private CompModuleSlots Comp =>
            (Armor as ThingWithComps)?.GetComp<CompModuleSlots>();

        private bool ModuleInInventory =>
            pawn.inventory != null &&
            pawn.inventory.innerContainer.Contains(Module);

        public override bool TryMakePreToilReservations(
            bool errorOnFailed)
        {
            if (Armor == null ||
                Armor.Destroyed ||
                Module == null ||
                Module.Destroyed ||
                Comp == null)
                return false;

            if (Comp.Wearer != null && Comp.Wearer != pawn)
                return false;

            if (Armor.Spawned &&
                !pawn.Reserve(
                    Armor,
                    job,
                    1,
                    -1,
                    null,
                    errorOnFailed))
                return false;

            if (Module.Spawned &&
                !pawn.Reserve(
                    Module,
                    job,
                    1,
                    -1,
                    null,
                    errorOnFailed))
                return false;

            return Module.Spawned || ModuleInInventory;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() =>
                Armor == null ||
                Armor.Destroyed ||
                Module == null ||
                Module.Destroyed ||
                Comp == null ||
                !Comp.CanInstall(Module.def) ||
                (Comp.Wearer != null && Comp.Wearer != pawn) ||
                (Comp.Wearer == null && !Armor.Spawned));

            int duration = Math.Max(
                1,
                ModuleUtility.Info(Module.def)?.installTicks ?? 2500);

            Toil acquire = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            acquire.initAction = () =>
            {
                if (pawn.inventory == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (ModuleInInventory && Module.stackCount == 1)
                    return;

                Thing item = Module.SplitOff(1);

                if (!pawn.inventory.innerContainer.TryAdd(item, false))
                {
                    if (pawn.Spawned)
                    {
                        GenPlace.TryPlaceThing(
                            item,
                            pawn.Position,
                            pawn.Map,
                            ThingPlaceMode.Near);
                    }

                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // An interrupted installation leaves the item
                // in the installer's inventory.
                job.SetTarget(TargetIndex.B, item);
            };

            Toil wait = Toils_General.Wait(duration);
            Action originalTickAction = wait.tickAction;

            wait.tickAction = () =>
            {
                originalTickAction?.Invoke();

                Comp?.SetInstallationProgress(
                    pawn,
                    Math.Max(0, duration - ticksLeftThisToil));
            };

            // Target C is the installer.
            // Display the standard progress bar above the pawn.
            wait.WithProgressBar(
                TargetIndex.C,
                () => Mathf.Clamp01(
                    1f - ticksLeftThisToil / (float)duration));

            // Inventory items do not need to be collected from the ground.
            yield return Toils_Jump.JumpIf(
                acquire,
                () => ModuleInInventory);

            yield return Toils_Goto.GotoThing(
                TargetIndex.B,
                PathEndMode.Touch);

            yield return acquire;

            // Worn armor can be serviced without moving.
            yield return Toils_Jump.JumpIf(
                wait,
                () => Comp.Wearer == pawn);

            // Approach armor lying on the ground.
            yield return Toils_Goto.GotoThing(
                TargetIndex.A,
                PathEndMode.Touch);

            yield return wait;

            Toil finish = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            finish.initAction = () =>
            {
                if (!Comp.FinishInstallation(pawn, Module))
                    EndJobWith(JobCondition.Incompletable);
            };

            yield return finish;
        }
    }
}