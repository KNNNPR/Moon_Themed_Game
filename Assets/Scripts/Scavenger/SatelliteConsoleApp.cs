using MoonThemedGame.Computer;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // SAT-LINK desktop app: orbit map, which satellite carries the robot's signal, and rival intercepts.
    [DisallowMultipleComponent]
    public sealed class SatelliteConsoleApp : MonoBehaviour
    {
        static readonly Color Text = new Color(0.94f, 0.96f, 0.97f);
        static readonly Color Muted = new Color(0.5f, 0.56f, 0.6f);
        static readonly Color Good = new Color(0.55f, 1f, 0.6f);
        static readonly Color Alarm = new Color(1f, 0.38f, 0.3f);

        sealed class Card
        {
            public VisualElement root, fill;
            public VisualElement[] bars;
            public Label name, status, timer, action;
        }

        PcMonitorTerminal terminal;
        ScavengerLink link;
        SatelliteNetwork network;
        VisualElement icon, window, close, map, threat;
        Card[] cards;
        Label robotStatus, threatText;
        Label[] mapLabels;
        Label rivalLabel;
        float nextRefresh;

        public bool IsOpen => window != null && window.style.display == DisplayStyle.Flex;

        void Awake()
        {
            terminal = GetComponent<PcMonitorTerminal>();
            link = GetComponent<ScavengerLink>();
        }

        void Update()
        {
            if (!terminal) return;
            if (icon == null && terminal.Root != null) Build();
            if (icon == null) return;

            bool pointer = terminal.IsFocused && terminal.PointerActive && PcLogin.IsUnlocked(terminal);
            var point = terminal.PointerPosition;
            icon.EnableInClassList("pointer-over", pointer && icon.worldBound.Contains(point));
            if (IsOpen)
            {
                foreach (var card in cards)
                    card.root.style.borderLeftColor = card.root.style.borderRightColor = card.root.style.borderTopColor = card.root.style.borderBottomColor =
                        pointer && card.root.worldBound.Contains(point) ? Text : new Color(1, 1, 1, 0.12f);
                close.style.color = pointer && close.worldBound.Contains(point) ? Color.white : Muted;
                map.MarkDirtyRepaint();
            }
            if (pointer && Clicked()) HandleClick(point);
            if (IsOpen && Time.time >= nextRefresh) Refresh();
        }

        static bool Clicked()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        void HandleClick(Vector2 point)
        {
            if (icon.worldBound.Contains(point)) { SetOpen(!IsOpen); return; }
            if (!IsOpen) return;
            if (close.worldBound.Contains(point)) { SetOpen(false); return; }
            for (int i = 0; i < cards.Length; i++)
                if (cards[i].root.worldBound.Contains(point)) { network.Select(i); Refresh(); return; }
        }

        void SetOpen(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) Refresh();
        }

        void Build()
        {
            network = SatelliteNetwork.Instance;
            var cursor = terminal.Root.Q("screen-cursor");
            var desktop = cursor != null ? cursor.parent : terminal.Root;
            int index = cursor != null ? desktop.IndexOf(cursor) : desktop.childCount;

            icon = Box();
            icon.name = "satellites";
            icon.AddToClassList("desktop-icon");
            icon.style.position = Position.Absolute;
            icon.style.left = 204;
            icon.style.top = 24;
            var art = Box();
            art.AddToClassList("icon-image");
            art.generateVisualContent += DrawIcon;
            icon.Add(art);
            var iconLabel = new Label("SATELLITES") { pickingMode = PickingMode.Ignore };
            iconLabel.AddToClassList("icon-label");
            icon.Add(iconLabel);
            desktop.Insert(index++, icon);

            window = Box();
            window.name = "sat-link";
            var s = window.style;
            s.position = Position.Absolute;
            s.left = 300;
            s.top = 20;
            s.width = 640;
            s.backgroundColor = new Color(0.02f, 0.04f, 0.055f, 0.97f);
            Border(window, Muted);
            s.paddingLeft = s.paddingRight = 12;
            s.paddingTop = s.paddingBottom = 10;
            s.display = DisplayStyle.None;

            var title = Box();
            title.style.flexDirection = FlexDirection.Row;
            title.style.justifyContent = Justify.SpaceBetween;
            title.Add(MakeLabel("SAT-LINK", 18, Text));
            close = MakeLabel("[X]", 18, Muted);
            title.Add(close);
            window.Add(title);
            var help = MakeLabel("The robot's signal goes through ONE satellite. Pick one that is IN RANGE (over the station, bright arc).\nIn the robot: keys 1 / 2 / 3. A rival intercept is beaten in the robot feed: [H] RF patch panel.", 12, Muted);
            help.style.marginTop = 4;
            help.style.marginBottom = 8;
            help.style.whiteSpace = WhiteSpace.Normal;
            window.Add(help);

            var body = Box();
            body.style.flexDirection = FlexDirection.Row;
            window.Add(body);
            body.Add(BuildMap());
            var column = Box();
            column.style.flexGrow = 1;
            column.style.marginLeft = 10;
            body.Add(column);
            cards = new Card[network.satellites.Count];
            for (int i = 0; i < cards.Length; i++) column.Add(BuildCard(i));
            column.Add(BuildThreat());

            robotStatus = MakeLabel("", 14, Text);
            robotStatus.style.marginTop = 8;
            robotStatus.style.whiteSpace = WhiteSpace.Normal;
            window.Add(robotStatus);
            desktop.Insert(index, window);
        }

        const float MapSize = 240;

        VisualElement BuildMap()
        {
            map = Box();
            map.style.width = map.style.height = MapSize;
            map.style.flexShrink = 0;
            map.style.backgroundColor = new Color(0, 0, 0, 0.35f);
            Border(map, new Color(1, 1, 1, 0.12f));
            map.generateVisualContent += DrawMap;
            mapLabels = new Label[network.satellites.Count];
            for (int i = 0; i < mapLabels.Length; i++)
            {
                mapLabels[i] = MakeLabel(network.satellites[i].name, 11, Text);
                mapLabels[i].style.position = Position.Absolute;
                map.Add(mapLabels[i]);
            }
            rivalLabel = MakeLabel(network.rivalName, 11, Alarm);
            rivalLabel.style.position = Position.Absolute;
            map.Add(rivalLabel);
            var station = MakeLabel("STATION", 10, Muted);
            station.style.position = Position.Absolute;
            station.style.left = MapSize * 0.5f - 18;
            station.style.top = MapSize * 0.5f + 4;
            map.Add(station);
            return map;
        }

        VisualElement BuildThreat()
        {
            threat = Box();
            threat.style.marginTop = 2;
            threat.style.paddingLeft = threat.style.paddingRight = 8;
            threat.style.paddingTop = threat.style.paddingBottom = 6;
            Border(threat, new Color(1, 1, 1, 0.12f));
            var row = Box();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            threatText = MakeLabel("", 13, Muted);
            threatText.style.whiteSpace = WhiteSpace.Normal;
            threatText.style.flexShrink = 1;
            row.Add(threatText);
            threat.Add(row);
            return threat;
        }

        float OrbitRadius(int i) => MapSize * (0.2f + 0.085f * i);
        float RivalRadius => MapSize * 0.46f;
        Vector2 MapCenter => new Vector2(MapSize, MapSize) * 0.5f;
        static Vector2 OnOrbit(Vector2 center, float radius, float angle) => center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius;

        Vector2 SatellitePoint(int i) => OnOrbit(MapCenter, OrbitRadius(i), network.satellites[i].Angle(network.Clock));

        Vector2 RivalPoint()
        {
            float clock = network.Clock;
            if (network.Rival == RivalPhase.None || network.RivalTarget < 0)
                return OnOrbit(MapCenter, RivalRadius, -clock * 2 * Mathf.PI / 260);
            int target = network.RivalTarget;
            float angle = network.satellites[target].Angle(clock);
            float t = network.Rival == RivalPhase.Hijacked ? 1 : network.RivalProgress;
            return OnOrbit(MapCenter, Mathf.Lerp(RivalRadius, OrbitRadius(target) + 10, t), angle - (1 - t) * 1.6f);
        }

        void DrawMap(MeshGenerationContext context)
        {
            if (network == null) return;
            var p = context.painter2D;
            var center = MapCenter;
            float clock = network.Clock;
            float blink = Mathf.Repeat(Time.time * 2, 1) < 0.6f ? 1 : 0.35f;

            p.fillColor = new Color(0.55f, 0.58f, 0.6f, 0.9f);
            p.BeginPath();
            p.Arc(center, MapSize * 0.11f, 0, 360);
            p.Fill();
            var station = OnOrbit(center, MapSize * 0.11f, 0);
            p.fillColor = Good;
            p.BeginPath();
            p.Arc(station, 3.5f, 0, 360);
            p.Fill();

            for (int i = 0; i < network.satellites.Count; i++)
            {
                var sat = network.satellites[i];
                float radius = OrbitRadius(i);
                bool active = i == network.ActiveIndex;
                p.lineWidth = 1;
                p.strokeColor = new Color(1, 1, 1, active ? 0.3f : 0.14f);
                p.BeginPath();
                p.Arc(center, radius, 0, 360);
                p.Stroke();
                float half = 180 * sat.window / sat.period;
                p.lineWidth = 3;
                p.strokeColor = new Color(Good.r, Good.g, Good.b, active ? 0.55f : 0.28f);
                p.BeginPath();
                p.Arc(center, radius, -90 - half, -90 + half);
                p.Stroke();
            }

            for (int i = 0; i < network.satellites.Count; i++)
            {
                var sat = network.satellites[i];
                var point = SatellitePoint(i);
                bool active = i == network.ActiveIndex, inRange = sat.InRange(clock);
                if (active && inRange)
                {
                    bool hijacked = network.Rival == RivalPhase.Hijacked && network.RivalOnActive;
                    p.lineWidth = 1.5f;
                    p.strokeColor = hijacked ? new Color(Alarm.r, Alarm.g, Alarm.b, blink) : network.Hopping ? new Color(1, 1, 1, 0.25f * blink) : new Color(Good.r, Good.g, Good.b, 0.8f);
                    Dashed(p, station, point, 5, 4);
                }
                p.fillColor = inRange ? (active ? Color.white : Text * 0.85f) : Muted;
                p.BeginPath();
                p.Arc(point, active ? 5 : 3.5f, 0, 360);
                p.Fill();
                if (active)
                {
                    p.lineWidth = 1;
                    p.strokeColor = Color.white;
                    p.BeginPath();
                    p.Arc(point, 8, 0, 360);
                    p.Stroke();
                }
            }

            var rival = RivalPoint();
            bool attacking = network.Rival != RivalPhase.None && network.RivalTarget >= 0;
            if (attacking)
            {
                p.lineWidth = 1.5f;
                p.strokeColor = new Color(Alarm.r, Alarm.g, Alarm.b, blink);
                Dashed(p, rival, SatellitePoint(network.RivalTarget), 3, 3);
            }
            p.fillColor = new Color(Alarm.r, Alarm.g, Alarm.b, attacking ? blink : 0.5f);
            p.BeginPath();
            p.MoveTo(rival + new Vector2(0, -5));
            p.LineTo(rival + new Vector2(5, 0));
            p.LineTo(rival + new Vector2(0, 5));
            p.LineTo(rival + new Vector2(-5, 0));
            p.ClosePath();
            p.Fill();
        }

        static void Dashed(Painter2D p, Vector2 from, Vector2 to, float dash, float gap)
        {
            float length = Vector2.Distance(from, to);
            if (length < 0.01f) return;
            var direction = (to - from) / length;
            p.BeginPath();
            for (float d = 0; d < length; d += dash + gap)
            {
                p.MoveTo(from + direction * d);
                p.LineTo(from + direction * Mathf.Min(length, d + dash));
            }
            p.Stroke();
        }

        VisualElement BuildCard(int i)
        {
            var card = cards[i] = new Card { root = Box(), bars = new VisualElement[4] };
            var root = card.root;
            root.style.marginBottom = 6;
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = root.style.paddingBottom = 6;
            Border(root, new Color(1, 1, 1, 0.12f));

            var top = Box();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            card.name = MakeLabel($"{i + 1}  {network.satellites[i].name}", 16, Text);
            card.name.style.width = 120;
            top.Add(card.name);
            var bars = Box();
            bars.style.flexDirection = FlexDirection.Row;
            bars.style.alignItems = Align.FlexEnd;
            bars.style.height = 16;
            bars.style.width = 44;
            for (int b = 0; b < 4; b++)
            {
                var bar = card.bars[b] = Box();
                bar.style.width = 6;
                bar.style.height = 4 + b * 4;
                bar.style.marginRight = 3;
                bars.Add(bar);
            }
            top.Add(bars);
            card.status = MakeLabel("", 14, Text);
            card.status.style.flexGrow = 1;
            card.status.style.marginLeft = 10;
            top.Add(card.status);
            card.action = MakeLabel("", 14, Muted);
            top.Add(card.action);
            root.Add(top);

            var bottom = Box();
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.alignItems = Align.Center;
            bottom.style.marginTop = 4;
            var track = Box();
            track.style.flexGrow = 1;
            track.style.height = 5;
            track.style.backgroundColor = new Color(1, 1, 1, 0.1f);
            card.fill = Box();
            card.fill.style.height = Length.Percent(100);
            track.Add(card.fill);
            bottom.Add(track);
            card.timer = MakeLabel("", 13, Muted);
            card.timer.style.width = 150;
            card.timer.style.unityTextAlign = TextAnchor.MiddleRight;
            bottom.Add(card.timer);
            root.Add(bottom);
            return root;
        }

        void Refresh()
        {
            nextRefresh = Time.time + 0.2f;
            float clock = network.Clock;
            for (int i = 0; i < cards.Length; i++)
            {
                var sat = network.satellites[i];
                var card = cards[i];
                bool active = i == network.ActiveIndex;
                bool inRange = sat.InRange(clock);
                bool leaving = inRange && sat.TimeLeft(clock) <= network.leaveWarning;
                int bars = sat.Bars(clock);
                for (int b = 0; b < 4; b++)
                    card.bars[b].style.backgroundColor = b < bars ? (bars == 1 ? Alarm : Text) : new Color(1, 1, 1, 0.12f);

                card.status.text = inRange ? (leaving ? "LEAVING RANGE" : "IN RANGE") : "OUT OF RANGE";
                card.status.style.color = !inRange ? Muted : leaving ? Alarm : Good;
                card.timer.text = inRange ? $"{Clock(sat.TimeLeft(clock))} left" : $"back in {Clock(sat.TimeToReturn(clock))}";
                card.fill.style.width = Length.Percent(inRange ? (1 - sat.WindowProgress(clock)) * 100 : 0);
                card.fill.style.backgroundColor = leaving ? Alarm : Good;
                card.action.text = active ? "[ACTIVE]" : "[USE]";
                card.action.style.color = active ? Color.white : Muted;
                card.name.style.color = inRange ? Text : Muted;
                card.root.style.backgroundColor = active ? new Color(1, 1, 1, 0.07f) : Color.clear;
                var point = SatellitePoint(i);
                mapLabels[i].style.left = point.x + 8;
                mapLabels[i].style.top = point.y - 7;
                mapLabels[i].style.color = active ? Color.white : inRange ? Text : Muted;
            }
            var rivalPoint = RivalPoint();
            rivalLabel.style.left = rivalPoint.x + 7;
            rivalLabel.style.top = rivalPoint.y - 7;
            RefreshThreat();

            if (link && link.robot)
            {
                link.RefreshUplink();
                bool ok = link.Uplink == UplinkStatus.Ok;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                robotStatus.text = ok
                    ? $"ROBOT SIGNAL: OK via {(link.ViaRelay ? "BASE RELAY" : network.Active.name)}   Q {Mathf.RoundToInt(link.UplinkQuality * 100)}%   LOSS {link.ExpectedLoss.ToString("0.0", inv)}%"
                    : $"ROBOT SIGNAL: NONE - {ScavengerFeedHud.Reason(link.Uplink)}";
                robotStatus.style.color = ok && link.UplinkQuality >= 0.4f ? Good : Alarm;
            }
            else robotStatus.text = "ROBOT SIGNAL: --";
        }

        void RefreshThreat()
        {
            string rival = network.rivalName;
            switch (network.Rival)
            {
                case RivalPhase.Incoming:
                    var target = network.satellites[network.RivalTarget].name;
                    threatText.text = $"WARNING: rival {rival} is jamming CH{network.CarrierChannel + 1} via {target}  {Mathf.RoundToInt(network.RivalProgress * 100)}%\nIn the robot feed: [H] patch panel, retune to a free channel, LOCK.";
                    threatText.style.color = Alarm;
                    break;
                case RivalPhase.Hijacked:
                    threatText.text = $"HIJACKED: {rival} took over {network.satellites[network.RivalTarget].name}.\nNo picture until the carrier is re-locked ([H] in the robot feed).";
                    threatText.style.color = Alarm;
                    break;
                default:
                    threatText.text = network.Hopping
                        ? $"RETUNING... {Mathf.CeilToInt(network.HopTimeLeft)}s"
                        : $"THREATS: none. {rival} (rival) on outer orbit.";
                    threatText.style.color = network.Hopping ? Text : Muted;
                    break;
            }
            Border(threat, network.Rival != RivalPhase.None ? Alarm : new Color(1, 1, 1, 0.12f));
        }

        static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        static VisualElement Box() => new VisualElement { pickingMode = PickingMode.Ignore };

        static void Border(VisualElement element, Color color)
        {
            var s = element.style;
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = color;
        }

        static Label MakeLabel(string value, int size, Color color)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            return label;
        }

        static void DrawIcon(MeshGenerationContext context)
        {
            var rect = context.visualElement.contentRect;
            float u = Mathf.Min(rect.width, rect.height) / 62f;
            var c = rect.center;
            var p = context.painter2D;
            p.strokeColor = Color.white;
            p.fillColor = Color.white;
            p.lineWidth = 2.5f * u;
            Rect(p, c, new Vector2(12, 12) * u, true);
            Rect(p, c + new Vector2(-19, 0) * u, new Vector2(18, 9) * u, false);
            Rect(p, c + new Vector2(19, 0) * u, new Vector2(18, 9) * u, false);
            p.BeginPath();
            p.MoveTo(c + new Vector2(-6, 0) * u);
            p.LineTo(c + new Vector2(-10, 0) * u);
            p.MoveTo(c + new Vector2(6, 0) * u);
            p.LineTo(c + new Vector2(10, 0) * u);
            p.MoveTo(c + new Vector2(0, -6) * u);
            p.LineTo(c + new Vector2(0, -16) * u);
            p.Stroke();
            p.BeginPath();
            p.Arc(c + new Vector2(0, -22) * u, 7 * u, 20, 160);
            p.Stroke();
        }

        static void Rect(Painter2D p, Vector2 center, Vector2 size, bool fill)
        {
            var half = size * 0.5f;
            p.BeginPath();
            p.MoveTo(center + new Vector2(-half.x, -half.y));
            p.LineTo(center + new Vector2(half.x, -half.y));
            p.LineTo(center + new Vector2(half.x, half.y));
            p.LineTo(center + new Vector2(-half.x, half.y));
            p.ClosePath();
            if (fill) p.Fill();
            else p.Stroke();
        }
    }
}
