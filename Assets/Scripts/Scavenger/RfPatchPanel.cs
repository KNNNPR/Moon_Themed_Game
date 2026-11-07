using UnityEngine;
using Cursor = UnityEngine.Cursor;

namespace MoonThemedGame.Scavenger
{
    // Receiver tuning terminal in the robot feed ([H]), in the same worn phosphor style as the feed.
    // A rival intercept jams every channel but one; the operator patches the free channel into the tuner and locks.
    [DisallowMultipleComponent]
    public sealed class RfPatchPanel : MonoBehaviour
    {
        ScavengerLink link;
        ScavengerFeedHud hud;
        int plugged, held = -1;
        string message;
        bool messageAlarm;
        float messageUntil, closeAt = -1, nextNoise;
        float[] noise = new float[0];
        CursorLockMode previousLock;
        bool previousVisible;
        Texture2D dot;
        GUIStyle text, center, right;

        public bool IsOpen { get; private set; }

        SatelliteNetwork Network => link ? link.Network : null;
        Color Phosphor => hud ? hud.phosphor : new Color(0.78f, 0.96f, 0.8f);
        Color Warning => hud ? hud.warning : new Color(1f, 0.3f, 0.22f);

        void Awake()
        {
            link = GetComponent<ScavengerLink>();
            hud = GetComponent<ScavengerFeedHud>();
        }

        void OnDisable() => Close();

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            held = -1;
            closeAt = -1;
            if (Network) plugged = Network.CarrierChannel;
            previousLock = Cursor.lockState;
            previousVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            held = -1;
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
        }

        void Update()
        {
            if (IsOpen && (!link || link.Phase != LinkPhase.Live)) Close();
            if (IsOpen && closeAt > 0 && Time.time >= closeAt) Close();
        }

        static string Ghz(float value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        bool Ready => plugged >= 0 && !Network.IsJammed(plugged);

        void TryLock()
        {
            var network = Network;
            if (!Ready)
            {
                bool attack = network.Rival == RivalPhase.Incoming;
                network.PenalizeLock();
                string reason = plugged < 0 ? Loc.T("TUNER IS EMPTY") : Loc.F("CH{0} IS JAMMED", plugged + 1);
                Say(Loc.F("ERR NO LOCK - {0}", reason) + (attack ? Loc.F("  +{0}% TO RIVAL", Mathf.RoundToInt(network.lockPenalty * 100)) : ""), true);
                return;
            }
            bool repelled = network.Rival != RivalPhase.None;
            network.Repel(plugged);
            Say(repelled ? Loc.F("CARRIER LOCKED CH{0} - {1} LOST THE SIGNAL", plugged + 1, network.rivalName) : Loc.F("CARRIER STABLE CH{0}", plugged + 1), false);
            if (repelled) closeAt = Time.time + 1.4f;
        }

        void Say(string value, bool alarm)
        {
            message = value;
            messageAlarm = alarm;
            messageUntil = Time.time + 4;
        }

        static string Bar(float value, int cells)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(value * cells), 0, cells);
            return "[" + new string('|', filled) + new string('.', cells - filled) + "]";
        }

        void Print(Rect rect, string value, Color color, int salt, GUIStyle style)
        {
            if (hud) hud.Draw(rect, value, color, 400 + salt, style);
            else { style.normal.textColor = color; GUI.Label(rect, value, style); }
        }

        static Vector2 Pointer => Event.current.mousePosition;

        // Drawn by ScavengerFeedHud right after the feed text, so it always sits on top of it.
        internal void Gui()
        {
            if (!IsOpen || !Network) return;
            if (hud) hud.EnsureStyles();
            var network = Network;
            int channels = network.channels.Length;
            float h = Mathf.Min(Screen.height * 0.7f, Screen.width * 0.42f), w = h * 1.5f;
            float line = h / 18f;
            EnsureStyles(line);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            float pad = w * 0.04f;
            float Row(float r) => panel.y + line * r;

            var outputs = new Vector2[channels];
            for (int i = 0; i < channels; i++) outputs[i] = new Vector2(panel.x + w * (0.2f + 0.6f * i / Mathf.Max(1, channels - 1)), Row(11.2f));
            var tuner = new Vector2(panel.x + w * 0.5f, Row(14.4f));
            float reach = line * 0.9f;
            var lockButton = new Rect(panel.xMax - pad - w * 0.3f, Row(16.2f), w * 0.3f, line * 1.2f);
            HandleInput(outputs, tuner, reach, lockButton);
            if (Event.current.type != EventType.Repaint) return;

            bool ready = Ready;
            var phosphor = Phosphor;
            var dim = phosphor * 0.6f;
            var warning = Warning;
            var mouse = Pointer;

            Fill(panel, new Color(0.004f, 0.012f, 0.008f, 0.96f));
            Frame(panel, dim, 1);
            Frame(new Rect(panel.x + 4, panel.y + 4, panel.width - 8, panel.height - 8), phosphor * 0.25f, 1);

            Print(new Rect(panel.x + pad, Row(0.6f), w, line), Loc.T("RCVR-2 [CARRIER TUNER]"), phosphor, 0, text);
            Print(new Rect(panel.x, Row(0.6f), w - pad, line), Loc.T("[H] / [ESC] EXIT"), dim, 1, right);

            bool blink = Mathf.Repeat(Time.time * 1.6f, 1) < 0.7f;
            switch (network.Rival)
            {
                case RivalPhase.Incoming:
                    if (blink) Print(new Rect(panel.x + pad, Row(2f), w, line), Loc.F("ALERT  {0} IS JAMMING THE CARRIER", network.rivalName), warning, 2, text);
                    Print(new Rect(panel.x + pad, Row(3f), w, line), Loc.F("INTERCEPT {0} {1,3}%", Bar(network.RivalProgress, 20), Mathf.RoundToInt(network.RivalProgress * 100)), warning, 3, text);
                    break;
                case RivalPhase.Hijacked:
                    if (blink) Print(new Rect(panel.x + pad, Row(2f), w, line), Loc.F("ALERT  SIGNAL HIJACKED BY {0}", network.rivalName), warning, 2, text);
                    break;
                default:
                    Print(new Rect(panel.x + pad, Row(2f), w, line), Loc.T(network.Hopping ? "RETUNING..." : "NO RIVAL CARRIER"), dim, 2, text);
                    break;
            }
            Print(new Rect(panel.x + pad, Row(4.3f), w, line), Loc.T("> PLUG A FREE CHANNEL INTO THE TUNER, THEN LOCK"), phosphor, 4, text);

            if (noise.Length != channels) noise = new float[channels];
            if (Time.time >= nextNoise)
            {
                nextNoise = Time.time + 0.09f;
                for (int c = 0; c < channels; c++)
                {
                    float value = network.IsJammed(c) ? Random.Range(0.7f, 1f) : c == network.CarrierChannel ? Random.Range(0.45f, 0.55f) : Random.Range(0.05f, 0.15f);
                    noise[c] = Mathf.Lerp(noise[c], value, 0.6f);
                }
            }
            for (int c = 0; c < channels; c++)
            {
                bool jammed = network.IsJammed(c);
                bool free = !jammed && c != network.CarrierChannel;
                string state = Loc.T(jammed ? "JAMMED" : free ? "FREE" : "CARRIER");
                Print(new Rect(panel.x + pad, Row(5.7f + c), w, line), Loc.F("CH{0} {1} GHz {2} {3}", c + 1, Ghz(network.channels[c]), Bar(noise[c], 18), state), jammed ? warning : phosphor * (free ? 1 : 0.75f), 10 + c, text);
            }

            for (int i = 0; i < channels; i++)
            {
                bool hover = Vector2.Distance(mouse, outputs[i]) < reach;
                var color = hover ? Color.white : network.IsJammed(i) ? warning : dim;
                Print(new Rect(outputs[i].x - w * 0.08f, outputs[i].y - line * 1.1f, w * 0.16f, line), Loc.F("CH{0}", i + 1), color, 20 + i, center);
                Jack(outputs[i], line, plugged == i || held == i, hover, phosphor);
            }
            var tunerColor = ready ? phosphor : plugged < 0 ? dim : warning;
            string tunerTag = ready ? "[OK]" : plugged < 0 ? "[--]" : "[!!]";
            string tunerState = plugged < 0 ? Loc.T("EMPTY") : network.IsJammed(plugged) ? Loc.F("CH{0} JAMMED", plugged + 1) : Loc.F("CH{0} FREE", plugged + 1);
            Print(new Rect(tuner.x - w * 0.3f, tuner.y + line * 0.7f, w * 0.6f, line), Loc.F("{0} TUNER  {1}", tunerTag, tunerState), tunerColor, 30, center);
            Jack(tuner, line, plugged >= 0, held >= 0 && Vector2.Distance(mouse, tuner) < reach * 1.5f, phosphor);
            if (plugged >= 0) Cable(outputs[plugged], tuner, line, phosphor * 0.75f);
            if (held >= 0) Cable(outputs[held], mouse, line, Mathf.Repeat(Time.time * 4, 1) < 0.5f ? phosphor : phosphor * 0.7f);

            bool lockHover = lockButton.Contains(mouse);
            if (ready || lockHover) Fill(lockButton, ready ? phosphor * (lockHover ? 0.85f : 0.6f) : phosphor * 0.15f);
            Frame(lockButton, ready ? phosphor : dim, 1);
            center.normal.textColor = ready ? new Color(0.01f, 0.03f, 0.02f) : dim;
            GUI.Label(lockButton, Loc.T("[ LOCK CARRIER ]"), center);

            var messageRect = new Rect(panel.x + pad, lockButton.y, w * 0.6f, lockButton.height);
            if (message != null && Time.time < messageUntil)
                Print(messageRect, message, messageAlarm ? warning : phosphor, 50, text);
            else
                Print(messageRect, Loc.T("> CLICK A CHANNEL, THEN THE TUNER"), dim, 51, text);

            for (float y = panel.y; y < panel.yMax; y += 3) Fill(new Rect(panel.x, y, panel.width, 1), new Color(0, 0, 0, 0.28f));
            float roll = Mathf.Repeat(Time.time * 0.2f, 1.3f) - 0.15f;
            Fill(new Rect(panel.x, panel.y + panel.height * roll, panel.width, panel.height * 0.06f), new Color(phosphor.r, phosphor.g, phosphor.b, 0.03f));
        }

        void HandleInput(Vector2[] outputs, Vector2 tuner, float reach, Rect lockButton)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            var mouse = Pointer;
            if (e.button == 1) { held = -1; e.Use(); return; }
            if (e.button != 0) return;
            e.Use();
            if (lockButton.Contains(mouse)) { held = -1; TryLock(); return; }
            for (int i = 0; i < outputs.Length; i++)
                if (Vector2.Distance(mouse, outputs[i]) < reach)
                {
                    if (plugged == i) plugged = -1;
                    held = i;
                    return;
                }
            if (Vector2.Distance(mouse, tuner) < reach * 1.5f)
            {
                int previous = plugged;
                if (held >= 0) plugged = held;
                else plugged = -1;
                held = held >= 0 ? -1 : previous;
                return;
            }
            held = -1;
        }

        void Jack(Vector2 position, float line, bool plugged, bool hover, Color phosphor)
        {
            var rect = new Rect(position.x - line * 1.5f, position.y - line * 0.5f, line * 3, line);
            center.normal.textColor = hover ? Color.white : plugged ? phosphor : phosphor * 0.55f;
            GUI.Label(rect, plugged ? "(@)" : "( )", center);
        }

        // Phosphor dotted line hanging between the jacks; a free end follows the mouse.
        void Cable(Vector2 from, Vector2 to, float line, Color color)
        {
            from.y += line * 0.45f;
            float sag = line * 0.8f + Vector2.Distance(from, to) * 0.1f;
            float size = line * 0.2f;
            int segments = Mathf.Max(12, Mathf.RoundToInt(Vector2.Distance(from, to) / (size * 1.6f)));
            float crawl = Time.time * 3;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                var point = Vector2.Lerp(from, to, t) + Vector2.up * (sag * 4 * t * (1 - t));
                float pulse = 0.65f + 0.35f * Mathf.Sin((t * segments * 0.5f - crawl) * 1.3f);
                GUI.color = new Color(color.r, color.g, color.b, 0.15f * pulse);
                GUI.DrawTexture(new Rect(point.x - size * 1.4f, point.y - size * 1.4f, size * 2.8f, size * 2.8f), dot);
                GUI.color = new Color(color.r, color.g, color.b, pulse);
                GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), dot);
            }
            GUI.color = Color.white;
        }

        static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void Frame(Rect rect, Color color, float width)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, width), color);
            Fill(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Fill(new Rect(rect.x, rect.y, width, rect.height), color);
            Fill(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        void EnsureStyles(float line)
        {
            int size = Mathf.RoundToInt(line * 0.62f);
            if (text == null || text.fontSize != size)
            {
                text = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, wordWrap = false, clipping = TextClipping.Overflow, alignment = TextAnchor.MiddleLeft };
                if (hud && hud.font) text.font = hud.font;
                center = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
                right = new GUIStyle(text) { alignment = TextAnchor.MiddleRight };
            }
            if (dot) return;
            const int resolution = 32;
            dot = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { name = "Patch cable dot", hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float d = new Vector2(x + 0.5f - resolution / 2f, y + 0.5f - resolution / 2f).magnitude;
                dot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(resolution / 2f - d)));
            }
            dot.Apply();
        }

        void OnDestroy()
        {
            if (dot) Destroy(dot);
        }
    }
}
