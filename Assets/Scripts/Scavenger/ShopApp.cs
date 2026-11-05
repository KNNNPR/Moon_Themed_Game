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
        [Tooltip("Shop list pixels per mouse-wheel notch.")]
        [Min(1f)] public float scrollPixelsPerStep = 120f;

        internal static readonly Color Text = new Color(0.94f, 0.96f, 0.97f);
        internal static readonly Color Muted = new Color(0.5f, 0.56f, 0.6f);
        static readonly Color Good = new Color(0.55f, 1f, 0.6f);
        static readonly Color Alarm = new Color(1f, 0.38f, 0.3f);
        internal static readonly Color Edge = new Color(1, 1, 1, 0.12f);

        sealed class Card
        {
            public Upgrade upgrade;
            public VisualElement root, preview, buy;
            public Label title, description, level, value, next, buyLabel;
            public Wireframe wireframe;
            public float yaw;
            public bool moonFinder, installed;
        }

        internal sealed class Wireframe
        {
            public Vector3[] from, to;
        }

        PcMonitorTerminal terminal;
        VisualElement window, close;
        Label heading, balance, status;
        readonly List<Card> cards = new List<Card>();
        ShopCatalog catalog;
        MoonFinderContent moonFinder;
        MoonFinderApp moonFinderApp;
        ScrollView listings;
        float statusUntil;

        public bool IsOpen => window != null && window.style.display == DisplayStyle.Flex;

        void Awake() => terminal = GetComponent<PcMonitorTerminal>();

        void OnEnable() => Loc.Changed += Refresh;

        void OnDisable() => Loc.Changed -= Refresh;

        void Update()
        {
            if(MoonThemedGame.Interactions.DeveloperConsole.BlockGameplayInput)return;
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

#if ENABLE_INPUT_SYSTEM
            if(pointer && Mouse.current!=null && listings.worldBound.Contains(point))
            {
                float wheel=Mouse.current.scroll.ReadValue().y;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                if(InputSystem.settings.scrollDeltaBehavior!=InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms)
                    wheel/=120f;
#endif
                float offset=Mathf.Clamp(listings.scrollOffset.y-wheel*scrollPixelsPerStep,0,Mathf.Max(0,listings.verticalScroller.highValue));
                listings.scrollOffset=new Vector2(0,offset);
            }
#endif

            close.style.color = pointer && close.worldBound.Contains(point) ? Color.white : Muted;
            foreach (var card in cards)
            {
                bool hover = pointer && card.root.worldBound.Contains(point);
                card.root.style.borderLeftColor = card.root.style.borderRightColor = card.root.style.borderTopColor = card.root.style.borderBottomColor = hover ? Text : Edge;
                bool canBuy = card.moonFinder ? moonFinder && moonFinderApp && !moonFinderApp.Purchased && terminal.Credits>=moonFinder.price : !RobotUpgrades.IsMax(card.upgrade) && terminal.Credits >= RobotUpgrades.Price(card.upgrade);
                card.buy.style.backgroundColor = hover && canBuy && card.buy.worldBound.Contains(point) ? new Color(1, 1, 1, 0.18f) : Color.clear;
                card.yaw += Time.deltaTime * (hover ? 60 : 12);
                card.preview.MarkDirtyRepaint();
            }
            balance.text = Loc.F("BALANCE {0} CR", terminal.Credits);
            if (Time.time > statusUntil) status.text = "";
        }

        internal static bool Clicked()
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
            if(!listings.contentViewport.worldBound.Contains(point))return;
            foreach (var card in cards)
            {
                if (!card.buy.worldBound.Contains(point)) continue;
                Buy(card);
                return;
            }
        }

        void Buy(Card card)
        {
            if(card.moonFinder)
            {
                if(moonFinderApp && moonFinderApp.Purchased){Say(Loc.T("[INSTALLED]"),Muted);return;}
                if(!moonFinder || !moonFinderApp || !moonFinderApp.Purchase()){Say(Loc.F("NOT ENOUGH CREDITS - NEED {0} CR",moonFinder?moonFinder.price:40),Alarm);return;}
                Say(Loc.Russian?"MoonFinder установлен на рабочем столе.":"MoonFinder installed on your desktop.",Good);Refresh();return;
            }
            var upgrade = card.upgrade;
            if (RobotUpgrades.IsMax(upgrade)) { Say(Loc.F("{0} ALREADY AT MAX", RobotUpgrades.Title(upgrade)), Muted); return; }
            int price = RobotUpgrades.Price(upgrade);
            if (!RobotUpgrades.TryBuy(upgrade, terminal)) { Say(Loc.F("NOT ENOUGH CREDITS - NEED {0} CR", price), Alarm); return; }
            Say(Loc.F("INSTALLED: {0} MK{1}  -{2} CR", RobotUpgrades.Title(upgrade), RobotUpgrades.Level(upgrade) + 1, price), Good);
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
            GetComponent<MoonFinderApp>()?.Close();
            if (TryGetComponent(out RepairMessagesApp messages)) messages.Close();
            window.BringToFront();
            terminal.Root.Q("screen-cursor")?.BringToFront();
            Refresh();
        }

        // Covers the desktop right of the icon column (SATELLITES and CREDIT included) so nothing shows through.
        void Build()
        {
            catalog = Resources.Load<ShopCatalog>("ShopCatalog");
            moonFinder=Resources.Load<MoonFinderContent>("MoonFinderContent");
            moonFinderApp=GetComponent<MoonFinderApp>();
            var cursor = terminal.Root.Q("screen-cursor");
            var desktop = cursor != null ? cursor.parent : terminal.Root;
            int index = cursor != null ? desktop.IndexOf(cursor) : desktop.childCount;

            window = Box();
            window.name = "shop-window";
            var s = window.style;
            s.position = Position.Absolute;
            s.left = 196;
            s.top = 8;
            s.width = 756;
            s.backgroundColor = new Color(0.02f, 0.04f, 0.055f, 1);
            Border(window, Muted);
            s.paddingLeft = s.paddingRight = 10;
            s.paddingTop = s.paddingBottom = 6;
            s.overflow = Overflow.Hidden;
            s.display = DisplayStyle.None;

            var title = Box();
            title.style.flexDirection = FlexDirection.Row;
            title.style.alignItems = Align.Center;
            title.style.marginBottom = 6;
            heading = MakeLabel("", 18, Text);
            heading.style.flexGrow = 1;
            title.Add(heading);
            balance = MakeLabel("", 15, Good);
            balance.style.marginRight = 16;
            title.Add(balance);
            close = MakeLabel("[X]", 18, Muted);
            title.Add(close);
            window.Add(title);

            var grid = Box();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.SpaceBetween;
            foreach (var upgrade in RobotUpgrades.All) grid.Add(BuildCard(upgrade));
            grid.Add(BuildCard(default,true));
            listings=new ScrollView(ScrollViewMode.Vertical){name="shop-listings",pickingMode=PickingMode.Ignore,verticalScrollerVisibility=ScrollerVisibility.Hidden};
            listings.style.height=418;listings.Add(grid);window.Add(listings);

            status = MakeLabel("", 14, Text);
            status.style.height = 18;
            window.Add(status);
            desktop.Insert(index, window);
        }

        VisualElement BuildCard(Upgrade upgrade,bool moonFinderCard=false)
        {
            var card = new Card { upgrade = upgrade, moonFinder=moonFinderCard, root = Box(), yaw = 30 + cards.Count * 47 };
            card.root.name=moonFinderCard?"shop-moonfinder":"shop-"+upgrade;
            cards.Add(card);
            var model = moonFinderCard ? (moonFinder?moonFinder.shopModel:null) : catalog ? catalog.Model(upgrade) : null;
            card.wireframe = model ? BuildWireframe(model) : null;

            var root = card.root;
            root.style.width = 362;
            root.style.height = 136;
            root.style.marginBottom = 6;
            root.style.flexDirection = FlexDirection.Row;
            root.style.paddingLeft = root.style.paddingRight = 6;
            root.style.paddingTop = root.style.paddingBottom = 6;
            root.style.overflow = Overflow.Hidden;
            Border(root, Edge);

            card.preview = Box();
            card.preview.style.width = card.preview.style.height = 122;
            card.preview.style.flexShrink = 0;
            card.preview.style.backgroundColor = new Color(0, 0, 0, 0.4f);
            Border(card.preview, Edge);
            card.preview.generateVisualContent += context => DrawWireframe(context, card);
            root.Add(card.preview);

            var column = Box();
            column.style.flexGrow = 1;
            column.style.flexShrink = 1;
            column.style.minWidth = 0;
            column.style.marginLeft = 10;
            column.style.overflow = Overflow.Hidden;
            card.title = Line(RobotUpgrades.Title(upgrade), 15, Text);
            column.Add(card.title);
            var description = card.description = MakeLabel(RobotUpgrades.Description(upgrade), 11, Muted);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.height = 28;
            description.style.marginTop = 3;
            description.style.overflow = Overflow.Hidden;
            column.Add(description);
            card.level = Line("", 13, Text);
            card.level.style.marginTop = 2;
            column.Add(card.level);
            card.value = Line("", 12, Muted);
            column.Add(card.value);
            card.next = Line("", 12, Good);
            column.Add(card.next);
            var spacer = Box();
            spacer.style.flexGrow = 1;
            column.Add(spacer);
            card.buy = Box();
            card.buy.style.alignSelf = Align.FlexStart;
            card.buy.style.paddingLeft = card.buy.style.paddingRight = 4;
            card.buyLabel = Line("", 14, Text);
            card.buy.Add(card.buyLabel);
            column.Add(card.buy);
            root.Add(column);
            return root;
        }

        static Label Line(string value, int size, Color color)
        {
            var label = MakeLabel(value, size, color);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            return label;
        }

        void Refresh()
        {
            if (heading == null) return;
            heading.text = Loc.Russian?"СКЛАД // ДЕТАЛИ И ПРОГРАММЫ":"DEPOT // PARTS & SOFTWARE";
            foreach (var card in cards)
            {
                if(card.moonFinder)
                {
                    card.installed=moonFinderApp && moonFinderApp.Purchased;
                    card.title.text="MoonFinder";card.description.text=moonFinder?moonFinder.shopDescription.Value:"";
                    card.level.text=Loc.Russian?"ПРИЛОЖЕНИЕ ДЛЯ ПК":"DESKTOP APPLICATION";card.value.text=Loc.Russian?"Одна покупка":"One-time purchase";card.next.text="";
                    card.buyLabel.text=card.installed?Loc.T("[INSTALLED]"):Loc.F("[BUY  {0} CR]",moonFinder?moonFinder.price:40);
                    card.buyLabel.style.color=card.installed?Good:terminal.Credits>=(moonFinder?moonFinder.price:40)?Text:Alarm;
                    continue;
                }
                var upgrade = card.upgrade;
                int level = RobotUpgrades.Level(upgrade), max = RobotUpgrades.MaxLevel(upgrade);
                bool full = RobotUpgrades.IsMax(upgrade);
                card.installed=level>0;
                card.title.text = RobotUpgrades.Title(upgrade);
                card.description.text = RobotUpgrades.Description(upgrade);
                card.level.text = Loc.F("LVL {0}  {1}/{2}", new string('#', level) + new string('.', max - level), level, max);
                card.value.text = Loc.F("NOW   {0}", RobotUpgrades.Value(upgrade, level));
                card.next.text = Loc.F("NEXT  {0}", full ? "--" : RobotUpgrades.Value(upgrade, level + 1));
                if (full)
                {
                    card.buyLabel.text = Loc.T("[INSTALLED]");
                    card.buyLabel.style.color = Good;
                }
                else
                {
                    int price = RobotUpgrades.Price(upgrade);
                    card.buyLabel.text = Loc.F("[BUY  {0} CR]", price);
                    card.buyLabel.style.color = terminal.Credits >= price ? Text : Alarm;
                }
            }
        }

        // Feature edges only (silhouette creases and open borders), normalised to a unit sphere.
        internal static Wireframe BuildWireframe(GameObject model, System.Predicate<Transform> include = null)
        {
            var from = new List<Vector3>();
            var to = new List<Vector3>();
            var root = model.transform.worldToLocalMatrix;
            var sources = new List<(Mesh mesh, Transform transform)>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (include == null || include(filter.transform)) sources.Add((filter.sharedMesh, filter.transform));
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (include == null || include(skinned.transform)) sources.Add((skinned.sharedMesh, skinned.transform));
            foreach (var (mesh, transform) in sources)
            {
                if (!mesh || !mesh.isReadable) continue;
                var matrix = root * transform.localToWorldMatrix;
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
            bool installed = card.moonFinder ? card.installed : RobotUpgrades.Level(card.upgrade) > 0;
            DrawWireframe(context, card.wireframe, card.yaw,
                installed ? new Color(Good.r, Good.g, Good.b, 0.85f) : new Color(Text.r, Text.g, Text.b, 0.75f));
        }

        internal static void DrawWireframe(MeshGenerationContext context, Wireframe frame, float yaw, Color color)
        {
            var rect = context.visualElement.contentRect;
            var p = context.painter2D;
            var center = rect.center;
            float size = Mathf.Min(rect.width, rect.height) * 0.45f;
            if (frame == null)
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
            var rotation = Quaternion.Euler(22, yaw, 0);
            p.strokeColor = color;
            p.lineWidth = 1;
            p.BeginPath();
            for (int i = 0; i < frame.from.Length; i++)
            {
                var a = rotation * frame.from[i];
                var b = rotation * frame.to[i];
                p.MoveTo(center + new Vector2(a.x, -a.y) * size);
                p.LineTo(center + new Vector2(b.x, -b.y) * size);
            }
            p.Stroke();
        }

        internal static VisualElement Box() => new VisualElement { pickingMode = PickingMode.Ignore };

        internal static void Border(VisualElement element, Color color)
        {
            var s = element.style;
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = color;
        }

        internal static Label MakeLabel(string value, int size, Color color)
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
