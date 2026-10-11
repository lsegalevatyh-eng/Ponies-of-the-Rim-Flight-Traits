using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class Dialog_ArmorSettings : Window
    {
        private readonly CompModuleSlots comp;
        private Vector2 scroll;

        public override Vector2 InitialSize =>
            new Vector2(900f, 760f);

        public Dialog_ArmorSettings(CompModuleSlots comp)
        {
            this.comp = comp;

            doCloseX = true;
            doCloseButton = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (comp.parent.Destroyed)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;

            Widgets.Label(
                new Rect(0f, 0f, rect.width - 240f, 36f),
                ModuleUtility.T(
                    "MA_Settings",
                    "Armor settings"));

            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(
                new Rect(rect.width - 220f, 3f, 180f, 32f),
                ModuleUtility.T(
                    "MA_Install",
                    "Install module")))
                comp.OpenInstallMenu();

            float y = 45f;

            Widgets.Label(
                new Rect(0f, y, rect.width, 30f),
                comp.ArmorLabel);

            y += 35f;

            Widgets.Label(
                new Rect(0f, y, rect.width, 55f),
                comp.SlotStatus);

            y += 60f;

            if (comp.InstallationPending)
            {
                Rect progress = new Rect(
                    0f,
                    y,
                    rect.width,
                    28f);

                Widgets.DrawBoxSolid(
                    progress,
                    new Color(0.12f, 0.12f, 0.12f));

                float fraction = comp.InstallationFraction;

                if (fraction > 0f)
                {
                    Widgets.DrawBoxSolid(
                        new Rect(
                            progress.x,
                            progress.y,
                            progress.width * fraction,
                            progress.height),
                        new Color(0.30f, 0.70f, 0.95f));
                }

                Text.Anchor = TextAnchor.MiddleCenter;

                Widgets.Label(
                    progress,
                    comp.InstallationStatus);

                Text.Anchor = TextAnchor.UpperLeft;
                y += 38f;
            }

            foreach (ArmorSystem system in comp.Systems)
                system.DrawSettingsHeader(rect, ref y);

            Rect outer = new Rect(
                0f,
                y,
                rect.width,
                Mathf.Max(80f, rect.height - y - 65f));

            var installed =
                new List<ThingDef>(comp.InstalledModules);

            const float rowHeight = 340f;

            Rect view = new Rect(
                0f,
                0f,
                outer.width - 20f,
                Mathf.Max(
                    outer.height,
                    installed.Count * rowHeight));

            Widgets.BeginScrollView(
                outer,
                ref scroll,
                view);

            if (installed.Count == 0)
            {
                Widgets.Label(
                    new Rect(8f, 8f, view.width - 16f, 40f),
                    ModuleUtility.T(
                        "MA_Empty",
                        "No modules installed."));
            }

            for (int i = 0; i < installed.Count; i++)
            {
                ThingDef module = installed[i];
                ModuleExtension info = ModuleUtility.Info(module);

                Rect row = new Rect(
                    0f,
                    i * rowHeight,
                    view.width,
                    rowHeight - 8f);

                Widgets.DrawMenuSection(row);

                GUI.DrawTexture(
                    new Rect(
                        10f,
                        row.y + 10f,
                        44f,
                        44f),
                    module.uiIcon,
                    ScaleMode.ScaleToFit);

                Widgets.Label(
                    new Rect(
                        65f,
                        row.y + 10f,
                        row.width - 265f,
                        30f),
                    module.LabelCap.ToString());

                Widgets.Label(
                    new Rect(
                        65f,
                        row.y + 46f,
                        row.width - 270f,
                        row.height - 55f),
                    comp.ModuleDescription(module));

                if (Widgets.ButtonText(
                    new Rect(
                        row.width - 110f,
                        row.y + 10f,
                        100f,
                        30f),
                    ModuleUtility.T(
                        "MA_Remove",
                        "Remove")))
                    comp.RemoveModule(module);

                if (info?.active == true)
                {
                    if (Widgets.ButtonText(
                        new Rect(
                            row.width - 180f,
                            row.y + 48f,
                            170f,
                            30f),
                        comp.IsEnabled(module)
                            ? ModuleUtility.T(
                                "MA_SwitchOff",
                                "Switch off")
                            : ModuleUtility.T(
                                "MA_SwitchOn",
                                "Switch on")))
                        comp.Toggle(module);
                }

                Rect actions = new Rect(
                    row.width - 180f,
                    row.y + 86f,
                    170f,
                    row.height - 96f);

                foreach (ArmorSystem system in comp.Systems)
                    system.DrawModuleActions(module, actions);
            }

            Widgets.EndScrollView();
        }
    }
}