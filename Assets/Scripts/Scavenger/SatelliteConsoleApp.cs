using MoonThemedGame.Computer;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // SAT-LINK desktop app: shows the satellites' passes and picks the one the robot relays through.
    [DisallowMultipleComponent]
    public sealed class SatelliteConsoleApp : MonoBehaviour
    {
        static readonly Color Text = new Color(0.94f, 0.96f, 0.97f);
        static readonly Color Muted = new Color(0.58f, 0.64f, 0.67f);
        static readonly Color Alarm = new Color(1f, 0.38f, 0.3f);

        PcMonitorTerminal terminal;
        ScavengerLink link;
        SatelliteNetwork network;
        VisualElement icon, window, close, plot;
        VisualElement[] rows;
        Label[] rowLabels;
        Label footer;
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
                for (int i = 0; i < rows.Length; i++)
                    rows[i].style.backgroundColor = pointer && rows[i].worldBound.Contains(point) ? new Color(1, 1, 1, 0.08f) : Color.clear;
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
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].worldBound.Contains(point)) { network.Select(i); Refresh(); return; }
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

            icon = new VisualElement { name = "satellites" };
            icon.AddToClassList("desktop-icon");
            icon.style.position = Position.Absolute;
            icon.style.left = 204;
            icon.style.top = 24;
            var art = new VisualElement();
            art.AddToClassList("icon-image");
            art.generateVisualContent += DrawIcon;
            icon.Add(art);
            var label = new Label("SATELLITES");
            label.AddToClassList("icon-label");
            icon.Add(label);
            icon.pickingMode = PickingMode.Ignore;
            desktop.Insert(index++, icon);

            window = new VisualElement { name = "sat-link" };
            window.pickingMode = PickingMode.Ignore;
            var s = window.style;
            s.position = Position.Absolute;
            s.left = 372;
            s.top = 48;
            s.width = 420;
            s.backgroundColor = new Color(0.02f, 0.04f, 0.055f, 0.97f);
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = Muted;
            s.paddingLeft = s.paddingRight = 12;
            s.paddingTop = s.paddingBottom = 8;
            s.display = DisplayStyle.None;

            var title = Row();
            title.style.justifyContent = Justify.SpaceBetween;
            title.style.marginBottom = 8;
            title.Add(MakeLabel("SAT-LINK CONTROL", 16, Text));
            close = MakeLabel("[X]", 16, Muted);
            title.Add(close);
            window.Add(title);

            var body = Row();
            var list = new VisualElement { pickingMode = PickingMode.Ignore };
            list.style.flexGrow = 1;
            rows = new VisualElement[network.satellites.Count];
            rowLabels = new Label[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                rows[i].style.paddingTop = rows[i].style.paddingBottom = 4;
                rows[i].style.paddingLeft = 4;
                rowLabels[i] = MakeLabel("", 13, Text);
                rows[i].Add(rowLabels[i]);
                list.Add(rows[i]);
            }
            body.Add(list);
            plot = new VisualElement { pickingMode = PickingMode.Ignore };
            plot.style.width = plot.style.height = 128;
            plot.style.marginLeft = 8;
            plot.generateVisualContent += DrawSky;
            body.Add(plot);
            window.Add(body);

            footer = MakeLabel("", 13, Text);
            footer.style.marginTop = 8;
            window.Add(footer);
            window.Add(MakeLabel("CLICK A SATELLITE TO ROUTE THE ROBOT UPLINK", 11, Muted));
            desktop.Insert(index, window);
        }

        static VisualElement Row()
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            return row;
        }

        static Label MakeLabel(string value, int size, Color color)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = 0;
            label.style.paddingLeft = label.style.paddingRight = 0;
            return label;
        }

        void Refresh()
        {
            nextRefresh = Time.time + 0.25f;
            float clock = network.Clock;
            for (int i = 0; i < rows.Length; i++)
            {
                var sat = network.satellites[i];
                bool active = i == network.ActiveIndex;
                bool inView = sat.InView(clock, network.minElevation);
                string marker = active ? "> " : "  ";
                string pass = inView ? $"SET  {Clock(sat.TimeToSet(clock))}" : $"RISE {Clock(sat.TimeToRise(clock))}";
                string position = inView
                    ? $"EL {Mathf.RoundToInt(sat.Elevation(clock)):00}  AZ {Mathf.RoundToInt(sat.Azimuth(clock)):000}"
                    : "EL --  AZ ---";
                rowLabels[i].text = $"{marker}{sat.name,-7} {position}  {pass}";
                rowLabels[i].style.color = active ? Color.white : inView ? Text : Muted;
            }
            if (link && link.robot)
            {
                link.RefreshUplink();
                var status = link.Uplink;
                footer.text = status == UplinkStatus.Ok
                    ? $"ROBOT UPLINK  OK  via {network.Active.name}  Q {Mathf.RoundToInt(link.UplinkQuality * 100)}%"
                    : $"ROBOT UPLINK  LOST  {ScavengerFeedHud.Reason(status)}";
                footer.style.color = status == UplinkStatus.Ok ? Text : Alarm;
            }
            else footer.text = "ROBOT UPLINK  --";
            plot.MarkDirtyRepaint();
        }

        static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        void DrawSky(MeshGenerationContext context)
        {
            var rect = plot.contentRect;
            var center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 4;
            var p = context.painter2D;
            p.lineWidth = 1;
            p.strokeColor = Muted;
            foreach (float r in new[] { 1f, 0.66f, 0.33f })
            {
                p.BeginPath();
                p.Arc(center, radius * r, 0, 360);
                p.Stroke();
            }
            p.BeginPath();
            p.MoveTo(center + Vector2.left * radius);
            p.LineTo(center + Vector2.right * radius);
            p.MoveTo(center + Vector2.up * radius);
            p.LineTo(center + Vector2.down * radius);
            p.Stroke();
            if (network == null) return;
            float clock = network.Clock;
            for (int i = 0; i < network.satellites.Count; i++)
            {
                var sat = network.satellites[i];
                if (!sat.InView(clock, 0)) continue;
                float distance = (90 - sat.Elevation(clock)) / 90 * radius;
                float angle = sat.Azimuth(clock) * Mathf.Deg2Rad;
                var dot = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * distance;
                bool active = i == network.ActiveIndex;
                p.BeginPath();
                p.Arc(dot, active ? 5 : 3.5f, 0, 360);
                if (active) { p.fillColor = Color.white; p.Fill(); }
                else { p.strokeColor = Text; p.Stroke(); }
            }
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
            Box(p, c, new Vector2(12, 12) * u, true);
            Box(p, c + new Vector2(-19, 0) * u, new Vector2(18, 9) * u, false);
            Box(p, c + new Vector2(19, 0) * u, new Vector2(18, 9) * u, false);
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

        static void Box(Painter2D p, Vector2 center, Vector2 size, bool fill)
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
