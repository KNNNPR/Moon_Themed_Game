using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MoonThemedGame.Interactions;
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
        bool subscribed, weakSignal, leavingSoon, covered, charging;

        ScavengerRobotState State => link ? link.state : null;
        float Interference
        {
            get
            {
                float k = link ? link.Noise : 0;
                if (link && link.Phase == LinkPhase.Terminating) k = Mathf.Max(k, Mathf.Clamp01(link.PhaseTime / link.terminateDuration * 2));
                return k;
            }
        }
        float Uptime => Time.time - connectedAt;

        void Awake()
        {
            link = GetComponent<ScavengerLink>();
            if (!font && link && link.terminal && link.terminal.assets) font = link.terminal.assets.font;
        }

        public void OnConnected()
        {
            if (!subscribed && link.Network)
            {
                link.UplinkChanged += OnUplinkChanged;
                link.Network.Selected += OnHandover;
                link.Network.RivalChanged += OnRival;
                subscribed = true;
            }
            weakSignal = leavingSoon = covered = charging = false;
            connectedAt = Time.time;
            packets = Random.Range(10000, 40000);
            log.Clear();
            AddLog(Loc.T("LINK ESTABLISHED  ch 0x3F2A"));
            AddLog(Loc.F("FW {0}", firmware));
            OnUplinkChanged(link.Uplink);
            nextSample = nextLog = 0;
        }

        public void Notify(string message)
        {
            notice = message;
            noticeUntil = Time.time + 3;
            AddLog(message);
        }

        string SatName => link.Network && link.Network.Active != null ? link.Network.Active.name : Loc.T("NONE");

        void OnUplinkChanged(UplinkStatus status)
        {
            if (!link.Connected) return;
            AddLog(status switch
            {
                UplinkStatus.Ok when link.ViaRelay => Loc.T("UPLINK via BASE RELAY"),
                UplinkStatus.Ok => Loc.F("UPLINK via {0}  {1}", SatName, Bars(link.Network.Active.Bars(link.Network.Clock))),
                UplinkStatus.OutOfRange => Loc.F("ERR {0} out of range - uplink lost", SatName),
                UplinkStatus.Intercepted => Loc.F("ALERT {0} carrier hijacked by {1}", SatName, link.Network.rivalName),
                UplinkStatus.Hopping => Loc.T("RECEIVER RETUNE - carrier offline"),
                _ => Loc.T("ERR no satellite selected")
            });
        }

        void OnHandover(Satellite satellite)
        {
            if (link.Connected) AddLog(Loc.F("HANDOVER -> {0}", satellite.name));
        }

        void OnRival(RivalPhase phase)
        {
            if (!link.Connected) return;
            var network = link.Network;
            if (phase == RivalPhase.Incoming)
            {
                AddLog(Loc.F("WARN rival carrier {0} jamming CH{1}", network.rivalName, network.CarrierChannel + 1));
                AddLog(Loc.T("WARN retune to a free channel [H]"));
            }
            else if (phase == RivalPhase.None) AddLog(Loc.F("CARRIER LOCKED CH{0} - {1} lost the signal", network.CarrierChannel + 1, network.rivalName));
        }

        public static string Reason(UplinkStatus status) => Loc.T(status switch
        {
            UplinkStatus.Intercepted => "SIGNAL HIJACKED BY RIVAL SATELLITE",
            UplinkStatus.Hopping => "RECEIVER RETUNING TO NEW CHANNEL",
            UplinkStatus.OutOfRange => "SATELLITE OUT OF RANGE",
            UplinkStatus.NoSatellite => "NO SATELLITE SELECTED",
            _ => ""
        });

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
            bool signal = link.HasPicture;
            float k = Interference;
            if (Time.time >= nextSample)
            {
                nextSample = Time.time + 0.25f;
                float d = link.Degradation;
                latency = signal ? 120 + Random.Range(0f, 40f) + 700 * d * d + 900 * k * k : 0;
                loss = signal ? Mathf.Min(99.9f, link.ExpectedLoss * Random.Range(0.85f, 1.15f) + Random.Range(0f, 0.3f)) : 100;
                rssi = signal ? -60 - 45 * d - Random.Range(0f, 5f) - 20 * k : -120;
                cellTemp = -34 + Random.Range(-0.4f, 0.4f);
                if (signal) packets += Random.Range(20, 30);
            }
            bool nowCharging = state && state.Charging;
            if (nowCharging && !charging) AddLog(Loc.T("DOCKED AT CHARGE STATION - CHARGING"));
            else if (!nowCharging && charging) AddLog(Loc.T(state && state.Normalized >= 0.999f ? "CHARGE FULL" : "UNDOCKED FROM CHARGER"));
            charging = nowCharging;
            bool cover = link.Cover > 0.3f;
            if (cover && !covered) AddLog(Loc.F("WARN overhead cover {0}% - signal degraded", Mathf.RoundToInt(link.Cover * 100)));
            else if (!cover && covered) AddLog(Loc.T("SKY CLEAR - signal restored"));
            covered = cover;
            bool weak = link.Uplink == UplinkStatus.Ok && !link.ViaRelay && !cover && link.UplinkQuality < 0.35f;
            if (weak && !weakSignal) AddLog(Loc.F("WARN {0} signal weak", SatName));
            weakSignal = weak;
            bool leaving = link.Uplink == UplinkStatus.Ok && !link.ViaRelay && link.Network.ActiveLeavingSoon;
            if (leaving && !leavingSoon) AddLog(Loc.F("WARN {0} leaves range in {1}s", SatName, Mathf.CeilToInt(link.Network.Active.TimeLeft(link.Network.Clock))));
            leavingSoon = leaving;
            if (Time.time >= nextLog)
            {
                nextLog = Time.time + Random.Range(1.2f, 3f) * (k > 0.4f ? 0.5f : 1);
                if (signal) AddLog(Loc.T(k > 0.2f && Random.value < k + 0.3f ? Pick(FailingLog) : Pick(RoutineLog)));
            }
        }

        static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];

        static readonly string[] Handshake =
        {
            "DIAL SCAV_BOT  ch 0x3F2A", "SAT UPLINK", "HANDSHAKE", "AUTH OPERATOR", "VIDEO STREAM"
        };

        RfPatchPanel patch;

        void OnGUI()
        {
            if (!link || !link.Connected) return;
            if (!patch) patch = GetComponent<RfPatchPanel>();
            if (Event.current.type == EventType.Repaint)
            {
                EnsureStyles();
                DrawContent();
            }
            if (patch) patch.Gui();
        }

        void DrawContent()
        {
            float unit = Screen.height / 42f;
            float line = unit * 1.35f;
            switch (link.Phase)
            {
                case LinkPhase.Connecting:
                    DrawHandshake(line);
                    return;
                case LinkPhase.Terminating:
                    DrawTerminated(unit, line);
                    return;
            }
            DrawLive(unit, line);
        }

        void DrawHandshake(float line)
        {
            float t = link.PhaseTime;
            float step = link.handshakeDuration / Handshake.Length;
            bool power = link.Powered;
            bool uplink = link.Uplink == UplinkStatus.Ok;
            var origin = new Vector2(Screen.width * 0.3f, Screen.height * 0.36f);
            Draw(new Rect(origin.x, origin.y - line * 1.5f, Screen.width, line), Loc.T("ESTABLISHING CONNECTION") + Dots(t), phosphor, 80, text);
            for (int i = 0; i < Handshake.Length; i++)
            {
                float shown = t - step * i;
                if (shown < 0) break;
                string result;
                bool failed = false;
                if (i == 0) result = "";
                else if (shown < step * 0.8f) result = " " + new string('.', 1 + Mathf.FloorToInt(shown / step * 10));
                else if (i == 1) { failed = !uplink; result = $" ({SatName}) .... " + Loc.T(uplink ? "OK" : "FAIL"); }
                else { failed = i == Handshake.Length - 1 && !(power && uplink); result = " ......... " + Loc.T(failed ? "NO CARRIER" : "OK"); }
                Draw(new Rect(origin.x, origin.y + line * i, Screen.width, line), "> " + Loc.T(Handshake[i]) + result, failed ? warning : phosphor, 81 + i, text);
            }
        }

        void DrawTerminated(float unit, float line)
        {
            var center = new Rect(0, Screen.height * 0.36f, Screen.width, unit * 3);
            float u = link.PhaseTime / link.terminateDuration;
            if (Mathf.Repeat(Time.time * 14, 1) < 0.15f && u > 0.5f) return;
            Alert(center, Loc.T("CONNECTION TERMINATED"), warning, 70);
            int up = Mathf.FloorToInt(Uptime);
            Draw(new Rect(0, center.yMax + unit * 0.5f, Screen.width, line),
                Loc.F("LINK CLOSED BY OPERATOR   SESSION {0:00}:{1:00}   PKT {2}", up / 60, up % 60, packets), phosphor, 71, Centered(text));
            Draw(new Rect(0, center.yMax + unit * 0.5f + line, Screen.width, line), Loc.F("UPLINK {0} RELEASED", SatName), Dim(phosphor), 72, Centered(text));
        }

        static string Dots(float t) => new string('.', 1 + Mathf.FloorToInt(t * 3) % 3);

        void DrawLive(float unit, float line)
        {
            var state = State;
            float pad = unit;
            bool signal = link.HasPicture;
            var inv = CultureInfo.InvariantCulture;

            if (signal)
            {
                var position = link.robot ? link.robot.position : Vector3.zero;
                float heading = link.robot ? link.robot.eulerAngles.y : 0;
                float volts = state ? Mathf.Lerp(10.2f, 12.6f, state.Normalized) : 12.6f;
                int up = Mathf.FloorToInt(Uptime);
                var left = new[]
                {
                    Loc.F("{0} // LIVE FEED", ScavengerLink.RobotName),
                    Loc.F("LINK 0x3F2A  PKT {0}  LOSS {1}%", packets, loss.ToString("0.0", inv)),
                    Loc.F("LAT {0}ms  RSSI {1}dBm", Mathf.RoundToInt(latency), Mathf.RoundToInt(rssi)),
                    Loc.F("POS {0} {1} {2}  HDG {3:000}", position.x.ToString("0.00", inv), position.y.ToString("0.00", inv), position.z.ToString("0.00", inv), Mathf.RoundToInt(heading)),
                    SatLine(),
                    Loc.F("CELL {0}V  T {1}C  CARGO {2}/{3}{4}", volts.ToString("0.0", inv), cellTemp.ToString("0.0", inv), state ? state.Cargo : 0, state ? state.cargoCapacity : 0, RobotUpgrades.Lamps ? Loc.T("  LAMP HI") : ""),
                    Loc.F("UPTIME {0:00}:{1:00}:{2:00}", up / 3600, up / 60 % 60, up % 60)
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
                var tint = state.LowCharge || !link.Powered ? warning : phosphor;
                Draw(new Rect(x, pad, width, line), Loc.F("CHARGE {0:000}%", Mathf.CeilToInt(state.Normalized * 100)), tint, 20, text);
                Draw(new Rect(x, pad + line, width, line), Bar(state.Normalized, 16), tint, 21, text);
                Draw(new Rect(x, pad + line * 2, width, line), Loc.F("STATUS {0}", StatusText(state)), tint, 22, text);

                var center = new Rect(0, Screen.height * 0.38f, Screen.width, unit * 3);
                var below = new Rect(0, center.yMax + unit * 0.5f, Screen.width, line);
                if (link.Powered && link.Uplink != UplinkStatus.Ok)
                {
                    var network = link.Network;
                    string title = link.Uplink switch
                    {
                        UplinkStatus.Intercepted => Loc.T("SIGNAL HIJACKED"),
                        UplinkStatus.Hopping => Loc.F("RETUNING  {0}", Mathf.CeilToInt(network.HopTimeLeft)),
                        _ => Loc.T("NO UPLINK")
                    };
                    Alert(center, title, warning, 34);
                    Draw(below, Reason(link.Uplink), phosphor, 35, Centered(text));
                    string hint = link.Uplink == UplinkStatus.Intercepted ? Loc.T("[H] TUNER - PLUG A FREE CHANNEL AND LOCK")
                        : link.Uplink == UplinkStatus.Hopping ? Loc.T("RETUNING CARRIER...") : Loc.F("PRESS {0} TO SWITCH SATELLITE", SatelliteKeys);
                    Draw(new Rect(0, below.yMax, Screen.width, line), hint, Dim(phosphor), 36, Centered(text));
                }
                else switch (state.Status)
                {
                    case RobotStatus.Failing:
                        Alert(center, Loc.F("SIGNAL LOSS IN {0}", Mathf.CeilToInt(state.TimeLeft)), warning, 30);
                        break;
                    case RobotStatus.Offline:
                        Alert(center, Loc.T("ROBOT OFFLINE"), warning, 31);
                        Draw(below, Loc.F("[E] ORDER EVACUATION - {0} CR   BALANCE {1} CR", state.evacuationCost, link.terminal.Credits), phosphor, 32, Centered(text));
                        break;
                    case RobotStatus.Evacuating:
                        Alert(center, Loc.F("EVACUATION EN ROUTE  {0}", Mathf.CeilToInt(state.TimeLeft)), phosphor, 33);
                        break;
                }
            }
            if (link.Powered) DrawSatellites(unit, line);
            var lever = link.NearLever;
            if (lever && signal && state && state.Status == RobotStatus.Online)
            {
                bool reach = RobotUpgrades.HasArm && link.ArmCanReach(lever);
                string prompt = !RobotUpgrades.HasArm ? Loc.T("LEVER - MANIPULATOR ARM REQUIRED (SHOP)")
                    : link.ArmBusy ? Loc.T("ARM ENGAGED...")
                    : reach ? Loc.F("[E] PULL LEVER  ({0})", Loc.T(lever.on ? "ON" : "OFF"))
                    : Loc.T("LEVER - DRIVE CLOSER, ARM CAN'T REACH");
                Draw(new Rect(0, Screen.height * 0.7f, Screen.width, line), prompt, reach || link.ArmBusy ? phosphor : warning, 41, Centered(text));
            }
            if (notice != null && Time.time < noticeUntil)
                Draw(new Rect(0, Screen.height * 0.62f, Screen.width, line), notice, phosphor, 40, Centered(text));
            Draw(new Rect(pad, Screen.height - pad - line, Screen.width, line), Loc.F("{0} - DRIVE   {1} - SATELLITE   [{2}] - TUNER   ESC - BACK TO PC", DriveKeys, SatelliteKeys, GameControls.Label(GameCommand.RobotPanel)), phosphor, 60, text);
        }

        string SatLine()
        {
            string coverText = link.Cover > 0 ? Loc.F("  COVER {0}%", Mathf.RoundToInt(link.Cover * 100)) : "";
            if (link.ViaRelay) return Loc.F("LINK BASE RELAY  Q {0}%{1}", Mathf.RoundToInt(link.UplinkQuality * 100), coverText);
            var satellite = link.Network ? link.Network.Active : null;
            if (satellite == null) return Loc.T("SAT NONE");
            float clock = link.Network.Clock;
            return satellite.InRange(clock)
                ? Loc.F("SAT {0}  Q {1}%  LEFT {2}{3}", satellite.name, Mathf.RoundToInt(link.UplinkQuality * 100), Clock(satellite.TimeLeft(clock)), coverText)
                : Loc.F("SAT {0}  OUT OF RANGE", satellite.name);
        }

        static string DriveKeys => GameControls.Label(GameCommand.Forward) + "/" + GameControls.Label(GameCommand.Left) + "/" +
            GameControls.Label(GameCommand.Backward) + "/" + GameControls.Label(GameCommand.Right);

        static string SatelliteKeys => GameControls.Label(GameCommand.SatelliteOne) + "/" + GameControls.Label(GameCommand.SatelliteTwo) + "/" +
            GameControls.Label(GameCommand.SatelliteThree);

        static string Bars(int bars) => "[" + new string('|', bars) + new string('.', 4 - bars) + "]";

        static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        // Bottom-right list of satellites with their key, signal and timer.
        void DrawSatellites(float unit, float line)
        {
            var network = link.Network;
            if (!network) return;
            float clock = network.Clock;
            float width = unit * 16;
            float x = Screen.width - unit - width;
            float y = Screen.height - unit - line * (network.satellites.Count + 2);
            Draw(new Rect(x, y, width, line), Loc.T("SATELLITES"), Dim(phosphor), 90, text);
            for (int i = 0; i < network.satellites.Count; i++)
            {
                var sat = network.satellites[i];
                bool active = i == network.ActiveIndex;
                string timer = sat.InRange(clock) ? Loc.F("{0} left", Clock(sat.TimeLeft(clock))) : Loc.F("back {0}", Clock(sat.TimeToReturn(clock)));
                string row = $"{(active ? ">" : " ")}[{i + 1}] {sat.name,-6} {Bars(sat.Bars(clock))} {timer}";
                var tint = !sat.InRange(clock) ? Dim(Dim(phosphor)) : active ? phosphor : Dim(phosphor);
                Draw(new Rect(x, y + line * (i + 1), width, line), row, tint, 91 + i, text);
            }
            if (link.Uplink == UplinkStatus.Ok && network.Rival == RivalPhase.Incoming && network.RivalOnActive && Mathf.Repeat(Time.time * 3, 1) < 0.7f)
                Draw(new Rect(0, unit * 7.2f, Screen.width, line),
                    Loc.F("RIVAL {0} INTERCEPTING {1}  {2}%  -  [H] TUNER", network.rivalName, network.Active.name, Mathf.RoundToInt(network.RivalProgress * 100)), warning, 96, Centered(text));
            if (link.Uplink == UplinkStatus.Ok && !link.ViaRelay && network.ActiveLeavingSoon && Mathf.Repeat(Time.time * 2, 1) < 0.7f)
            {
                int left = Mathf.CeilToInt(network.Active.TimeLeft(clock));
                Draw(new Rect(0, unit * 6, Screen.width, line), Loc.F("{0} LEAVES RANGE IN {1}s - SWITCH SATELLITE ({2})", network.Active.name, left, SatelliteKeys), warning, 95, Centered(text));
            }
        }

        internal void EnsureStyles()
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

        static string StatusText(ScavengerRobotState state) => Loc.T(state.Status switch
        {
            _ when state.Charging => "CHARGING",
            RobotStatus.Online => state.LowCharge ? "LOW CHARGE" : "ONLINE",
            RobotStatus.Failing => "CRITICAL",
            RobotStatus.Offline => "OFFLINE",
            _ => "EVACUATING"
        });

        // Big alerts glow hard and skip the faded glyphs so they read at a glance.
        void Alert(Rect rect, string message, Color color, int salt)
        {
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 6);
            Backing(rect, message, big);
            CopyLayout(big);
            ghost.normal.textColor = new Color(color.r, color.g, color.b, 0.3f * pulse);
            for (int i = 0; i < 4; i++)
            {
                var o = new Vector2(i % 2 == 0 ? 3 : -3, i < 2 ? 2 : -2);
                GUI.Label(new Rect(rect.x + o.x, rect.y + o.y, rect.width, rect.height), message, ghost);
            }
            Draw(rect, message, Color.Lerp(color, Color.white, 0.25f), salt, big, false);
        }

        // Dark plate behind the text so it reads over the busy ASCII feed.
        static void Backing(Rect rect, string message, GUIStyle style)
        {
            var plain = new GUIStyle(style) { richText = false };
            var size = plain.CalcSize(new GUIContent(message));
            float x = style.alignment == TextAnchor.MiddleCenter ? rect.center.x - size.x * 0.5f : rect.x;
            float y = style.alignment == TextAnchor.MiddleCenter ? rect.center.y - size.y * 0.5f : rect.y;
            var previous = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(x - 4, y, size.x + 8, size.y), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void CopyLayout(GUIStyle style)
        {
            ghost.fontSize = style.fontSize;
            ghost.fontStyle = style.fontStyle;
            ghost.alignment = style.alignment;
            ghost.font = style.font;
        }

        // Phosphor bleed, line jitter, permanently faded glyphs and glitches that grow with interference.
        internal void Draw(Rect rect, string message, Color color, int salt, GUIStyle style, bool worn = true)
        {
            message=MoonThemedGame.Interactions.GameControls.FormatPrompt(message);
            float k = Interference;
            int slice = Mathf.FloorToInt(Time.time * 10);
            if (Hash(salt, slice, 1) < 0.03f + 0.3f * k) rect.x += (Hash(salt, slice, 2) - 0.5f) * (4 + 20 * k);
            float flicker = worn ? 0.88f + 0.12f * Hash(salt, slice, 3) : 1;

            if (worn) Backing(rect, message, style);
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
