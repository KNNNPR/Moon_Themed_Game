using UnityEngine;
using Cursor = UnityEngine.Cursor;

namespace MoonThemedGame.Scavenger
{
    // Receiver patch panel in the robot feed ([H]). A rival intercept jams the carrier and knocks cables loose;
    // the operator re-patches the RF chain onto a free channel and locks the carrier.
    [DisallowMultipleComponent]
    public sealed class RfPatchPanel : MonoBehaviour
    {
        enum Out { AntR, AntL, LnaOut, Lo0, Lo1, Lo2, Lo3, MixIf }
        enum In { LnaIn, MixRf, MixLo, DemodIn }

        const int Outputs = 8, Inputs = 4;
        static readonly string[] InputNames = { "LNA IN", "MIXER RF", "MIXER LO", "DEMOD IN" };
        static readonly Color[] CableColors =
        {
            new Color(0.95f, 0.3f, 0.25f), new Color(0.3f, 0.6f, 1f), new Color(0.95f, 0.8f, 0.25f),
            new Color(0.4f, 0.95f, 0.5f), new Color(0.4f, 0.95f, 0.5f), new Color(0.4f, 0.95f, 0.5f), new Color(0.4f, 0.95f, 0.5f),
            new Color(0.85f, 0.5f, 1f)
        };

        ScavengerLink link;
        ScavengerFeedHud hud;
        readonly int[] plug = { (int)Out.AntR, (int)Out.LnaOut, (int)Out.Lo0, (int)Out.MixIf };
        int held = -1;
        string message;
        Color messageColor;
        float messageUntil, closeAt = -1, nextNoise;
        float[] noise = new float[0];
        CursorLockMode previousLock;
        bool previousVisible;
        Texture2D circle;
        GUIStyle label, small, title;

        public bool IsOpen { get; private set; }

        SatelliteNetwork Network => link ? link.Network : null;

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

        float LoFrequency(int lo) => Network.channels[lo] - Network.intermediateFrequency;

        static string Ghz(float value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        string OutputName(int output) => output switch
        {
            (int)Out.AntR => "ANT RHCP",
            (int)Out.AntL => "ANT LHCP",
            (int)Out.LnaOut => "LNA OUT",
            (int)Out.MixIf => "MIXER IF",
            _ => "LO " + Ghz(LoFrequency(output - (int)Out.Lo0))
        };

        void TryLock()
        {
            var network = Network;
            var satellite = network.Active;
            int lo = plug[(int)In.MixLo] - (int)Out.Lo0;
            int antenna = plug[(int)In.LnaIn];
            string fault = null;
            if (plug[(int)In.DemodIn] != (int)Out.MixIf) fault = "DEMOD: NO IF SIGNAL - FEED IT FROM MIXER IF";
            else if (plug[(int)In.MixRf] != (int)Out.LnaOut) fault = "MIXER RF: NO AMPLIFIED SIGNAL - FEED IT FROM LNA OUT";
            else if (antenna != (int)Out.AntR && antenna != (int)Out.AntL) fault = "LNA: NO ANTENNA FEED";
            else if (satellite != null && (antenna == (int)Out.AntR ? Polarization.RHCP : Polarization.LHCP) != satellite.polarization)
                fault = $"POLARIZATION MISMATCH - {satellite.name} TRANSMITS {satellite.polarization}";
            else if (lo < 0 || lo >= network.channels.Length) fault = "MIXER LO: NO LOCAL OSCILLATOR";
            else if (network.IsJammed(lo)) fault = $"CH{lo + 1} {Ghz(network.channels[lo])} GHz IS JAMMED - NO LOCK";

            if (fault != null)
            {
                bool attack = network.Rival == RivalPhase.Incoming;
                network.PenalizeLock();
                Say("LOCK FAILED: " + fault + (attack ? $"   (+{Mathf.RoundToInt(network.lockPenalty * 100)}% INTERCEPT)" : ""), hud ? hud.warning : Color.red);
                return;
            }
            bool repelled = network.Rival != RivalPhase.None;
            network.Repel(lo);
            Say(repelled ? $"CARRIER LOCKED ON CH{lo + 1} - {network.rivalName} LOST THE SIGNAL" : $"CARRIER STABLE ON CH{lo + 1}", hud ? hud.phosphor : Color.green);
            if (repelled) closeAt = Time.time + 1.4f;
        }

        void Say(string text, Color color)
        {
            message = text;
            messageColor = color;
            messageUntil = Time.time + 4;
        }

        void OnGUI()
        {
            if (!IsOpen || !Network) return;
            GUI.depth = -10;
            EnsureStyles();
            float h = Mathf.Min(Screen.height * 0.82f, Screen.width * 0.5f), w = h * 1.6f;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            var phosphor = hud ? hud.phosphor : new Color(0.78f, 0.96f, 0.8f);
            var warning = hud ? hud.warning : Color.red;

            var outputs = new Vector2[Outputs];
            for (int i = 0; i < Outputs; i++) outputs[i] = new Vector2(panel.x + w * (0.07f + 0.11f * i + (i >= 3 ? 0.02f : 0) + (i >= 7 ? 0.02f : 0)), panel.y + h * 0.64f);
            var inputs = new Vector2[Inputs];
            for (int i = 0; i < Inputs; i++) inputs[i] = new Vector2(panel.x + w * (0.12f + 0.19f * i), panel.y + h * 0.83f);
            float jack = h * 0.04f;
            var lockButton = new Rect(panel.xMax - w * 0.2f, panel.yMax - h * 0.085f, w * 0.17f, h * 0.06f);
            HandleInput(outputs, inputs, jack, lockButton);
            if (Event.current.type != EventType.Repaint) return;

            Fill(panel, new Color(0.02f, 0.03f, 0.03f, 0.94f));
            Frame(panel, phosphor * 0.6f, 2);
            var network = Network;
            var satellite = network.Active;
            Text(new Rect(panel.x + w * 0.03f, panel.y + h * 0.02f, w, h * 0.06f), "RF PATCH PANEL // RECEIVER CHAIN", phosphor, title);
            Text(new Rect(panel.x, panel.y + h * 0.025f, w * 0.97f, h * 0.06f), "[H] / [ESC] CLOSE", phosphor * 0.7f, Right(small));

            DrawSpectrum(new Rect(panel.x + w * 0.03f, panel.y + h * 0.1f, w * 0.44f, h * 0.36f), phosphor, warning);

            float x = panel.x + w * 0.51f, y = panel.y + h * 0.1f, line = h * 0.042f;
            Text(new Rect(x, y, w, line), $"SAT {(satellite != null ? satellite.name : "NONE")}   POL {(satellite != null ? satellite.polarization.ToString() : "--")}", phosphor, label);
            Text(new Rect(x, y + line, w, line), $"CARRIER CH{network.CarrierChannel + 1}   IF {Ghz(network.intermediateFrequency)} GHz", phosphor, label);
            if (network.Rival != RivalPhase.None)
            {
                float p = network.RivalProgress;
                Text(new Rect(x, y + line * 2.2f, w, line), network.Rival == RivalPhase.Hijacked ? $"{network.rivalName}: SIGNAL HIJACKED" : $"{network.rivalName} INTERCEPT {Mathf.RoundToInt(p * 100)}%", warning, label);
                var bar = new Rect(x, y + line * 3.3f, w * 0.44f, line * 0.45f);
                Fill(bar, new Color(1, 1, 1, 0.08f));
                Fill(new Rect(bar.x, bar.y, bar.width * p, bar.height), warning);
            }
            else Text(new Rect(x, y + line * 2.2f, w, line), "NO RIVAL CARRIER", phosphor * 0.7f, label);
            string[] manual =
            {
                "MANUAL",
                "1  ANTENNA -> LNA IN   (match satellite POL)",
                "2  LNA OUT -> MIXER RF",
                "3  LO -> MIXER LO      LO = CHANNEL - IF",
                "4  MIXER IF -> DEMOD IN",
                "TUNE TO A CHANNEL THAT IS NOT JAMMED, THEN LOCK"
            };
            for (int i = 0; i < manual.Length; i++)
                Text(new Rect(x, y + line * (4f + i * 0.8f), w, line), manual[i], phosphor * (i == 0 ? 0.85f : 0.65f), small);

            Group(outputs[0], outputs[1], jack, "ANTENNA", phosphor);
            Group(outputs[2], outputs[2], jack, "AMP", phosphor);
            Group(outputs[3], outputs[6], jack, "LO SYNTH", phosphor);
            Group(outputs[7], outputs[7], jack, "MIXER", phosphor);
            for (int i = 0; i < Outputs; i++)
            {
                Text(new Rect(outputs[i].x - w * 0.06f, outputs[i].y - jack * 1.9f, w * 0.12f, line), OutputName(i), phosphor, Center(small));
                Jack(outputs[i], jack, phosphor);
            }
            for (int i = 0; i < Inputs; i++)
            {
                Text(new Rect(inputs[i].x - w * 0.08f, inputs[i].y + jack * 0.9f, w * 0.16f, line), InputNames[i], phosphor, Center(small));
                Jack(inputs[i], jack, phosphor);
            }
            for (int i = 0; i < Inputs; i++)
                if (plug[i] >= 0) Cable(outputs[plug[i]], inputs[i], CableColors[plug[i]], jack);
            if (held >= 0) Cable(outputs[held], Event.current.mousePosition, CableColors[held], jack);

            bool hover = lockButton.Contains(Event.current.mousePosition);
            Fill(lockButton, hover ? phosphor * 0.35f : new Color(1, 1, 1, 0.06f));
            Frame(lockButton, phosphor, 1);
            Text(lockButton, "LOCK CARRIER", phosphor, Center(label));
            if (message != null && Time.time < messageUntil)
                Text(new Rect(panel.x + w * 0.03f, lockButton.y, w * 0.76f, lockButton.height), message, messageColor, small);
        }

        void HandleInput(Vector2[] outputs, Vector2[] inputs, float jack, Rect lockButton)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            var mouse = e.mousePosition;
            if (e.button == 1) { held = -1; e.Use(); return; }
            if (e.button != 0) return;
            e.Use();
            if (lockButton.Contains(mouse)) { held = -1; TryLock(); return; }
            for (int i = 0; i < Outputs; i++)
                if (Vector2.Distance(mouse, outputs[i]) < jack)
                {
                    for (int j = 0; j < Inputs; j++) if (plug[j] == i) plug[j] = -1;
                    held = i;
                    return;
                }
            for (int i = 0; i < Inputs; i++)
                if (Vector2.Distance(mouse, inputs[i]) < jack)
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

        void DrawSpectrum(Rect box, Color phosphor, Color warning)
        {
            var network = Network;
            int channels = network.channels.Length, bins = 9;
            if (noise.Length != channels * bins) noise = new float[channels * bins];
            if (Time.time >= nextNoise)
            {
                nextNoise = Time.time + 0.08f;
                for (int c = 0; c < channels; c++)
                for (int b = 0; b < bins; b++)
                {
                    float value = Random.Range(0.04f, 0.14f);
                    if (network.IsJammed(c)) value = Random.Range(0.5f, 0.98f);
                    else if (c == network.CarrierChannel && Mathf.Abs(b - bins / 2) <= 1) value = b == bins / 2 ? 0.8f : 0.45f;
                    noise[c * bins + b] = Mathf.Lerp(noise[c * bins + b], value, 0.6f);
                }
            }
            Fill(box, new Color(0, 0, 0, 0.5f));
            Frame(box, phosphor * 0.4f, 1);
            Text(new Rect(box.x + 6, box.y + 2, box.width, box.height * 0.12f), "SPECTRUM", phosphor * 0.7f, small);
            var plot = new Rect(box.x + box.width * 0.04f, box.y + box.height * 0.15f, box.width * 0.92f, box.height * 0.62f);
            float column = plot.width / channels, bin = column * 0.8f / bins;
            for (int c = 0; c < channels; c++)
            {
                bool jammed = network.IsJammed(c);
                var tint = jammed ? warning : phosphor;
                for (int b = 0; b < bins; b++)
                {
                    float value = noise[c * bins + b];
                    float bx = plot.x + column * c + column * 0.1f + bin * b;
                    Fill(new Rect(bx, plot.yMax - plot.height * value, bin * 0.7f, plot.height * value), tint * (jammed ? 0.9f : 0.75f));
                }
                var labelRect = new Rect(plot.x + column * c, plot.yMax + 2, column, box.height * 0.1f);
                Text(labelRect, $"CH{c + 1} {Ghz(network.channels[c])}", tint, Center(small));
                string state = jammed ? "JAM" : c == network.CarrierChannel ? "CARRIER" : "FREE";
                Text(new Rect(labelRect.x, labelRect.yMax, column, labelRect.height), state, tint * (jammed ? 1 : 0.7f), Center(small));
            }
        }

        void Group(Vector2 from, Vector2 to, float jack, string name, Color color)
        {
            var rect = Rect.MinMaxRect(from.x - jack * 1.9f, from.y - jack * 3.2f, to.x + jack * 1.9f, from.y + jack * 1.5f);
            Frame(rect, color * 0.35f, 1);
            Text(new Rect(rect.x + 4, rect.y + 1, rect.width, jack), name, color * 0.55f, small);
        }

        void Jack(Vector2 center, float radius, Color color)
        {
            GUI.color = color * 0.55f;
            GUI.DrawTexture(new Rect(center.x - radius * 0.8f, center.y - radius * 0.8f, radius * 1.6f, radius * 1.6f), circle);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(center.x - radius * 0.5f, center.y - radius * 0.5f, radius, radius), circle);
            GUI.color = Color.white;
        }

        // A cable hangs between its plugs; a free end follows the mouse.
        void Cable(Vector2 from, Vector2 to, Color color, float jack)
        {
            float sag = jack * 1.2f + Vector2.Distance(from, to) * 0.12f;
            float thickness = jack * 0.32f;
            const int segments = 48;
            GUI.color = color;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                var point = Vector2.Lerp(from, to, t) + Vector2.up * (sag * 4 * t * (1 - t));
                GUI.DrawTexture(new Rect(point.x - thickness * 0.5f, point.y - thickness * 0.5f, thickness, thickness), circle);
            }
            GUI.DrawTexture(new Rect(from.x - jack * 0.45f, from.y - jack * 0.45f, jack * 0.9f, jack * 0.9f), circle);
            GUI.DrawTexture(new Rect(to.x - jack * 0.45f, to.y - jack * 0.45f, jack * 0.9f, jack * 0.9f), circle);
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

        static void Text(Rect rect, string value, Color color, GUIStyle style)
        {
            color.a = 1;
            style.normal.textColor = color;
            GUI.Label(rect, value, style);
        }

        static GUIStyle Center(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };
        static GUIStyle Right(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.UpperRight };

        void EnsureStyles()
        {
            int size = Mathf.RoundToInt(Mathf.Min(Screen.height * 0.82f, Screen.width * 0.5f) * 0.03f);
            if (label != null && label.fontSize == size) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = false, clipping = TextClipping.Overflow, alignment = TextAnchor.MiddleLeft };
            if (hud && hud.font) label.font = hud.font;
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(size * 0.78f) };
            title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(size * 1.3f), fontStyle = FontStyle.Bold };
            if (circle) return;
            const int resolution = 64;
            circle = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { name = "Patch jack", hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float d = new Vector2(x + 0.5f - resolution / 2f, y + 0.5f - resolution / 2f).magnitude;
                circle.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(resolution / 2f - d)));
            }
            circle.Apply();
        }

        void OnDestroy()
        {
            if (circle) Destroy(circle);
        }
    }
}
