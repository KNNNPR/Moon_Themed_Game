using UnityEngine;
using Cursor = UnityEngine.Cursor;

namespace MoonThemedGame.Scavenger
{
    // Receiver patch terminal in the robot feed ([H]), in the same worn phosphor style as the feed.
    // A rival intercept jams the carrier and knocks cables loose; the operator brings all four
    // checklist lines to [OK] (correct chain on a free channel) and locks the carrier.
    [DisallowMultipleComponent]
    public sealed class RfPatchPanel : MonoBehaviour
    {
        enum Out { AntR, AntL, LnaOut, Tune0, Tune1, Tune2, Tune3, MixIf }
        enum In { LnaIn, MixRf, MixLo, DemodIn }

        struct Step
        {
            public bool ok, empty;
            public string status;
        }

        const int Outputs = 8, Inputs = 4;
        static readonly string[] OutputNames = { "ANT-R", "ANT-L", "LNA", "CH1", "CH2", "CH3", "CH4", "IF" };
        static readonly string[] InputNames = { "LNA IN", "MIXER RF", "MIXER LO", "DEMOD IN" };
        static readonly string[] StepNames = { "ANT->LNA IN  ", "LNA->MIXER RF", "CH->MIXER LO ", "IF->DEMOD IN " };

        ScavengerLink link;
        ScavengerFeedHud hud;
        readonly int[] plug = { (int)Out.AntR, (int)Out.LnaOut, (int)Out.Tune0, (int)Out.MixIf };
        int held = -1;
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

        void OnEnable()
        {
            if (Network) Network.RivalAttack += OnAttack;
        }

        void Start()
        {
            if (Network) { Network.RivalAttack -= OnAttack; Network.RivalAttack += OnAttack; }
        }

        void OnDisable()
        {
            if (Network) Network.RivalAttack -= OnAttack;
            Close();
        }

        // The jamming surge pulls one or two cables out of the panel.
        void OnAttack()
        {
            int pulls = Random.Range(1, 3);
            for (int i = 0; i < pulls; i++) plug[Random.Range(0, Inputs)] = -1;
        }

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

        Step[] Evaluate()
        {
            var network = Network;
            var satellite = network.Active;
            var steps = new Step[Inputs];
            int antenna = plug[(int)In.LnaIn];
            if (antenna < 0) steps[0] = new Step { empty = true, status = "EMPTY" };
            else if (antenna != (int)Out.AntR && antenna != (int)Out.AntL) steps[0] = new Step { status = "NOT AN ANTENNA" };
            else
            {
                var polarization = antenna == (int)Out.AntR ? Polarization.RHCP : Polarization.LHCP;
                steps[0] = satellite != null && polarization != satellite.polarization
                    ? new Step { status = $"WRONG POL, USE ANT-{(satellite.polarization == Polarization.RHCP ? "R" : "L")}" }
                    : new Step { ok = true, status = $"{polarization}" };
            }

            int rf = plug[(int)In.MixRf];
            steps[1] = rf < 0 ? new Step { empty = true, status = "EMPTY" }
                : rf != (int)Out.LnaOut ? new Step { status = "NEEDS LNA" } : new Step { ok = true, status = "OK" };

            int channel = plug[(int)In.MixLo] - (int)Out.Tune0;
            if (plug[(int)In.MixLo] < 0) steps[2] = new Step { empty = true, status = "EMPTY" };
            else if (channel < 0 || channel >= network.channels.Length) steps[2] = new Step { status = "NEEDS A CHANNEL" };
            else steps[2] = network.IsJammed(channel)
                ? new Step { status = $"CH{channel + 1} JAMMED, PICK FREE" }
                : new Step { ok = true, status = $"CH{channel + 1} FREE" };

            int demod = plug[(int)In.DemodIn];
            steps[3] = demod < 0 ? new Step { empty = true, status = "EMPTY" }
                : demod != (int)Out.MixIf ? new Step { status = "NEEDS IF" } : new Step { ok = true, status = "OK" };
            return steps;
        }

        void TryLock()
        {
            var network = Network;
            var steps = Evaluate();
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i].ok) continue;
                bool attack = network.Rival == RivalPhase.Incoming;
                network.PenalizeLock();
                Say($"ERR NO LOCK - LINE {i + 1}: {steps[i].status}" + (attack ? $"  +{Mathf.RoundToInt(network.lockPenalty * 100)}% TO RIVAL" : ""), true);
                return;
            }
            int channel = plug[(int)In.MixLo] - (int)Out.Tune0;
            bool repelled = network.Rival != RivalPhase.None;
            network.Repel(channel);
            Say(repelled ? $"CARRIER LOCKED CH{channel + 1} - {network.rivalName} LOST THE SIGNAL" : $"CARRIER STABLE CH{channel + 1}", false);
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

        void OnGUI()
        {
            if (!IsOpen || !Network) return;
            GUI.depth = -10;
            if (hud) hud.EnsureStyles();
            float h = Mathf.Min(Screen.height * 0.84f, Screen.width * 0.52f), w = h * 1.6f;
            float line = h / 24f;
            EnsureStyles(line);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            float pad = w * 0.035f;
            float Row(float r) => panel.y + line * r;

            var outputs = new Vector2[Outputs];
            for (int i = 0; i < Outputs; i++) outputs[i] = new Vector2(panel.x + w * (0.08f + 0.105f * i + (i >= 2 ? 0.02f : 0) + (i >= 3 ? 0.02f : 0) + (i >= 7 ? 0.02f : 0)), Row(13.6f));
            var inputs = new Vector2[Inputs];
            for (int i = 0; i < Inputs; i++) inputs[i] = new Vector2(panel.x + w * (0.13f + 0.245f * i), Row(17.8f));
            float reach = line * 0.9f;
            var lockButton = new Rect(panel.xMax - pad - w * 0.24f, Row(21.4f), w * 0.24f, line * 1.3f);
            HandleInput(outputs, inputs, reach, lockButton);
            if (Event.current.type != EventType.Repaint) return;

            var network = Network;
            var satellite = network.Active;
            var steps = Evaluate();
            bool ready = true;
            foreach (var step in steps) ready &= step.ok;
            var phosphor = Phosphor;
            var dim = phosphor * 0.6f;
            var warning = Warning;
            var mouse = Event.current.mousePosition;

            Fill(panel, new Color(0.004f, 0.012f, 0.008f, 0.96f));
            Frame(panel, dim, 1);
            Frame(new Rect(panel.x + 4, panel.y + 4, panel.width - 8, panel.height - 8), phosphor * 0.25f, 1);

            Print(new Rect(panel.x + pad, Row(0.6f), w, line), "RF PATCH // RCVR-2   SER 0447-B", phosphor, 0, text);
            Print(new Rect(panel.x, Row(0.6f), w - pad, line), "[H] / [ESC] EXIT", dim, 1, right);
            Print(new Rect(panel.x + pad, Row(1.7f), w, line), $"SAT {(satellite != null ? satellite.name : "NONE")}   POL {(satellite != null ? satellite.polarization.ToString() : "--")}   CARRIER CH{network.CarrierChannel + 1}", dim, 2, text);

            bool blink = Mathf.Repeat(Time.time * 1.6f, 1) < 0.7f;
            switch (network.Rival)
            {
                case RivalPhase.Incoming:
                    if (blink) Print(new Rect(panel.x + pad, Row(3f), w, line), $"ALERT  {network.rivalName} JAMMING THE CARRIER, CABLES KNOCKED LOOSE", warning, 3, text);
                    Print(new Rect(panel.x + pad, Row(4f), w, line), $"INTERCEPT {Bar(network.RivalProgress, 24)} {Mathf.RoundToInt(network.RivalProgress * 100),3}%   AT 100% THE SIGNAL IS HIJACKED", warning, 4, text);
                    break;
                case RivalPhase.Hijacked:
                    if (blink) Print(new Rect(panel.x + pad, Row(3f), w, line), $"ALERT  SIGNAL HIJACKED BY {network.rivalName}", warning, 3, text);
                    Print(new Rect(panel.x + pad, Row(4f), w, line), "RE-PATCH ONTO A FREE CHANNEL AND LOCK TO TAKE IT BACK", warning, 4, text);
                    break;
                default:
                    Print(new Rect(panel.x + pad, Row(3f), w, line), network.Hopping ? "RETUNING..." : "NO RIVAL CARRIER - MAINTENANCE MODE", dim, 3, text);
                    break;
            }
            Print(new Rect(panel.x + pad, Row(5.2f), w, line), "> BRING LINES 1-4 TO [OK], THEN LOCK CARRIER", phosphor, 5, text);

            float left = panel.x + pad, half = panel.x + w * 0.5f;
            Print(new Rect(left, Row(6.6f), w, line), "SPECTRUM", phosphor, 6, text);
            DrawSpectrum(left, Row(7.6f), line);
            Print(new Rect(half, Row(6.6f), w, line), "CHECKLIST", phosphor, 7, text);
            for (int i = 0; i < steps.Length; i++)
            {
                var color = steps[i].ok ? phosphor : steps[i].empty ? dim : warning;
                Print(new Rect(half, Row(7.6f + i), w, line), $"{Tag(steps[i])} {i + 1} {StepNames[i]}  {steps[i].status}", color, 20 + i, text);
            }
            Print(new Rect(half, Row(11.6f), w, line), ready ? "[OK] 5 LOCK CARRIER - READY" : "[--] 5 LOCK CARRIER", ready ? phosphor : dim, 24, text);

            Print(new Rect(left, Row(11.4f), w, line), "OUTPUTS", phosphor, 8, text);
            for (int i = 0; i < Outputs; i++)
            {
                bool hover = Vector2.Distance(mouse, outputs[i]) < reach;
                Print(new Rect(outputs[i].x - w * 0.05f, outputs[i].y - line * 1.1f, w * 0.1f, line), OutputName(i), hover ? Color.white : dim, 30 + i, center);
                Jack(outputs[i], line, IsPlugged(i), hover, phosphor);
            }
            Print(new Rect(left, Row(15.6f), w, line), "INPUTS", phosphor, 9, text);
            for (int i = 0; i < Inputs; i++)
            {
                bool hover = held >= 0 && Vector2.Distance(mouse, inputs[i]) < reach * 1.5f;
                var color = steps[i].ok ? phosphor : steps[i].empty ? dim : warning;
                Print(new Rect(inputs[i].x - w * 0.1f, inputs[i].y + line * 0.8f, w * 0.2f, line), $"{Tag(steps[i])} {i + 1} {InputNames[i]}", color, 40 + i, center);
                Jack(inputs[i], line, plug[i] >= 0, hover, phosphor);
            }
            for (int i = 0; i < Inputs; i++)
                if (plug[i] >= 0) Cable(outputs[plug[i]], inputs[i], line, phosphor * 0.75f, i);
            if (held >= 0) Cable(outputs[held], mouse, line, Mathf.Repeat(Time.time * 4, 1) < 0.5f ? phosphor : phosphor * 0.7f, 9);

            bool lockHover = lockButton.Contains(mouse);
            if (ready || lockHover) Fill(lockButton, ready ? phosphor * (lockHover ? 0.85f : 0.6f) : phosphor * 0.15f);
            Frame(lockButton, ready ? phosphor : dim, 1);
            var lockColor = ready ? new Color(0.01f, 0.03f, 0.02f) : dim;
            center.normal.textColor = lockColor;
            GUI.Label(lockButton, "[ LOCK CARRIER ]", center);

            var messageRect = new Rect(left, lockButton.y, w * 0.68f, lockButton.height);
            if (message != null && Time.time < messageUntil)
                Print(messageRect, message, messageAlarm ? warning : phosphor, 50, text);
            else
                Print(messageRect, "> CLICK OUTPUT = TAKE CABLE   CLICK INPUT = PLUG   RMB = DROP", dim, 51, text);

            for (float y = panel.y; y < panel.yMax; y += 3) Fill(new Rect(panel.x, y, panel.width, 1), new Color(0, 0, 0, 0.28f));
            float roll = Mathf.Repeat(Time.time * 0.2f, 1.3f) - 0.15f;
            Fill(new Rect(panel.x, panel.y + panel.height * roll, panel.width, panel.height * 0.06f), new Color(phosphor.r, phosphor.g, phosphor.b, 0.03f));
        }

        static string Tag(Step step) => step.ok ? "[OK]" : step.empty ? "[--]" : "[!!]";

        string OutputName(int output) => output >= (int)Out.Tune0 && output <= (int)Out.Tune3
            ? $"{OutputNames[output]} {Ghz(Network.channels[output - (int)Out.Tune0])}"
            : OutputNames[output];

        bool IsPlugged(int output)
        {
            foreach (int p in plug) if (p == output) return true;
            return held == output;
        }

        void DrawSpectrum(float x, float y, float line)
        {
            var network = Network;
            int channels = network.channels.Length;
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
                string state = jammed ? "JAMMED" : c == network.CarrierChannel ? "CARRIER" : "FREE";
                Print(new Rect(x, y + line * c, Screen.width, line), $"CH{c + 1} {Ghz(network.channels[c])} {Bar(noise[c], 14)} {state}", jammed ? Warning : Phosphor * (state == "FREE" ? 1 : 0.75f), 10 + c, text);
            }
        }

        void HandleInput(Vector2[] outputs, Vector2[] inputs, float reach, Rect lockButton)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            var mouse = e.mousePosition;
            if (e.button == 1) { held = -1; e.Use(); return; }
            if (e.button != 0) return;
            e.Use();
            if (lockButton.Contains(mouse)) { held = -1; TryLock(); return; }
            for (int i = 0; i < Outputs; i++)
                if (Vector2.Distance(mouse, outputs[i]) < reach)
                {
                    for (int j = 0; j < Inputs; j++) if (plug[j] == i) plug[j] = -1;
                    held = i;
                    return;
                }
            for (int i = 0; i < Inputs; i++)
                if (Vector2.Distance(mouse, inputs[i]) < reach * 1.5f)
                {
                    int previous = plug[i];
                    if (held >= 0)
                    {
                        plug[i] = held;
                        held = previous;
                    }
                    else if (previous >= 0)
                    {
                        plug[i] = -1;
                        held = previous;
                    }
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
        void Cable(Vector2 from, Vector2 to, float line, Color color, int seed)
        {
            from.y += line * 0.45f;
            float sag = line * 0.8f + Vector2.Distance(from, to) * 0.1f;
            float size = line * 0.2f;
            int segments = Mathf.Max(12, Mathf.RoundToInt(Vector2.Distance(from, to) / (size * 1.6f)));
            float crawl = Time.time * 3 + seed;
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
            int size = Mathf.RoundToInt(line * 0.68f);
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
