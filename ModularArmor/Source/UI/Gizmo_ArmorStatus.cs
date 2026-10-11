using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularArmor
{
    public class ArmorStatusBar
    {
        public string Label;
        public float Value;
        public float Maximum;
        public Color Color = Color.white;
    }

    public class Gizmo_ArmorStatus : Gizmo
    {
        private readonly string title;
        private readonly string tooltip;
        private readonly List<ArmorStatusBar> bars;
        private readonly string buttonLabel;
        private readonly Action buttonAction;

        public Gizmo_ArmorStatus(
            string title,
            string tooltip,
            List<ArmorStatusBar> bars,
            string buttonLabel,
            Action buttonAction,
            float order)
        {
            this.title = title;
            this.tooltip = tooltip;
            this.bars = bars;
            this.buttonLabel = buttonLabel;
            this.buttonAction = buttonAction;
            Order = order;
        }

        public override float GetWidth(float maxWidth) => 230f;

        public override GizmoResult GizmoOnGUI(
            Vector2 topLeft,
            float maxWidth,
            GizmoRenderParms parms)
        {
            Rect rect = new Rect(
                topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);

            Widgets.DrawWindowBackground(rect);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Widgets.Label(
                new Rect(rect.x + 6f, rect.y + 2f,
                    rect.width - 12f, 17f), title);

            int count = Math.Min(2, bars?.Count ?? 0);

            if (buttonAction != null)
                count = Math.Min(1, count);

            for (int i = 0; i < count; i++)
            {
                ArmorStatusBar bar = bars[i];

                Rect row = new Rect(
                    rect.x + 6f, rect.y + 21f + i * 26f,
                    rect.width - 12f, 20f);

                float fraction = bar.Maximum > 0f
                    ? Mathf.Clamp01(bar.Value / bar.Maximum) : 0f;

                Widgets.DrawBoxSolid(
                    row, new Color(0.12f, 0.12f, 0.12f));

                if (fraction > 0f)
                    Widgets.DrawBoxSolid(
                        new Rect(row.x, row.y,
                            row.width * fraction, row.height),
                        bar.Color);

                Text.Anchor = TextAnchor.MiddleCenter;

                Widgets.Label(
                    row, bar.Label + ": " +
                    bar.Value.ToString("0.0") + "/" +
                    bar.Maximum.ToString("0"));
            }

            if (buttonAction != null)
            {
                Rect button = new Rect(
                    rect.x + 6f, rect.y + 47f,
                    rect.width - 12f, 20f);

                if (Widgets.ButtonText(button, buttonLabel))
                    buttonAction();
            }

            TooltipHandler.TipRegion(rect, tooltip ?? "");

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            return new GizmoResult(GizmoState.Clear);
        }
    }
}