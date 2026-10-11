using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularArmor
{
    public class JobDriver_RemoveModule : JobDriver
    {
        private Thing Armor =>
            job.GetTarget(TargetIndex.A).Thing;

        private CompModuleSlots Comp =>
            (Armor as ThingWithComps)?.GetComp<CompModuleSlots>();

        public override bool TryMakePreToilReservations(
            bool errorOnFailed)
        {
            if (Armor == null ||
                Armor.Destroyed ||
                Comp == null)
                return false;

            if (!Comp.RemovalPending ||
                !Comp.IsRemovalInProgressFor(
                    pawn,
                    Comp.PendingModuleDef))
                return false;

            if (Comp.Wearer != null && Comp.Wearer != pawn)
                return false;

            if (Armor.Spawned)
            {
                return pawn.Reserve(
                    Armor,
                    job,
                    1,
                    -1,
                    null,
                    errorOnFailed);
            }

            return Comp.Wearer == pawn;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Operation state is stored and saved in the armor component.
            ThingDef moduleDef = Comp?.PendingModuleDef;

            this.FailOn(() =>
                Armor == null ||
                Armor.Destroyed ||
                Comp == null ||
                moduleDef == null ||
                !Comp.IsRemovalInProgressFor(pawn, moduleDef) ||
                Comp.RemovalBlockedReason(moduleDef) != null ||
                (Comp.Wearer != null && Comp.Wearer != pawn) ||
                (Comp.Wearer == null && !Armor.Spawned));

            int duration = Math.Max(
                1,
                ModuleUtility.Info(moduleDef)?.removeTicks ?? 2500);

            Toil wait = Toils_General.Wait(duration);
            Action originalTickAction = wait.tickAction;

            wait.tickAction = () =>
            {
                originalTickAction?.Invoke();

                Comp?.SetInstallationProgress(
                    pawn,
                    Math.Max(0, duration - ticksLeftThisToil));
            };

            wait.WithProgressBar(
                TargetIndex.C,
                () => Mathf.Clamp01(
                    1f - ticksLeftThisToil / (float)duration));

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
                if (!Comp.FinishRemoval(pawn, moduleDef))
                    EndJobWith(JobCondition.Incompletable);
            };

            yield return finish;
        }
    }
}