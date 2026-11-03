using MoonThemedGame.Computer;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // SAT-LINK desktop app: which satellite the robot's signal goes through, and when each one is in range.
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
        VisualElement icon, window, close;
        Card[] cards;
        Label robotStatus;
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

            bool pointer = terminal.IsFocused && terminal.PointerActive;
            var point = terminal.PointerPosition;
            icon.EnableInClassList("pointer-over", pointer && icon.worldBound.Contains(point));
            if (IsOpen)
            {
                foreach (var card in cards)
                    card.root.style.borderLeftColor = card.root.style.borderRightColor = card.root.style.borderTopColor = card.root.style.borderBottomColor =
                        pointer && card.root.worldBound.Contains(point) ? Text : new Color(1, 1, 1, 0.12f);
                close.style.color = pointer && close.worldBound.Contains(point) ? Color.white : Muted;
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
            s.left = 372;
            s.top = 36;
            s.width = 440;
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
            var help = MakeLabel("The robot's signal goes through ONE satellite.\nPick one that is IN RANGE. In the robot: keys 1 / 2 / 3.", 12, Muted);
            help.style.marginTop = 4;
            help.style.marginBottom = 8;
            help.style.whiteSpace = WhiteSpace.Normal;
            window.Add(help);

            cards = new Card[network.satellites.Count];
            for (int i = 0; i < cards.Length; i++) window.Add(BuildCard(i));

            robotStatus = MakeLabel("", 14, Text);
            robotStatus.style.marginTop = 8;
            robotStatus.style.whiteSpace = WhiteSpace.Normal;
            window.Add(robotStatus);
            desktop.Insert(index, window);
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
            }

            if (link && link.robot)
            {
                link.RefreshUplink();
                bool ok = link.Uplink == UplinkStatus.Ok;
                robotStatus.text = ok
                    ? $"ROBOT SIGNAL: OK via {network.Active.name}"
                    : $"ROBOT SIGNAL: NONE - {ScavengerFeedHud.Reason(link.Uplink)}";
                robotStatus.style.color = ok ? Good : Alarm;
            }
            else robotStatus.text = "ROBOT SIGNAL: --";
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
