using System.Collections.Generic;
using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    // Quiet on-screen hints for the repair job: small PT Mono in soft light with a shadow, no panels.
    public static class RepairHud
    {
        static readonly Color Light = new Color(0.93f, 0.9f, 0.84f, 0.78f);
        static readonly Color Shadow = new Color(0, 0, 0, 0.55f);
        static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        static Font font;
        static bool searched;

        static Font HudFont
        {
            get
            {
                if (!searched)
                {
                    searched = true;
                    var catalog = Resources.Load<ShopCatalog>("ShopCatalog");
                    if (catalog) font = catalog.hudFont;
                }
                return font;
            }
        }

        static GUIStyle Style(int size, TextAnchor anchor)
        {
            int key = size * 16 + (int)anchor;
            if (styles.TryGetValue(key, out var style) && style != null) return style;
            style = new GUIStyle(GUI.skin.label) { alignment = anchor, fontSize = size, wordWrap = true, richText = false };
            if (HudFont) style.font = HudFont;
            style.normal.textColor = Color.white;
            styles[key] = style;
            return style;
        }

        static void Draw(Rect rect, string text, GUIStyle style)
        {
            var previous = GUI.color;
            GUI.color = Shadow;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
            GUI.color = Light;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        public static void Hint(float centerX, float y, string text, int size = 14)
        {
            if (string.IsNullOrEmpty(text)) return;
            float width = Mathf.Min(Screen.width - 32, 620);
            Draw(new Rect(centerX - width / 2, y, width, 44), text, Style(size, TextAnchor.UpperCenter));
        }

        public static void Corner(string text) =>
            Draw(new Rect(18, Screen.height - 40, 460, 26), text, Style(13, TextAnchor.MiddleLeft));
    }
}
