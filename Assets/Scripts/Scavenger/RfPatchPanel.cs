using UnityEngine;
using Cursor = UnityEngine.Cursor;

namespace MoonThemedGame.Scavenger
{
    // Worn receiver patch panel in the robot feed ([H]). A rival intercept jams the carrier and knocks cables loose;
    // the operator lights all four checklist lamps (correct chain on a free channel) and locks the carrier.
    [DisallowMultipleComponent]
    public sealed class RfPatchPanel : MonoBehaviour
    {
        enum Out { AntR, AntL, LnaOut, Lo0, Lo1, Lo2, Lo3, MixIf }
        enum In { LnaIn, MixRf, MixLo, DemodIn }

        struct Step
        {
            public bool ok, empty;
            public string status;
        }

        const int Outputs = 8, Inputs = 4;
        static readonly string[] InputNames = { "LNA IN", "MIXER RF", "MIXER LO", "DEMOD IN" };
        static readonly string[] StepNames = { "ANTENNA  ->  LNA IN", "LNA OUT  ->  MIXER RF", "LO  ->  MIXER LO", "MIXER IF  ->  DEMOD IN" };
        static readonly Color[] CableColors =
        {
            new Color(0.78f, 0.25f, 0.2f), new Color(0.25f, 0.45f, 0.75f), new Color(0.8f, 0.66f, 0.22f),
            new Color(0.35f, 0.7f, 0.4f), new Color(0.35f, 0.7f, 0.4f), new Color(0.35f, 0.7f, 0.4f), new Color(0.35f, 0.7f, 0.4f),
            new Color(0.62f, 0.4f, 0.75f)
        };
        static readonly Color Metal = new Color(0.12f, 0.13f, 0.11f);
        static readonly Color Paint = new Color(0.78f, 0.76f, 0.66f);
        static readonly Color LampGreen = new Color(0.45f, 1f, 0.45f);
        static readonly Color LampRed = new Color(1f, 0.3f, 0.2f);
        static readonly Color Tape = new Color(0.86f, 0.8f, 0.58f);
        static readonly Color Pencil = new Color(0.16f, 0.15f, 0.2f);

        ScavengerLink link;
        ScavengerFeedHud hud;
        readonly int[] plug = { (int)Out.AntR, (int)Out.LnaOut, (int)Out.Lo0, (int)Out.MixIf };
        int held = -1;
        string message;
        Color messageColor;
        float messageUntil, closeAt = -1, nextNoise, nextJitter;
        float[] noise = new float[0];
        Vector2 jitter;
        CursorLockMode previousLock;
        bool previousVisible;
        Texture2D circle, grime;
        GUIStyle label, small, title, hand;

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
                    ? new Step { status = $"WRONG POL - {satellite.name} IS {satellite.polarization}" }
                    : new Step { ok = true, status = $"OK  {polarization}" };
            }

            int rf = plug[(int)In.MixRf];
            steps[1] = rf < 0 ? new Step { empty = true, status = "EMPTY" }
                : rf != (int)Out.LnaOut ? new Step { status = "NEEDS LNA OUT" } : new Step { ok = true, status = "OK" };

            int lo = plug[(int)In.MixLo] - (int)Out.Lo0;
            if (plug[(int)In.MixLo] < 0) steps[2] = new Step { empty = true, status = "EMPTY" };
            else if (lo < 0 || lo >= network.channels.Length) steps[2] = new Step { status = "NEEDS AN LO" };
            else
            {
                string tuned = $"TUNED CH{lo + 1} {Ghz(network.channels[lo])}";
                steps[2] = network.IsJammed(lo) ? new Step { status = tuned + " - JAMMED" } : new Step { ok = true, status = tuned + " - FREE" };
            }

            int demod = plug[(int)In.DemodIn];
            steps[3] = demod < 0 ? new Step { empty = true, status = "EMPTY" }
                : demod != (int)Out.MixIf ? new Step { status = "NEEDS MIXER IF" } : new Step { ok = true, status = "OK" };
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
                Say($"NO LOCK - STEP {i + 1}: {steps[i].status}" + (attack ? $"   +{Mathf.RoundToInt(network.lockPenalty * 100)}% TO RIVAL" : ""), LampRed);
                return;
            }
            int lo = plug[(int)In.MixLo] - (int)Out.Lo0;
            bool repelled = network.Rival != RivalPhase.None;
            network.Repel(lo);
            Say(repelled ? $"CARRIER LOCKED ON CH{lo + 1} - {network.rivalName} LOST THE SIGNAL" : $"CARRIER STABLE ON CH{lo + 1}", LampGreen);
            if (repelled) closeAt = Time.time + 1.4f;
        }

        void Say(string text, Color color)
        {
            message = text;
            messageColor = color;
            messageUntil = Time.time + 4;
        }

        string Situation(out bool alarm)
        {
            var network = Network;
            alarm = network.Rival != RivalPhase.None;
            return network.Rival switch
            {
                RivalPhase.Incoming => $"{network.rivalName} IS JAMMING YOUR CHANNEL AND KNOCKED CABLES OUT.  LIGHT ALL 4 LAMPS, THEN PRESS LOCK.",
                RivalPhase.Hijacked => $"{network.rivalName} HAS HIJACKED THE SIGNAL.  LIGHT ALL 4 LAMPS ON A FREE CHANNEL, THEN PRESS LOCK.",
                _ => network.Hopping ? "RECEIVER RETUNING..." : "NO RIVAL CARRIER.  PANEL OPEN FOR MAINTENANCE."
            };
        }

        void OnGUI()
        {
            if (!IsOpen || !Network) return;
            GUI.depth = -10;
            EnsureStyles();
            var network = Network;
            float h = Mathf.Min(Screen.height * 0.84f, Screen.width * 0.5f), w = h * 1.6f;
            if (Time.time >= nextJitter)
            {
                nextJitter = Time.time + 0.07f;
                float shake = network.Rival == RivalPhase.Incoming ? network.RivalProgress * 3 : network.Rival == RivalPhase.Hijacked ? 2 : 0;
                jitter = Random.value < 0.35f ? new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake) * 0.4f) : Vector2.zero;
            }
            var panel = new Rect((Screen.width - w) * 0.5f + jitter.x, (Screen.height - h) * 0.5f + jitter.y, w, h);

            var outputs = new Vector2[Outputs];
            for (int i = 0; i < Outputs; i++) outputs[i] = new Vector2(panel.x + w * (0.07f + 0.11f * i + (i >= 3 ? 0.02f : 0) + (i >= 7 ? 0.02f : 0)), panel.y + h * 0.66f);
            var inputs = new Vector2[Inputs];
            for (int i = 0; i < Inputs; i++) inputs[i] = new Vector2(panel.x + w * (0.12f + 0.19f * i), panel.y + h * 0.84f);
            float jack = h * 0.038f;
            var lockButton = new Rect(panel.xMax - w * 0.2f, panel.yMax - h * 0.09f, w * 0.17f, h * 0.06f);
            HandleInput(outputs, inputs, jack, lockButton);
            if (Event.current.type != EventType.Repaint) return;

            var steps = Evaluate();
            bool ready = true;
            foreach (var step in steps) ready &= step.ok;
            DrawHousing(panel);

            Text(new Rect(panel.x + w * 0.035f, panel.y + h * 0.025f, w, h * 0.06f), "RF PATCH PANEL  MK-II", Paint, title);
            Text(new Rect(panel.x + w * 0.4f, panel.y + h * 0.04f, w, h * 0.04f), "SER 0447-B   RCVR CHAIN 2.1-2.7 GHz", Paint * 0.6f, small);
            Text(new Rect(panel.x, panel.y + h * 0.035f, w * 0.965f, h * 0.05f), "[H] / [ESC] CLOSE", Paint * 0.6f, Right(small));

            string situation = Situation(out bool alarm);
            var strip = new Rect(panel.x + w * 0.03f, panel.y + h * 0.1f, w * 0.94f, h * 0.055f);
            bool blink = Mathf.Repeat(Time.time * 1.6f, 1) < 0.75f;
            Fill(strip, alarm ? new Color(0.35f, 0.05f, 0.03f, blink ? 0.9f : 0.6f) : new Color(0, 0, 0, 0.35f));
            Text(new Rect(strip.x + w * 0.01f, strip.y, strip.width, strip.height), (alarm ? "!  " : "") + situation, alarm ? new Color(1, 0.75f, 0.65f) : Paint * 0.75f, small);
            if (network.Rival != RivalPhase.None)
            {
                float progress = network.Rival == RivalPhase.Hijacked ? 1 : network.RivalProgress;
                var bar = new Rect(strip.x, strip.yMax + h * 0.006f, strip.width, h * 0.012f);
                Fill(bar, new Color(0, 0, 0, 0.5f));
                Fill(new Rect(bar.x, bar.y, bar.width * progress, bar.height), LampRed * 0.85f);
                Text(new Rect(bar.x, bar.yMax, bar.width, h * 0.035f), network.Rival == RivalPhase.Hijacked ? "SIGNAL HIJACKED" : $"INTERCEPT {Mathf.RoundToInt(progress * 100)}%  - AT 100% THE SIGNAL IS HIJACKED", LampRed, Right(small));
            }

            DrawSpectrum(new Rect(panel.x + w * 0.03f, panel.y + h * 0.215f, w * 0.43f, h * 0.31f));
            DrawChecklist(new Rect(panel.x + w * 0.49f, panel.y + h * 0.215f, w * 0.48f, h * 0.31f), steps, ready);

            Group(outputs[0], outputs[1], jack, "ANTENNA");
            Group(outputs[2], outputs[2], jack, "AMP");
            Group(outputs[3], outputs[6], jack, "LO SYNTH");
            Group(outputs[7], outputs[7], jack, "MIXER");
            float line = h * 0.04f;
            var mouse = Event.current.mousePosition;
            for (int i = 0; i < Outputs; i++)
            {
                Text(new Rect(outputs[i].x - w * 0.06f, outputs[i].y - jack * 2f, w * 0.12f, line), OutputName(i), Paint, Center(small));
                Jack(outputs[i], jack, Vector2.Distance(mouse, outputs[i]) < jack);
            }
            for (int i = 0; i < Inputs; i++)
            {
                var stepColor = steps[i].ok ? LampGreen : steps[i].empty ? Paint * 0.5f : LampRed;
                Text(new Rect(inputs[i].x - w * 0.08f, inputs[i].y + jack * 1f, w * 0.16f, line), $"{i + 1}  {InputNames[i]}", Paint, Center(small));
                Jack(inputs[i], jack, held >= 0 && Vector2.Distance(mouse, inputs[i]) < jack * 1.6f);
                Lamp(inputs[i] + new Vector2(0, -jack * 1.7f), jack * 0.32f, stepColor, !steps[i].empty);
            }
            for (int i = 0; i < Inputs; i++)
                if (plug[i] >= 0) Cable(outputs[plug[i]], inputs[i], CableColors[plug[i]], jack);
            if (held >= 0) Cable(outputs[held], mouse, CableColors[held], jack);

            DrawTape(new Rect(panel.x + w * 0.78f, panel.y + h * 0.72f, w * 0.19f, h * 0.075f), -4, "LO = CH - 0.45 !!");
            DrawTape(new Rect(panel.x + w * 0.78f, panel.y + h * 0.81f, w * 0.19f, h * 0.06f), 2, "ARGUS/VESTA = R   HERMES = L");

            bool hover = lockButton.Contains(mouse);
            Fill(lockButton, ready ? new Color(0.1f, 0.3f, 0.1f, hover ? 1 : 0.8f) : new Color(0.05f, 0.05f, 0.05f, hover ? 0.9f : 0.7f));
            Frame(lockButton, ready ? LampGreen * 0.8f : Paint * 0.4f, 2);
            Text(lockButton, "LOCK CARRIER", ready ? LampGreen : Paint * 0.6f, Center(label));
            if (message != null && Time.time < messageUntil)
                Text(new Rect(panel.x + w * 0.035f, lockButton.y, w * 0.74f, lockButton.height), message, messageColor, small);
            else
                Text(new Rect(panel.x + w * 0.035f, lockButton.y, w * 0.74f, lockButton.height), "CLICK AN OUTPUT TO TAKE A CABLE, CLICK AN INPUT TO PLUG IT.  RIGHT CLICK - DROP.", Paint * 0.55f, small);
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
                if (Vector2.Distance(mouse, inputs[i]) < jack * 1.6f)
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

        // Scuffed olive metal with grime, bevels and corner screws.
        void DrawHousing(Rect panel)
        {
            Fill(new Rect(panel.x + 6, panel.y + 8, panel.width, panel.height), new Color(0, 0, 0, 0.5f));
            Fill(panel, Metal);
            GUI.color = new Color(1, 1, 1, 0.9f);
            GUI.DrawTexture(panel, grime);
            GUI.color = Color.white;
            Fill(new Rect(panel.x, panel.y, panel.width, 2), new Color(1, 1, 1, 0.12f));
            Fill(new Rect(panel.x, panel.y, 2, panel.height), new Color(1, 1, 1, 0.08f));
            Fill(new Rect(panel.x, panel.yMax - 3, panel.width, 3), new Color(0, 0, 0, 0.6f));
            Fill(new Rect(panel.xMax - 3, panel.y, 3, panel.height), new Color(0, 0, 0, 0.6f));
            float s = panel.height * 0.022f;
            foreach (var corner in new[] { new Vector2(panel.x, panel.y), new Vector2(panel.xMax, panel.y), new Vector2(panel.x, panel.yMax), new Vector2(panel.xMax, panel.yMax) })
            {
                var c = corner + new Vector2(corner.x < panel.center.x ? s * 1.6f : -s * 1.6f, corner.y < panel.center.y ? s * 1.6f : -s * 1.6f);
                GUI.color = new Color(0.32f, 0.32f, 0.3f);
                GUI.DrawTexture(new Rect(c.x - s, c.y - s, s * 2, s * 2), circle);
                GUI.color = Color.white;
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(corner.x * 7 + corner.y * 3, c);
                Fill(new Rect(c.x - s * 0.8f, c.y - s * 0.12f, s * 1.6f, s * 0.24f), new Color(0, 0, 0, 0.7f));
                GUI.matrix = matrix;
            }
        }

        void DrawChecklist(Rect box, Step[] steps, bool ready)
        {
            Fill(box, new Color(0, 0, 0, 0.3f));
            Frame(box, Paint * 0.3f, 1);
            float row = box.height / 6.2f;
            Text(new Rect(box.x + row * 0.3f, box.y + row * 0.15f, box.width, row), "CHECKLIST - EVERY LAMP MUST BE GREEN", Paint * 0.85f, small);
            for (int i = 0; i < steps.Length; i++)
            {
                float y = box.y + row * (1.2f + i);
                var color = steps[i].ok ? LampGreen : steps[i].empty ? Paint * 0.5f : LampRed;
                Lamp(new Vector2(box.x + row * 0.6f, y + row * 0.5f), row * 0.22f, color, !steps[i].empty);
                Text(new Rect(box.x + row * 1.1f, y, box.width, row), $"{i + 1}  {StepNames[i]}", Paint, small);
                Text(new Rect(box.x, y, box.width - row * 0.3f, row), steps[i].status, color, Right(small));
            }
            float last = box.y + row * 5.2f;
            Lamp(new Vector2(box.x + row * 0.6f, last + row * 0.5f), row * 0.22f, ready ? LampGreen : Paint * 0.5f, ready);
            Text(new Rect(box.x + row * 1.1f, last, box.width, row), ready ? "5  PRESS LOCK CARRIER" : "5  LOCK CARRIER (WHEN 1-4 ARE GREEN)", ready ? LampGreen : Paint * 0.6f, small);
        }

        // Green CRT with scanlines and a slow rolling bar.
        void DrawSpectrum(Rect box)
        {
            var network = Network;
            var green = new Color(0.55f, 0.95f, 0.55f);
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
            Fill(new Rect(box.x - 4, box.y - 4, box.width + 8, box.height + 8), new Color(0.03f, 0.03f, 0.03f));
            Fill(box, new Color(0.01f, 0.06f, 0.025f));
            Text(new Rect(box.x + 6, box.y + 2, box.width, box.height * 0.12f), "SPECTRUM  - RED = JAMMED, PICK A FREE CHANNEL", green * 0.75f, small);
            var plot = new Rect(box.x + box.width * 0.04f, box.y + box.height * 0.16f, box.width * 0.92f, box.height * 0.56f);
            float column = plot.width / channels, bin = column * 0.8f / bins;
            for (int c = 0; c < channels; c++)
            {
                bool jammed = network.IsJammed(c);
                var tint = jammed ? LampRed : green;
                for (int b = 0; b < bins; b++)
                {
                    float value = noise[c * bins + b];
                    float bx = plot.x + column * c + column * 0.1f + bin * b;
                    Fill(new Rect(bx, plot.yMax - plot.height * value, bin * 0.7f, plot.height * value), tint * (jammed ? 0.85f : 0.7f));
                }
                var labelRect = new Rect(plot.x + column * c, plot.yMax + 2, column, box.height * 0.11f);
                Text(labelRect, $"CH{c + 1} {Ghz(network.channels[c])}", tint, Center(small));
                string state = jammed ? "JAMMED" : c == network.CarrierChannel ? "CARRIER" : "FREE";
                Text(new Rect(labelRect.x, labelRect.yMax, column, labelRect.height), state, tint * (jammed ? 1 : 0.75f), Center(small));
            }
            for (float y = box.y; y < box.yMax; y += 3) Fill(new Rect(box.x, y, box.width, 1), new Color(0, 0, 0, 0.35f));
            float roll = Mathf.Repeat(Time.time * 0.25f, 1.3f) - 0.15f;
            Fill(new Rect(box.x, box.y + box.height * roll, box.width, box.height * 0.08f), new Color(0.6f, 1, 0.6f, 0.04f));
            Frame(box, new Color(0, 0, 0, 0.8f), 3);
        }

        void DrawTape(Rect rect, float angle, string note)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, rect.center);
            Fill(rect, new Color(Tape.r, Tape.g, Tape.b, 0.88f));
            Fill(new Rect(rect.x, rect.y, rect.width, rect.height * 0.15f), new Color(1, 1, 1, 0.12f));
            Text(rect, note, Pencil, hand);
            GUI.matrix = matrix;
        }

        void Group(Vector2 from, Vector2 to, float jack, string name)
        {
            var rect = Rect.MinMaxRect(from.x - jack * 2f, from.y - jack * 3.4f, to.x + jack * 2f, from.y + jack * 1.6f);
            Fill(rect, new Color(0, 0, 0, 0.18f));
            Frame(rect, Paint * 0.3f, 1);
            Text(new Rect(rect.x + 4, rect.y + 1, rect.width, jack), name, Paint * 0.55f, small);
        }

        void Jack(Vector2 center, float radius, bool highlight)
        {
            GUI.color = highlight ? new Color(0.85f, 0.8f, 0.55f) : new Color(0.42f, 0.4f, 0.34f);
            GUI.DrawTexture(new Rect(center.x - radius * 0.85f, center.y - radius * 0.85f, radius * 1.7f, radius * 1.7f), circle);
            GUI.color = new Color(0.2f, 0.19f, 0.16f);
            GUI.DrawTexture(new Rect(center.x - radius * 0.62f, center.y - radius * 0.62f, radius * 1.24f, radius * 1.24f), circle);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(center.x - radius * 0.38f, center.y - radius * 0.38f, radius * 0.76f, radius * 0.76f), circle);
            GUI.color = Color.white;
        }

        void Lamp(Vector2 center, float radius, Color color, bool lit)
        {
            if (lit)
            {
                float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 9, center.x * 0.1f);
                GUI.color = new Color(color.r, color.g, color.b, 0.22f * flicker);
                GUI.DrawTexture(new Rect(center.x - radius * 2.4f, center.y - radius * 2.4f, radius * 4.8f, radius * 4.8f), circle);
                GUI.color = new Color(color.r, color.g, color.b, flicker);
            }
            else GUI.color = new Color(0.22f, 0.21f, 0.18f);
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), circle);
            GUI.color = new Color(1, 1, 1, lit ? 0.5f : 0.12f);
            GUI.DrawTexture(new Rect(center.x - radius * 0.45f, center.y - radius * 0.6f, radius * 0.5f, radius * 0.5f), circle);
            GUI.color = Color.white;
        }

        // A cable hangs between its plugs; a free end follows the mouse.
        void Cable(Vector2 from, Vector2 to, Color color, float jack)
        {
            float sag = jack * 1.2f + Vector2.Distance(from, to) * 0.12f;
            float thickness = jack * 0.34f;
            const int segments = 48;
            for (int pass = 0; pass < 2; pass++)
            {
                GUI.color = pass == 0 ? new Color(0, 0, 0, 0.6f) : color;
                float size = pass == 0 ? thickness * 1.45f : thickness;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    var point = Vector2.Lerp(from, to, t) + Vector2.up * (sag * 4 * t * (1 - t)) + (pass == 0 ? new Vector2(1, 2) : Vector2.zero);
                    GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), circle);
                }
            }
            GUI.color = color * 0.8f;
            GUI.DrawTexture(new Rect(from.x - jack * 0.5f, from.y - jack * 0.5f, jack, jack), circle);
            GUI.DrawTexture(new Rect(to.x - jack * 0.5f, to.y - jack * 0.5f, jack, jack), circle);
            GUI.color = new Color(0.75f, 0.72f, 0.62f);
            GUI.DrawTexture(new Rect(to.x - jack * 0.22f, to.y - jack * 0.22f, jack * 0.44f, jack * 0.44f), circle);
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

        // Worn print: a faint offset ghost and a slow flicker.
        static void Text(Rect rect, string value, Color color, GUIStyle style)
        {
            float flicker = 0.82f + 0.18f * Mathf.PerlinNoise(Time.time * 3, rect.y * 0.05f);
            style.normal.textColor = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.35f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), value, style);
            style.normal.textColor = new Color(color.r, color.g, color.b, flicker);
            GUI.Label(rect, value, style);
        }

        static GUIStyle Center(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };
        static GUIStyle Right(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleRight };

        void EnsureStyles()
        {
            int size = Mathf.RoundToInt(Mathf.Min(Screen.height * 0.84f, Screen.width * 0.5f) * 0.029f);
            if (label == null || label.fontSize != size)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = false, clipping = TextClipping.Overflow, alignment = TextAnchor.MiddleLeft };
                if (hud && hud.font) label.font = hud.font;
                small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(size * 0.76f) };
                title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(size * 1.35f), fontStyle = FontStyle.Bold };
                hand = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 0.95f), fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            }
            if (!circle)
            {
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
            if (!grime) grime = BuildGrime();
        }

        // Stains, rust blooms and scratches baked once into a texture laid over the panel.
        static Texture2D BuildGrime()
        {
            const int width = 320, height = 200;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Patch panel grime", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float stain = Mathf.PerlinNoise(x * 0.025f + 3, y * 0.025f + 7) * 0.6f + Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.4f;
                float rust = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.05f + 40, y * 0.05f + 11) - 0.68f) * 4);
                float edge = Mathf.Clamp01(1 - Mathf.Min(Mathf.Min(x, width - 1 - x), Mathf.Min(y, height - 1 - y)) / 18f);
                var dark = new Color(0, 0, 0, Mathf.Clamp01((stain - 0.45f) * 0.9f + edge * 0.5f));
                pixels[y * width + x] = Color.Lerp(dark, new Color(0.35f, 0.18f, 0.08f, 0.55f), rust);
            }
            var random = new System.Random(19);
            for (int s = 0; s < 70; s++)
            {
                float x = random.Next(width), y = random.Next(height), angle = (float)random.NextDouble() * Mathf.PI;
                int length = 8 + random.Next(40);
                float alpha = 0.08f + (float)random.NextDouble() * 0.14f;
                for (int i = 0; i < length; i++)
                {
                    int px = Mathf.RoundToInt(x + Mathf.Cos(angle) * i), py = Mathf.RoundToInt(y + Mathf.Sin(angle) * i);
                    if (px < 0 || py < 0 || px >= width || py >= height) break;
                    var c = pixels[py * width + px];
                    pixels[py * width + px] = Color.Lerp(c, new Color(1, 1, 1, alpha), 0.8f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        void OnDestroy()
        {
            if (circle) Destroy(circle);
            if (grime) Destroy(grime);
        }
    }
}
