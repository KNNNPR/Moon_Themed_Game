using System.Collections.Generic;
using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    // On-screen text for the repair job in the game's amber terminal look (PT Mono on dark glass).
    public static class RepairHud
    {
        public static readonly Color Amber = new Color(1, 0.69f, 0.22f);
        public static readonly Color Dim = new Color(1, 0.69f, 0.22f, 0.5f);
        static readonly Color Glass = new Color(0.035f, 0.025f, 0.012f, 0.84f);
        static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        static Texture2D white;
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

        public static GUIStyle Style(int size, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            int key = size * 16 + (int)anchor;
            if (styles.TryGetValue(key, out var style) && style != null) return style;
            style = new GUIStyle(GUI.skin.label) { alignment = anchor, fontSize = size, wordWrap = true, richText = false };
            if (HudFont) style.font = HudFont;
            style.normal.textColor = Amber;
            styles[key] = style;
            return style;
        }

        static Texture2D White
        {
            get
            {
                if (!white)
                {
                    white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    white.SetPixel(0, 0, Color.white);
                    white.Apply();
                }
                return white;
            }
        }

        public static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = previous;
        }

        public static void Panel(Rect rect, Color border)
        {
            Fill(rect, Glass);
            Fill(new Rect(rect.x, rect.y, rect.width, 1), border);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), border);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), border);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), border);
        }

        public static void Text(Rect rect, string text, GUIStyle style, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        // A one-line hint on a dark strip, sized to its text.
        public static void Hint(float centerX, float y, string text, int size = 15)
        {
            if (string.IsNullOrEmpty(text)) return;
            var style = Style(size);
            var content = new GUIContent(text);
            float width = Mathf.Min(Screen.width - 32, style.CalcSize(content).x + 28);
            float height = style.CalcHeight(content, width) + 8;
            var rect = new Rect(centerX - width / 2, y, width, height);
            Panel(rect, Dim);
            GUI.Label(rect, text, style);
        }
    }
}
