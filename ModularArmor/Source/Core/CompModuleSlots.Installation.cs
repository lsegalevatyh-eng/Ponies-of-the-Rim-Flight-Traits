using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularArmor
{
    public partial class CompModuleSlots
    {
        private Pawn installingPawn;
        private ThingDef installingDef;
        private int installationProgressTicks;
        private bool removingModule;

        // The name is preserved for compatibility with the existing UI.
        // This now represents either installation or removal.
        public bool InstallationPending =>
            installingPawn != null && installingDef != null;

        public bool RemovalPending =>
            InstallationPending && removingModule;

        public ThingDef PendingModuleDef =>
            InstallationPending ? installingDef : null;

        private int CurrentOperationDuration
        {
            get
            {
                ModuleExtension info =
                    ModuleUtility.Info(installingDef);

                return Math.Max(
                    1,
                    removingModule
                        ? info?.removeTicks ?? 2500
                        : info?.installTicks ?? 2500);
            }
        }

        public float InstallationFraction =>
            !InstallationPending ? 0f :
            Mathf.Clamp01(
                installationProgressTicks /
                (float)CurrentOperationDuration);

        public string InstallationStatus =>
            !InstallationPending ? "" :
            (removingModule
                ? ModuleUtility.T("MA_Removing", "Removing: ")
                : ModuleUtility.T("MA_Installing", "Installing: ")) +
            installingDef.LabelCap + " — " +
            (InstallationFraction * 100f).ToString("0") + "%";

        private void ExposeInstallation()
        {
            Scribe_References.Look(
                ref installingPawn,
                "maInstallingPawn");

            Scribe_Defs.Look(
                ref installingDef,
                "maInstallingDef");

            Scribe_Values.Look(
                ref installationProgressTicks,
                "maInstallationProgress",
                0);

            Scribe_Values.Look(
                ref removingModule,
                "maRemovingModule",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit &&
                !InstallationPending)
                ClearInstallation();
        }

        public static bool InInventory(Pawn pawn, Thing thing)
        {
            return pawn?.inventory != null &&
                pawn.inventory.innerContainer.Contains(thing);
        }

        public bool IsAvailable(
            Thing thing,
            Pawn inventoryOwner)
        {
            if (thing == null ||
                thing.Destroyed ||
                thing.stackCount <= 0)
                return false;

            if (InInventory(inventoryOwner, thing))
                return true;

            return CurrentMap != null &&
                thing.Spawned &&
                thing.Map == CurrentMap &&
                !thing.IsForbidden(Faction.OfPlayer) &&
                thing.Position.DistanceToSquared(CurrentPosition) <= 4;
        }

        public List<Thing> NearbyThings(
            Func<ThingDef, bool> filter,
            Pawn inventoryOwner)
        {
            var result = new List<Thing>();

            if (inventoryOwner?.inventory != null)
            {
                foreach (Thing thing in
                    inventoryOwner.inventory.innerContainer)
                {
                    if (filter(thing.def) &&
                        IsAvailable(thing, inventoryOwner))
                        result.Add(thing);
                }
            }

            if (CurrentMap != null)
            {
                foreach (ThingDef def in
                    DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (!filter(def))
                        continue;

                    foreach (Thing thing in
                        CurrentMap.listerThings.ThingsOfDef(def))
                    {
                        if (IsAvailable(thing, inventoryOwner))
                            result.Add(thing);
                    }
                }
            }

            return result;
        }

        public bool ReturnItem(Thing item)
        {
            if (Wearer?.inventory != null &&
                Wearer.inventory.innerContainer.TryAdd(item))
                return true;

            return CurrentMap != null &&
                GenPlace.TryPlaceThing(
                    item,
                    CurrentPosition,
                    CurrentMap,
                    ThingPlaceMode.Near);
        }

        private static string BusyOperationMessage =>
            ModuleUtility.T(
                "MA_MaintenanceBusy",
                "Finish installation or removal, or cancel the pawn's job.");

        public void OpenInstallMenu()
        {
            if (InstallationPending)
            {
                ModuleUtility.Reject(BusyOperationMessage);
                return;
            }

            if (FreeSlots <= 0)
            {
                ModuleUtility.Reject(ModuleUtility.T(
                    "MA_NoFreeSlots",
                    "No free module slots."));
                return;
            }

            if (Wearer != null)
            {
                OpenModuleMenu(Wearer);
                return;
            }

            var options = new List<FloatMenuOption>();

            if (parent.Spawned)
            {
                foreach (Pawn pawn in
                    parent.Map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.Dead ||
                        pawn.Downed ||
                        !pawn.CanReach(
                            parent,
                            PathEndMode.Touch,
                            Danger.Deadly))
                        continue;

                    Pawn selected = pawn;

                    options.Add(new FloatMenuOption(
                        selected.LabelShortCap.ToString(),
                        () => OpenModuleMenu(selected)));
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(
                    ModuleUtility.T(
                        "MA_NoInstaller",
                        "No available installer"),
                    null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenModuleMenu(Pawn pawn)
        {
            if (InstallationPending)
            {
                ModuleUtility.Reject(BusyOperationMessage);
                return;
            }

            var options = new List<FloatMenuOption>();

            foreach (var group in NearbyThings(CanInstall, pawn)
                .GroupBy(t => t.def)
                .OrderBy(g => g.Key.label))
            {
                Thing selected = group.First();

                options.Add(new FloatMenuOption(
                    selected.def.LabelCap + " — " +
                    ModuleUtility.TimeText(
                        ModuleUtility.Info(selected.def).installTicks),
                    () => StartInstallation(pawn, selected)));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(
                    ModuleUtility.T(
                        "MA_NoAvailableModules",
                        "No compatible modules nearby or in inventory."),
                    null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void StartInstallation(Pawn pawn, Thing module)
        {
            if (InstallationPending ||
                pawn == null ||
                !pawn.Spawned ||
                pawn.Dead ||
                pawn.Downed ||
                !IsAvailable(module, pawn) ||
                !CanInstall(module.def))
                return;

            JobDef jobDef =
                DefDatabase<JobDef>.GetNamed("MA_InstallModule");

            installingPawn = pawn;
            installingDef = module.def;
            installationProgressTicks = 0;
            removingModule = false;

            Job job = JobMaker.MakeJob(
                jobDef,
                parent,
                module,
                pawn);

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void CheckInstallation()
        {
            if (!InstallationPending)
                return;

            Job job = installingPawn.CurJob;

            string expectedJob = removingModule
                ? "MA_RemoveModule"
                : "MA_InstallModule";

            if (installingPawn.Dead ||
                job == null ||
                job.def.defName != expectedJob ||
                job.targetA.Thing != parent)
                ClearInstallation();
        }

        private void ClearInstallation()
        {
            installingPawn = null;
            installingDef = null;
            installationProgressTicks = 0;
            removingModule = false;
        }

        public void SetInstallationProgress(
            Pawn pawn,
            int elapsed)
        {
            if (installingPawn == pawn)
                installationProgressTicks = Math.Max(0, elapsed);
        }

        public bool FinishInstallation(
            Pawn pawn,
            Thing module)
        {
            if (removingModule ||
                module == null ||
                module.Destroyed ||
                installingPawn != pawn ||
                module.def != installingDef ||
                !CanInstall(module.def))
                return false;

            Thing consumed = module.SplitOff(1);
            ThingDef def = consumed.def;

            modules.Add(def);
            consumed.Destroy(DestroyMode.Vanish);

            ClearInstallation();

            foreach (ArmorSystem system in Systems)
                system.ModuleInstalled(def);

            NotifyModulesChanged();
            return true;
        }

        // Checks removal restrictions without checking operation ownership.
        // Used both by the menu and by the running removal job.
        public string RemovalBlockedReason(ThingDef module)
        {
            if (parent.Destroyed ||
                module == null ||
                !modules.Contains(module))
            {
                return ModuleUtility.T(
                    "MA_ModuleNotInstalled",
                    "Module is not installed.");
            }

            foreach (ArmorSystem system in Systems)
            {
                string reason =
                    system.RemoveBlockedReason(module);

                if (reason != null)
                    return reason;
            }

            return null;
        }

        // Starts a removal job instead of removing the module immediately.
        public void RemoveModule(ThingDef module)
        {
            if (InstallationPending)
            {
                ModuleUtility.Reject(BusyOperationMessage);
                return;
            }

            string reason = RemovalBlockedReason(module);

            if (reason != null)
            {
                ModuleUtility.Reject(reason);
                return;
            }

            if (Wearer != null)
            {
                StartRemoval(Wearer, module);
                return;
            }

            var options = new List<FloatMenuOption>();

            if (parent.Spawned)
            {
                foreach (Pawn pawn in
                    parent.Map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.Dead ||
                        pawn.Downed ||
                        !pawn.CanReach(
                            parent,
                            PathEndMode.Touch,
                            Danger.Deadly))
                        continue;

                    Pawn selected = pawn;

                    options.Add(new FloatMenuOption(
                        selected.LabelShortCap + " — " +
                        ModuleUtility.TimeText(
                            ModuleUtility.Info(module)?.removeTicks ?? 2500),
                        () => StartRemoval(selected, module)));
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(
                    ModuleUtility.T(
                        "MA_NoInstaller",
                        "No available installer"),
                    null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void StartRemoval(
            Pawn pawn,
            ThingDef module)
        {
            if (InstallationPending)
            {
                ModuleUtility.Reject(BusyOperationMessage);
                return;
            }

            if (pawn == null ||
                !pawn.Spawned ||
                pawn.Dead ||
                pawn.Downed)
            {
                ModuleUtility.Reject(ModuleUtility.T(
                    "MA_WearerUnavailable",
                    "Wearer is unavailable"));
                return;
            }

            string reason = RemovalBlockedReason(module);

            if (reason != null)
            {
                ModuleUtility.Reject(reason);
                return;
            }

            if (Wearer != null && Wearer != pawn)
                return;

            if (Wearer == null &&
                (!parent.Spawned ||
                 !pawn.CanReach(
                     parent,
                     PathEndMode.Touch,
                     Danger.Deadly)))
                return;

            JobDef jobDef =
                DefDatabase<JobDef>.GetNamed("MA_RemoveModule");

            installingPawn = pawn;
            installingDef = module;
            installationProgressTicks = 0;
            removingModule = true;

            Job job = JobMaker.MakeJob(jobDef, parent);
            job.SetTarget(TargetIndex.C, pawn);

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool IsRemovalInProgressFor(
            Pawn pawn,
            ThingDef module)
        {
            return RemovalPending &&
                installingPawn == pawn &&
                installingDef == module;
        }

        public bool FinishRemoval(
            Pawn pawn,
            ThingDef module)
        {
            if (!IsRemovalInProgressFor(pawn, module))
                return false;

            string reason = RemovalBlockedReason(module);

            if (reason != null)
            {
                ModuleUtility.Reject(reason);
                return false;
            }

            Thing item = ThingMaker.MakeThing(module);

            // Remove the installed module only after its item
            // has been returned to inventory or placed on the ground.
            if (!ReturnItem(item))
            {
                item.Destroy(DestroyMode.Vanish);

                ModuleUtility.Reject(ModuleUtility.T(
                    "MA_CannotRemove",
                    "Cannot return the module to inventory or place it on the ground."));

                return false;
            }

            modules.Remove(module);
            enabled.Remove(module);

            ClearInstallation();

            foreach (ArmorSystem system in Systems)
                system.ModuleRemoved(module);

            NotifyModulesChanged();
            return true;
        }
    }
}