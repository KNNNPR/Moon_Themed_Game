using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Worn phosphor-terminal overlay for the robot feed: status, charge and fake telemetry.
    [DisallowMultipleComponent]
    public sealed class ScavengerFeedHud : MonoBehaviour
    {
        public Color phosphor = new Color(0.78f, 0.96f, 0.8f);
        public Color warning = new Color(1f, 0.3f, 0.22f);
        public Font font;
        [Range(0, 1)] public float wear = 0.12f;
        public string firmware = "PH-SCAV 0.9.3b";

        const string GlitchChars = "#%&@*/\\|=+-_?";
        static readonly string[] RoutineLog =
        {
            "PING base.station ... ok", "SERVO BUS 4/4 ok", "IMU recal ... ok", "LENS heater on",
            "UPLINK frame resync", "THERMAL loop nominal", "MEM 61% heap ok", "GPS-L lock 7 sat",
            "DUST on lens: 12%", "WHEEL ENC drift 0.02", "WATCHDOG kick", "CAM exposure auto"
        };
        static readonly string[] FailingLog =
        {
            "WARN signal degraded", "ERR packet crc", "WARN cell undervolt", "ERR frame dropped",
            "WARN brownout imminent", "ERR uplink timeout"
        };

        ScavengerLink link;
        GUIStyle text, big, ghost;
        readonly StringBuilder builder = new StringBuilder(256);
        readonly Queue<string> log = new Queue<string>();
        float connectedAt, nextSample, nextLog, latency, loss, rssi, cellTemp;
        int packets;
        string notice;
        float noticeUntil;

        ScavengerRobotState State => link ? link.state : null;
        float Interference => State ? State.Interference : 0;
        float Uptime => Time.time - connectedAt;

        void Awake()
        {
            link = GetComponent<ScavengerLink>();
            if (!font && link && link.terminal && link.terminal.assets) font = link.terminal.assets.font;
        }

        public void OnConnected()
        {
            connectedAt = Time.time;
            packets = Random.Range(10000, 40000);
            log.Clear();
            AddLog("LINK ESTABLISHED  ch 0x3F2A");
            AddLog($"FW {firmware}");
            nextSample = nextLog = 0;
        }

        public void Notify(string message)
        {
            notice = message;
            noticeUntil = Time.time + 3;
            AddLog(message);
        }

        void AddLog(string line)
        {
            int seconds = Mathf.FloorToInt(Uptime);
            log.Enqueue($"[{seconds / 60:00}:{seconds % 60:00}] {line}");
            while (log.Count > 5) log.Dequeue();
        }

        void Update()
        {
            if (!link || !link.Connected) return;
            var state = State;
            bool signal = !state || state.HasSignal;
            float k = Interference;
            if (Time.time >= nextSample)
            {
                nextSample = Time.time + 0.25f;
                latency = signal ? 120 + Random.Range(0f, 40f) + 900 * k * k : 0;
                loss = signal ? Random.Range(0.1f, 0.6f) + 60 * k * k : 100;
                rssi = signal ? -68 - Random.Range(0f, 5f) - 25 * k : -120;
                cellTemp = -34 + Random.Range(-0.4f, 0.4f);
                if (signal) packets += Random.Range(20, 30);
            }
            if (Time.time >= nextLog)
            {
                nextLog = Time.time + Random.Range(1.2f, 3f) * (k > 0.4f ? 0.5f : 1);
                if (signal) AddLog(k > 0.2f && Random.value < k + 0.3f ? Pick(FailingLog) : Pick(RoutineLog));
                else if (state && state.Status == RobotStatus.Offline) AddLog("NO CARRIER");
            }
        }

        static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];

        static readonly string[] Handshake =
        {
            "DIAL SCAV_BOT  ch 0x3F2A", "CARRIER", "HANDSHAKE", "AUTH OPERATOR", "VIDEO STREAM"
        };

        void OnGUI()
        {
            if (!link || !link.Connected || Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            float unit = Screen.height / 42f;
            float line = unit * 1.35f;
            switch (link.Phase)
            {
                case LinkPhase.Connecting:
                    DrawHandshake(line);
                    return;
                case LinkPhase.Terminating:
                    Alert(new Rect(0, Screen.height * 0.38f, Screen.width, unit * 3), "CONNECTION TERMINATED", phosphor, 70);
                    return;
            }
            DrawLive(unit, line);
        }

        void DrawHandshake(float line)
        {
            float t = link.PhaseTime;
            float step = link.handshakeDuration / Handshake.Length;
            bool carrier = !State || State.HasSignal;
            var origin = new Vector2(Screen.width * 0.3f, Screen.height * 0.36f);
            Draw(new Rect(origin.x, origin.y - line * 1.5f, Screen.width, line), "ESTABLISHING CONNECTION" + Dots(t), phosphor, 80, text);
            for (int i = 0; i < Handshake.Length; i++)
            {
                float shown = t - step * i;
                if (shown < 0) break;
                string result;
                if (i == 0) result = "";
                else if (shown < step * 0.8f) result = " " + new string('.', 1 + Mathf.FloorToInt(shown / step * 10));
                else result = i == Handshake.Length - 1 && !carrier ? " ......... NO CARRIER" : " ......... OK";
                var tint = result.EndsWith("NO CARRIER") ? warning : phosphor;
                Draw(new Rect(origin.x, origin.y + line * i, Screen.width, line), "> " + Handshake[i] + result, tint, 81 + i, text);
            }
        }

        static string Dots(float t) => new string('.', 1 + Mathf.FloorToInt(t * 3) % 3);

        void DrawLive(float unit, float line)
        {
            var state = State;
            float pad = unit;
            bool signal = !state || state.HasSignal;
            var inv = CultureInfo.InvariantCulture;

            if (signal)
            {
                var position = link.robot ? link.robot.position : Vector3.zero;
                float heading = link.robot ? link.robot.eulerAngles.y : 0;
                float volts = state ? Mathf.Lerp(10.2f, 12.6f, state.Normalized) : 12.6f;
                int up = Mathf.FloorToInt(Uptime);
                var left = new[]
                {
                    $"{ScavengerLink.RobotName} // LIVE FEED",
                    $"LINK 0x3F2A  PKT {packets}  LOSS {loss.ToString("0.0", inv)}%",
                    $"LAT {Mathf.RoundToInt(latency)}ms  RSSI {Mathf.RoundToInt(rssi)}dBm",
                    $"POS {position.x.ToString("0.00", inv)} {position.y.ToString("0.00", inv)} {position.z.ToString("0.00", inv)}  HDG {Mathf.RoundToInt(heading):000}",
                    $"CELL {volts.ToString("0.0", inv)}V  T {cellTemp.ToString("0.0", inv)}C",
                    $"UPTIME {up / 3600:00}:{up / 60 % 60:00}:{up % 60:00}"
                };
                for (int i = 0; i < left.Length; i++)
                    Draw(new Rect(pad, pad + line * i, Screen.width, line), left[i], i == 0 ? phosphor : Dim(phosphor), i, text);

                int row = 0;
                float logTop = Screen.height - pad - line * (log.Count + 1.5f);
                foreach (var entry in log)
                    Draw(new Rect(pad, logTop + line * row, Screen.width, line), entry, Dim(phosphor), 50 + row++, text);
            }

            if (state)
            {
                float width = unit * 11;
                float x = Screen.width - pad - width;
                var tint = state.LowCharge || !signal ? warning : phosphor;
                Draw(new Rect(x, pad, width, line), $"CHARGE {Mathf.CeilToInt(state.Normalized * 100):000}%", tint, 20, text);
                Draw(new Rect(x, pad + line, width, line), Bar(state.Normalized, 16), tint, 21, text);
                Draw(new Rect(x, pad + line * 2, width, line), $"STATUS {StatusText(state)}", tint, 22, text);

                var center = new Rect(0, Screen.height * 0.38f, Screen.width, unit * 3);
                var below = new Rect(0, center.yMax + unit * 0.5f, Screen.width, line);
                switch (state.Status)
                {
                    case RobotStatus.Failing:
                        Alert(center, $"SIGNAL LOSS IN {Mathf.CeilToInt(state.TimeLeft)}", warning, 30);
                        break;
                    case RobotStatus.Offline:
                        Alert(center, "ROBOT OFFLINE", warning, 31);
                        Draw(below, $"[E] ORDER EVACUATION - {state.evacuationCost} CR   BALANCE {link.terminal.Credits} CR", phosphor, 32, Centered(text));
                        break;
                    case RobotStatus.Evacuating:
                        Alert(center, $"EVACUATION EN ROUTE  {Mathf.CeilToInt(state.TimeLeft)}", phosphor, 33);
                        break;
                }
            }
            if (notice != null && Time.time < noticeUntil)
                Draw(new Rect(0, Screen.height * 0.62f, Screen.width, line), notice, phosphor, 40, Centered(text));
            Draw(new Rect(pad, Screen.height - pad - line, Screen.width, line), "ESC - BACK TO PC", phosphor, 60, text);
        }

        void EnsureStyles()
        {
            if (text != null) return;
            int size = Mathf.RoundToInt(Screen.height / 46f);
            text = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, wordWrap = false, clipping = TextClipping.Overflow };
            if (font) text.font = font;
            big = new GUIStyle(text) { fontSize = Mathf.RoundToInt(size * 2.4f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            ghost = new GUIStyle(text) { richText = false };
        }

        static GUIStyle Centered(GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };

        static Color Dim(Color c) => new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, c.a);

        static string Bar(float value, int cells)
        {
            int filled = Mathf.Clamp(Mathf.CeilToInt(value * cells), 0, cells);
            return "[" + new string('|', filled) + new string('.', cells - filled) + "]";
        }

        static string StatusText(ScavengerRobotState state) => state.Status switch
        {
            RobotStatus.Online => state.LowCharge ? "LOW CHARGE" : "ONLINE",
            RobotStatus.Failing => "CRITICAL",
            RobotStatus.Offline => "OFFLINE",
            _ => "EVACUATING"
        };

        // Big alerts glow hard and skip the faded glyphs so they read at a glance.
        void Alert(Rect rect, string message, Color color, int salt)
        {
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 6);
            CopyLayout(big);
            ghost.normal.textColor = new Color(color.r, color.g, color.b, 0.3f * pulse);
            for (int i = 0; i < 4; i++)
            {
                var o = new Vector2(i % 2 == 0 ? 3 : -3, i < 2 ? 2 : -2);
                GUI.Label(new Rect(rect.x + o.x, rect.y + o.y, rect.width, rect.height), message, ghost);
            }
            Draw(rect, message, Color.Lerp(color, Color.white, 0.25f), salt, big, false);
        }

        void CopyLayout(GUIStyle style)
        {
            ghost.fontSize = style.fontSize;
            ghost.fontStyle = style.fontStyle;
            ghost.alignment = style.alignment;
            ghost.font = style.font;
        }

        // Phosphor bleed, line jitter, permanently faded glyphs and glitches that grow with interference.
        void Draw(Rect rect, string message, Color color, int salt, GUIStyle style, bool worn = true)
        {
            float k = Interference;
            int slice = Mathf.FloorToInt(Time.time * 10);
            if (Hash(salt, slice, 1) < 0.03f + 0.3f * k) rect.x += (Hash(salt, slice, 2) - 0.5f) * (4 + 20 * k);
            float flicker = worn ? 0.88f + 0.12f * Hash(salt, slice, 3) : 1;

            CopyLayout(style);
            ghost.normal.textColor = new Color(color.r, color.g, color.b, 0.12f * flicker);
            GUI.Label(new Rect(rect.x + 2, rect.y, rect.width, rect.height), message, ghost);
            GUI.Label(new Rect(rect.x - 1, rect.y + 1, rect.width, rect.height), message, ghost);

            builder.Clear();
            for (int i = 0; i < message.Length; i++)
            {
                char c = message[i];
                if (c == ' ') { builder.Append(c); continue; }
                if (Hash(salt * 31 + i, slice, 4) < 0.004f + 0.12f * k * k) c = GlitchChars[Mathf.FloorToInt(Hash(i, slice, 5) * GlitchChars.Length) % GlitchChars.Length];
                float alpha = worn && Hash(salt * 17 + i, 0, 6) < wear ? 0.35f + 0.25f * Hash(i, salt, 7) : 1;
                alpha *= flicker * (1 - 0.6f * k * Hash(i, slice, 8));
                builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(new Color(color.r, color.g, color.b, alpha))).Append('>').Append(c).Append("</color>");
            }
            style.normal.textColor = color;
            GUI.Label(rect, builder.ToString(), style);
        }

        static float Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
