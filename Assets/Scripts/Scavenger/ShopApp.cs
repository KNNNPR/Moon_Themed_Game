using System.Collections.Generic;
using MoonThemedGame.Computer;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // SHOP desktop app: robot upgrades with a rotating wireframe of each part.
    [DisallowMultipleComponent]
    public sealed class ShopApp : MonoBehaviour
    {
        const string ShopIcon = "shop";

        static readonly Color Text = new Color(0.94f, 0.96f, 0.97f);
        static readonly Color Muted = new Color(0.5f, 0.56f, 0.6f);
        static readonly Color Good = new Color(0.55f, 1f, 0.6f);
        static readonly Color Alarm = new Color(1f, 0.38f, 0.3f);
        static readonly Color Edge = new Color(1, 1, 1, 0.12f);

        sealed class Card
        {
            public Upgrade upgrade;
            public VisualElement root, preview, buy;
            public Label level, value, buyLabel;
            public Wireframe wireframe;
            public float yaw;
        }

        sealed class Wireframe
        {
            public Vector3[] from, to;
        }

        PcMonitorTerminal terminal;
        VisualElement window, close;
        Label balance, status;
        readonly List<Card> cards = new List<Card>();
        ShopCatalog catalog;
        float statusUntil;

        public bool IsOpen => window != null && window.style.display == DisplayStyle.Flex;

        void Awake() => terminal = GetComponent<PcMonitorTerminal>();

        void Update()
        {
            if (!terminal) return;
            if (window == null && terminal.Root != null) Build();
            if (window == null) return;

            bool pointer = terminal.IsFocused && terminal.PointerActive && PcLogin.IsUnlocked(terminal);
            var point = terminal.PointerPosition;
            if (pointer && Clicked())
            {
                if (terminal.HoveredIcon == ShopIcon) SetOpen(!IsOpen);
                else if (IsOpen) HandleClick(point);
            }
            if (!IsOpen) return;

            close.style.color = pointer && close.worldBound.Contains(point) ? Color.white : Muted;
            foreach (var card in cards)
            {
                bool hover = pointer && card.root.worldBound.Contains(point);
                card.root.style.borderLeftColor = card.root.style.borderRightColor = card.root.style.borderTopColor = card.root.style.borderBottomColor = hover ? Text : Edge;
                bool canBuy = !RobotUpgrades.IsMax(card.upgrade) && terminal.Credits >= RobotUpgrades.Price(card.upgrade);
                card.buy.style.backgroundColor = hover && canBuy && card.buy.worldBound.Contains(point) ? new Color(1, 1, 1, 0.18f) : Color.clear;
                card.yaw += Time.deltaTime * (hover ? 60 : 12);
                card.preview.MarkDirtyRepaint();
            }
            balance.text = $"BALANCE {terminal.Credits} CR";
            if (Time.time > statusUntil) status.text = "";
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
            if (close.worldBound.Contains(point)) { SetOpen(false); return; }
            foreach (var card in cards)
            {
                if (!card.buy.worldBound.Contains(point)) continue;
                Buy(card);
                return;
            }
        }

        void Buy(Card card)
        {
            var upgrade = card.upgrade;
            if (RobotUpgrades.IsMax(upgrade)) { Say($"{RobotUpgrades.Title(upgrade)} ALREADY AT MAX", Muted); return; }
            int price = RobotUpgrades.Price(upgrade);
            if (!RobotUpgrades.TryBuy(upgrade, terminal)) { Say($"NOT ENOUGH CREDITS - NEED {price} CR", Alarm); return; }
            Say($"INSTALLED: {RobotUpgrades.Title(upgrade)} MK{RobotUpgrades.Level(upgrade) + 1}  -{price} CR", Good);
            Refresh();
        }

        void Say(string text, Color color)
        {
            status.text = text;
            status.style.color = color;
            statusUntil = Time.time + 3;
        }

        public void Close()
        {
            if (window != null) SetOpen(false);
        }

        void SetOpen(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            if (TryGetComponent(out SatelliteConsoleApp satellites)) satellites.Close();
            window.BringToFront();
            Refresh();
        }

        void Build()
        {
            catalog = Resources.Load<ShopCatalog>("ShopCatalog");
            var cursor = terminal.Root.Q("screen-cursor");
            var desktop = cursor != null ? cursor.parent : terminal.Root;
            int index = cursor != null ? desktop.IndexOf(cursor) : desktop.childCount;

            window = Box();
            window.name = "shop-window";
            var s = window.style;
            s.position = Position.Absolute;
            s.left = 300;
            s.top = 14;
            s.width = 640;
            s.backgroundColor = new Color(0.02f, 0.04f, 0.055f, 0.97f);
            Border(window, Muted);
            s.paddingLeft = s.paddingRight = 10;
            s.paddingTop = s.paddingBottom = 8;
            s.display = DisplayStyle.None;

            var title = Box();
            title.style.flexDirection = FlexDirection.Row;
            title.style.justifyContent = Justify.SpaceBetween;
            title.Add(MakeLabel("PARTS DEPOT // ROBOT UPGRADES", 18, Text));
            close = MakeLabel("[X]", 18, Muted);
            title.Add(close);
            window.Add(title);

            var info = Box();
            info.style.flexDirection = FlexDirection.Row;
            info.style.justifyContent = Justify.SpaceBetween;
            info.style.marginTop = 2;
            info.style.marginBottom = 6;
            info.Add(MakeLabel("Parts are installed on the robot right away.", 12, Muted));
            balance = MakeLabel("", 14, Good);
            info.Add(balance);
            window.Add(info);

            var grid = Box();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.SpaceBetween;
            foreach (var upgrade in RobotUpgrades.All) grid.Add(BuildCard(upgrade));
            window.Add(grid);

            status = MakeLabel("", 14, Text);
            status.style.marginTop = 2;
            status.style.height = 18;
            window.Add(status);
            desktop.Insert(index, window);
        }

        VisualElement BuildCard(Upgrade upgrade)
        {
            var card = new Card { upgrade = upgrade, root = Box(), yaw = 30 + cards.Count * 47 };
            cards.Add(card);
            var model = catalog ? catalog.Model(upgrade) : null;
            card.wireframe = model ? BuildWireframe(model) : null;

            var root = card.root;
            root.style.width = 306;
            root.style.height = 118;
            root.style.marginBottom = 6;
            root.style.flexDirection = FlexDirection.Row;
            root.style.paddingLeft = root.style.paddingRight = 6;
            root.style.paddingTop = root.style.paddingBottom = 6;
            Border(root, Edge);

            card.preview = Box();
            card.preview.style.width = card.preview.style.height = 104;
            card.preview.style.flexShrink = 0;
            card.preview.style.backgroundColor = new Color(0, 0, 0, 0.4f);
            Border(card.preview, Edge);
            card.preview.generateVisualContent += context => DrawWireframe(context, card);
            root.Add(card.preview);

            var column = Box();
            column.style.flexGrow = 1;
            column.style.marginLeft = 8;
            column.style.justifyContent = Justify.SpaceBetween;
            var name = MakeLabel(RobotUpgrades.Title(upgrade), 15, Text);
            column.Add(name);
            var description = MakeLabel(RobotUpgrades.Description(upgrade), 11, Muted);
            description.style.whiteSpace = WhiteSpace.Normal;
            column.Add(description);
            card.level = MakeLabel("", 13, Text);
            column.Add(card.level);
            card.value = MakeLabel("", 12, Muted);
            column.Add(card.value);
            card.buy = Box();
            card.buy.style.alignSelf = Align.FlexStart;
            card.buy.style.paddingLeft = card.buy.style.paddingRight = 4;
            card.buyLabel = MakeLabel("", 14, Text);
            card.buy.Add(card.buyLabel);
            column.Add(card.buy);
            root.Add(column);
            return root;
        }

        void Refresh()
        {
            foreach (var card in cards)
            {
                var upgrade = card.upgrade;
                int level = RobotUpgrades.Level(upgrade), max = RobotUpgrades.MaxLevel(upgrade);
                card.level.text = "LVL " + new string('#', level) + new string('.', max - level) + $"  {level}/{max}";
                card.value.text = RobotUpgrades.IsMax(upgrade)
                    ? RobotUpgrades.Value(upgrade, level)
                    : $"{RobotUpgrades.Value(upgrade, level)}  ->  {RobotUpgrades.Value(upgrade, level + 1)}";
                if (RobotUpgrades.IsMax(upgrade))
                {
                    card.buyLabel.text = "[INSTALLED]";
                    card.buyLabel.style.color = Good;
                }
                else
                {
                    int price = RobotUpgrades.Price(upgrade);
                    card.buyLabel.text = $"[BUY  {price} CR]";
                    card.buyLabel.style.color = terminal.Credits >= price ? Text : Alarm;
                }
            }
        }

        // Feature edges only (silhouette creases and open borders), normalised to a unit sphere.
        static Wireframe BuildWireframe(GameObject model)
        {
            var from = new List<Vector3>();
            var to = new List<Vector3>();
            var root = model.transform.worldToLocalMatrix;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (!mesh || !mesh.isReadable) continue;
                var matrix = root * filter.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var bounds = mesh.bounds;
                float cell = Mathf.Max(bounds.size.magnitude / 2000f, 1e-5f);
                var welded = new Dictionary<Vector3Int, int>();
                var ids = new int[vertices.Length];
                var points = new List<Vector3>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    var key = Vector3Int.RoundToInt(vertices[i] / cell);
                    if (!welded.TryGetValue(key, out int id)) { id = points.Count; welded[key] = id; points.Add(matrix.MultiplyPoint3x4(vertices[i])); }
                    ids[i] = id;
                }
                var edges = new Dictionary<long, (Vector3 normal, int count, bool crease)>();
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = ids[triangles[t]], b = ids[triangles[t + 1]], c = ids[triangles[t + 2]];
                    var normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                    if (normal.sqrMagnitude < 1e-12f) continue;
                    normal.Normalize();
                    AddEdge(edges, a, b, normal);
                    AddEdge(edges, b, c, normal);
                    AddEdge(edges, c, a, normal);
                }
                foreach (var pair in edges)
                {
                    if (pair.Value.count != 1 && !pair.Value.crease) continue;
                    from.Add(points[(int)(pair.Key >> 32)]);
                    to.Add(points[(int)(pair.Key & 0xffffffff)]);
                }
            }
            if (from.Count == 0) return null;

            const int maxEdges = 700;
            if (from.Count > maxEdges)
            {
                var order = new List<int>(from.Count);
                for (int i = 0; i < from.Count; i++) order.Add(i);
                order.Sort((x, y) => (to[y] - from[y]).sqrMagnitude.CompareTo((to[x] - from[x]).sqrMagnitude));
                var keptFrom = new List<Vector3>(maxEdges);
                var keptTo = new List<Vector3>(maxEdges);
                for (int i = 0; i < maxEdges; i++) { keptFrom.Add(from[order[i]]); keptTo.Add(to[order[i]]); }
                from = keptFrom;
                to = keptTo;
            }
            var all = new Bounds(from[0], Vector3.zero);
            for (int i = 0; i < from.Count; i++) { all.Encapsulate(from[i]); all.Encapsulate(to[i]); }
            float scale = 1 / Mathf.Max(all.extents.magnitude, 1e-5f);
            var frame = new Wireframe { from = new Vector3[from.Count], to = new Vector3[to.Count] };
            for (int i = 0; i < from.Count; i++)
            {
                frame.from[i] = (from[i] - all.center) * scale;
                frame.to[i] = (to[i] - all.center) * scale;
            }
            return frame;
        }

        static void AddEdge(Dictionary<long, (Vector3 normal, int count, bool crease)> edges, int a, int b, Vector3 normal)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (edges.TryGetValue(key, out var edge))
                edges[key] = (edge.normal, edge.count + 1, edge.crease || Vector3.Dot(edge.normal, normal) < 0.85f);
            else edges[key] = (normal, 1, false);
        }

        static void DrawWireframe(MeshGenerationContext context, Card card)
        {
            var rect = context.visualElement.contentRect;
            var p = context.painter2D;
            var center = rect.center;
            float size = Mathf.Min(rect.width, rect.height) * 0.45f;
            if (card.wireframe == null)
            {
                p.strokeColor = Muted;
                p.lineWidth = 1;
                p.BeginPath();
                p.MoveTo(center - new Vector2(size, size) * 0.5f);
                p.LineTo(center + new Vector2(size, size) * 0.5f);
                p.MoveTo(center + new Vector2(size, -size) * 0.5f);
                p.LineTo(center + new Vector2(-size, size) * 0.5f);
                p.Stroke();
                return;
            }
            var rotation = Quaternion.Euler(22, card.yaw, 0);
            bool installed = RobotUpgrades.Level(card.upgrade) > 0;
            p.strokeColor = installed ? new Color(Good.r, Good.g, Good.b, 0.85f) : new Color(Text.r, Text.g, Text.b, 0.75f);
            p.lineWidth = 1;
            p.BeginPath();
            var frame = card.wireframe;
            for (int i = 0; i < frame.from.Length; i++)
            {
                var a = rotation * frame.from[i];
                var b = rotation * frame.to[i];
                p.MoveTo(center + new Vector2(a.x, -a.y) * size);
                p.LineTo(center + new Vector2(b.x, -b.y) * size);
            }
            p.Stroke();
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
    }
}
