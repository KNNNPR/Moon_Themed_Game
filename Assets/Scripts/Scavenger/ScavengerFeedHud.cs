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
        public Color warning = new Color(1f, 0.45f, 0.3f);
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

        void OnGUI()
        {
            if (!link || !link.Connected || Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            var state = State;
            float unit = Screen.height / 42f;
            float pad = unit;
            float line = unit * 1.35f;
            bool signal = !state || state.HasSignal;
            float k = Interference;
            var inv = CultureInfo.InvariantCulture;

            var position = link.robot ? link.robot.position : Vector3.zero;
            float heading = link.robot ? link.robot.eulerAngles.y : 0;
            float volts = state ? Mathf.Lerp(10.2f, 12.6f, state.Normalized) : 12.6f;
            int up = Mathf.FloorToInt(Uptime);
            var left = new[]
            {
                $"{ScavengerLink.RobotName} // {(signal ? "LIVE FEED" : "NO SIGNAL")}",
                $"LINK 0x3F2A  PKT {packets}  LOSS {loss.ToString("0.0", inv)}%",
                $"LAT {(signal ? Mathf.RoundToInt(latency) + "ms" : "---")}  RSSI {Mathf.RoundToInt(rssi)}dBm",
                $"POS {position.x.ToString("0.00", inv)} {position.y.ToString("0.00", inv)} {position.z.ToString("0.00", inv)}  HDG {Mathf.RoundToInt(heading):000}",
                $"CELL {volts.ToString("0.0", inv)}V  T {cellTemp.ToString("0.0", inv)}C",
                $"UPTIME {up / 3600:00}:{up / 60 % 60:00}:{up % 60:00}"
            };
            for (int i = 0; i < left.Length; i++)
                Draw(new Rect(pad, pad + line * i, Screen.width, line), left[i], i == 0 ? phosphor : Dim(phosphor), i, text);

            if (state)
            {
                float width = unit * 11;
                float x = Screen.width - pad - width;
                bool alarm = state.LowCharge || !signal;
                var tint = alarm ? warning : phosphor;
                Draw(new Rect(x, pad, width, line), $"CHARGE {Mathf.CeilToInt(state.Normalized * 100):000}%", tint, 20, text);
                Draw(new Rect(x, pad + line, width, line), Bar(state.Normalized, 16), tint, 21, text);
                Draw(new Rect(x, pad + line * 2, width, line), $"STATUS {StatusText(state)}", tint, 22, text);

                var center = new Rect(0, Screen.height * 0.38f, Screen.width, unit * 3);
                var below = new Rect(0, center.yMax, Screen.width, line);
                switch (state.Status)
                {
                    case RobotStatus.Failing:
                        Draw(center, $"SIGNAL LOSS IN {Mathf.CeilToInt(state.TimeLeft)}", warning, 30, big);
                        break;
                    case RobotStatus.Offline:
                        Draw(center, "ROBOT OFFLINE", warning, 31, big);
                        Draw(below, $"[E] ORDER EVACUATION - {state.evacuationCost} CR   BALANCE {link.terminal.Credits} CR", phosphor, 32, Centered(text));
                        break;
                    case RobotStatus.Evacuating:
                        Draw(center, $"EVACUATION EN ROUTE  {Mathf.CeilToInt(state.TimeLeft)}", phosphor, 33, big);
                        break;
                }
            }
            if (notice != null && Time.time < noticeUntil)
                Draw(new Rect(0, Screen.height * 0.62f, Screen.width, line), notice, phosphor, 40, Centered(text));

            int row = 0;
            float logTop = Screen.height - pad - line * (log.Count + 1.5f);
            foreach (var entry in log)
                Draw(new Rect(pad, logTop + line * row, Screen.width, line), entry, Dim(phosphor), 50 + row++, text);
            Draw(new Rect(pad, Screen.height - pad - line, Screen.width, line), "ESC - BACK TO PC", phosphor, 60, text);
        }

        void EnsureStyles()
        {
            if (text != null) return;
            int size = Mathf.RoundToInt(Screen.height / 46f);
            text = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, wordWrap = false, clipping = TextClipping.Overflow };
            if (font) text.font = font;
            big = new GUIStyle(text) { fontSize = size * 2, alignment = TextAnchor.MiddleCenter };
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

        // Phosphor bleed, line jitter, permanently faded glyphs and glitches that grow with interference.
        void Draw(Rect rect, string message, Color color, int salt, GUIStyle style)
        {
            float k = Interference;
            int slice = Mathf.FloorToInt(Time.time * 10);
            if (Hash(salt, slice, 1) < 0.03f + 0.3f * k) rect.x += (Hash(salt, slice, 2) - 0.5f) * (4 + 20 * k);
            float flicker = 0.88f + 0.12f * Hash(salt, slice, 3);

            var previous = GUI.color;
            ghost.fontSize = style.fontSize;
            ghost.alignment = style.alignment;
            ghost.font = style.font;
            ghost.normal.textColor = new Color(color.r, color.g, color.b, 0.12f * flicker);
            GUI.Label(new Rect(rect.x + 2, rect.y, rect.width, rect.height), message, ghost);
            GUI.Label(new Rect(rect.x - 1, rect.y + 1, rect.width, rect.height), message, ghost);

            builder.Clear();
            for (int i = 0; i < message.Length; i++)
            {
                char c = message[i];
                if (c == ' ') { builder.Append(c); continue; }
                float glitch = Hash(salt * 31 + i, slice, 4);
                if (glitch < 0.004f + 0.12f * k * k) c = GlitchChars[Mathf.FloorToInt(Hash(i, slice, 5) * GlitchChars.Length) % GlitchChars.Length];
                float alpha = Hash(salt * 17 + i, 0, 6) < wear ? 0.35f + 0.25f * Hash(i, salt, 7) : 1;
                alpha *= flicker * (1 - 0.6f * k * Hash(i, slice, 8));
                builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(new Color(color.r, color.g, color.b, alpha))).Append('>');
                builder.Append(c);
                builder.Append("</color>");
            }
            style.normal.textColor = color;
            GUI.Label(rect, builder.ToString(), style);
            GUI.color = previous;
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
