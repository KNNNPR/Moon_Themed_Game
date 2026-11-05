using System.Collections.Generic;
using MoonThemedGame.Computer;
using MoonThemedGame.Repair;
using UnityEngine;
using UnityEngine.UIElements;
using static MoonThemedGame.Scavenger.ShopApp;

namespace MoonThemedGame.Scavenger
{
    [DisallowMultipleComponent]
    public sealed class RepairMessagesApp : MonoBehaviour
    {
        const string Company = "PALE HORSE CO.";
        const int PageSize = 5;
        static readonly (string part, int count)[][] InitialOrders =
        {
            new[] { ("Wheel", 2), ("Battery", 1) },
            new[] { ("Headlights", 1), ("Window_Robot", 1) },
            new[] { ("Back_Body_Robot", 1), ("Wheel", 3) }
        };

        sealed class Order
        {
            public int[] missingSlots;
            public int[] missingCounts;
            public Wireframe frame;
            public bool completed;
        }

        PcMonitorTerminal terminal;
        RepairRobotDropper dropper;
        RepairRobotTarget delivery;
        Order accepted;
        readonly List<Order> orders = new List<Order>();
        List<RepairRobotTarget.Slot> slots;
        VisualElement desktopRoot, window, preview;
        Label heading, queueHeading, close, customer, parts, description, accept, previous, next;
        readonly Label[] rows = new Label[PageSize];
        readonly VisualElement[] strikes = new VisualElement[PageSize];
        int selected, page;
        float yaw = 30f;

        public bool IsOpen => window != null && window.style.display == DisplayStyle.Flex;

        void Awake() => terminal = GetComponent<PcMonitorTerminal>();
        void OnEnable() => Loc.Changed += Refresh;

        void OnDisable()
        {
            Loc.Changed -= Refresh;
            Close();
        }

        public void Close()
        {
            if (window != null) window.style.display = DisplayStyle.None;
        }

        void Update()
        {
            TrackDelivery();
            if (!terminal || terminal.Root == null) return;
            if (desktopRoot != terminal.Root) Build();

            bool pointer = terminal.IsFocused && terminal.PointerActive && PcLogin.IsUnlocked(terminal);
            Vector2 point = terminal.PointerPosition;
            if (pointer && Clicked())
            {
                if (terminal.HoveredIcon == "messages")
                {
                    if (IsOpen) Close();
                    else Open();
                }
                else if (IsOpen)
                {
                    if (close.worldBound.Contains(point)) Close();
                    else if (accept.worldBound.Contains(point)) Accept();
                    else if (previous.worldBound.Contains(point) && page > 0) ChangePage(-1);
                    else if (next.worldBound.Contains(point) && (page + 1) * PageSize < orders.Count) ChangePage(1);
                    else
                    {
                        for (int i = 0; i < rows.Length; i++)
                        {
                            int index = page * PageSize + i;
                            if (index >= orders.Count || !rows[i].worldBound.Contains(point)) continue;
                            selected = index;
                            Refresh();
                            break;
                        }
                    }
                }
            }
            if (!IsOpen) return;

            close.style.color = pointer && close.worldBound.Contains(point) ? Color.white : Muted;
            for (int i = 0; i < rows.Length; i++)
                Border(rows[i], selected == page * PageSize + i || (pointer && rows[i].worldBound.Contains(point)) ? Text : Edge);
            RefreshAction();
            if (terminal.IsFocused)
            {
                yaw += Time.deltaTime * (pointer && preview.worldBound.Contains(point) ? 60f : 12f);
                preview.MarkDirtyRepaint();
            }
        }

        void Open()
        {
            if (TryGetComponent(out ShopApp shop)) shop.Close();
            if (TryGetComponent(out SatelliteConsoleApp satellites)) satellites.Close();
            if (orders.Count == 0)
            {
                dropper = RepairRobotDropper.Active;
                if (dropper && dropper.gameObject.scene == gameObject.scene && dropper.robotPrefab && dropper.parts != null)
                {
                    slots = RepairRobotTarget.FindSlots(dropper.robotPrefab.transform, dropper.parts);
                    if (slots.Count > 0)
                        for (int i = 0; i < 3; i++) AddOrder();
                }
            }
            window.style.display = DisplayStyle.Flex;
            window.BringToFront();
            desktopRoot.Q("screen-cursor")?.BringToFront();
            Refresh();
        }

        void AddOrder()
        {
            // Keep a stable damage plan for the preview, text and eventual delivery.
            var missing = SelectMissingSlots();
            var counts = new int[dropper.parts.Length];
            var hidden = new List<Transform>(missing.Length);
            foreach (int index in missing)
            {
                counts[slots[index].part]++;
                hidden.Add(slots[index].visual.transform);
            }
            var frame = BuildWireframe(dropper.robotPrefab, mesh =>
            {
                foreach (var part in hidden)
                    if (mesh.IsChildOf(part)) return false;
                return true;
            });
            orders.Add(new Order { missingSlots = missing, missingCounts = counts, frame = frame });
        }

        int[] SelectMissingSlots()
        {
            if (orders.Count < InitialOrders.Length)
            {
                var chosen = new List<int>();
                foreach (var (part, count) in InitialOrders[orders.Count])
                {
                    int remaining = count;
                    for (int i = 0; i < slots.Count && remaining > 0; i++)
                        if (dropper.parts[slots[i].part].prefab.name == part)
                        {
                            chosen.Add(i);
                            remaining--;
                        }
                    if (remaining > 0)
                    {
                        chosen.Clear();
                        break;
                    }
                }
                if (chosen.Count > 0) return chosen.ToArray();
            }
            return RepairDamagePlan.Select(slots.Count, dropper.minimumMissingParts,
                dropper.maximumMissingParts, 7301 + orders.Count * 7919);
        }

        void Accept()
        {
            if (orders.Count == 0 || accepted != null || !dropper || orders[selected].completed) return;
            delivery = dropper.TryDropOrder(orders[selected].missingSlots);
            if (!delivery) return;
            accepted = orders[selected];
            Refresh();
        }

        void TrackDelivery()
        {
            if (accepted == null || (delivery && !delivery.IsRepaired)) return;
            if (delivery)
            {
                accepted.completed = true;
                if (dropper)
                {
                    AddOrder();
                    selected = orders.Count - 1;
                    page = selected / PageSize;
                }
            }
            accepted = null;
            delivery = null;
            Refresh();
        }

        void ChangePage(int direction)
        {
            page += direction;
            selected = page * PageSize;
            Refresh();
        }

        void Build()
        {
            window?.RemoveFromHierarchy();
            desktopRoot = terminal.Root;
            var cursor = desktopRoot.Q("screen-cursor");
            var desktop = cursor != null ? cursor.parent : desktopRoot;
            window = Box();
            window.name = "repair-messages";
            var style = window.style;
            style.position = Position.Absolute;
            style.left = 196;
            style.top = 8;
            style.width = 756;
            style.height = 496;
            style.backgroundColor = new Color(0.02f, 0.04f, 0.055f, 1f);
            style.paddingLeft = style.paddingRight = 10;
            style.paddingTop = style.paddingBottom = 6;
            style.overflow = Overflow.Hidden;
            style.display = DisplayStyle.None;
            Border(window, Muted);

            var title = Box();
            title.style.flexDirection = FlexDirection.Row;
            title.style.justifyContent = Justify.SpaceBetween;
            title.style.marginBottom = 8;
            heading = MakeLabel("", 18, Text);
            close = MakeLabel("[X]", 18, Muted);
            title.Add(heading);
            title.Add(close);
            window.Add(title);

            var body = Box();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            window.Add(body);
            var queue = Box();
            queue.style.width = 220;
            queue.style.flexShrink = 0;
            queue.style.marginRight = 10;
            queueHeading = MakeLabel("", 13, Muted);
            queueHeading.style.marginBottom = 8;
            queue.Add(queueHeading);
            body.Add(queue);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] = MakeLabel("", 15, Text);
                row.style.paddingLeft = 8;
                row.style.height = 42;
                row.style.marginBottom = 6;
                row.style.unityTextAlign = TextAnchor.MiddleLeft;
                Border(row, Edge);
                var strike = strikes[i] = Box();
                strike.style.position = Position.Absolute;
                strike.style.left = 7;
                strike.style.width = 169;
                strike.style.height = 2;
                strike.style.top = 19;
                strike.style.backgroundColor = new Color(0.55f, 1f, 0.6f, 0.75f);
                row.Add(strike);
                queue.Add(row);
            }

            var pager = Box();
            pager.style.flexDirection = FlexDirection.Row;
            pager.style.justifyContent = Justify.SpaceBetween;
            previous = MakeLabel("[ < ]", 15, Text);
            next = MakeLabel("[ > ]", 15, Text);
            pager.Add(previous);
            pager.Add(next);
            queue.Add(pager);

            var details = Box();
            details.style.flexGrow = 1;
            details.style.minWidth = 0;
            details.style.paddingLeft = details.style.paddingRight = 10;
            details.style.paddingTop = details.style.paddingBottom = 8;
            Border(details, Edge);
            body.Add(details);
            customer = MakeLabel("", 17, Text);
            details.Add(customer);
            preview = Box();
            preview.style.height = 218;
            preview.style.flexShrink = 0;
            preview.style.marginTop = preview.style.marginBottom = 8;
            preview.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            Border(preview, Edge);
            preview.generateVisualContent += context => DrawWireframe(context, orders.Count > 0 ? orders[selected].frame : null, yaw, Color.white);
            details.Add(preview);
            parts = MakeLabel("", 15, Text);
            parts.style.whiteSpace = WhiteSpace.Normal;
            details.Add(parts);
            description = MakeLabel("", 14, Muted);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginTop = 10;
            details.Add(description);
            accept = MakeLabel("", 15, Text);
            accept.style.marginTop = 10;
            accept.style.alignSelf = Align.FlexStart;
            accept.style.paddingLeft = accept.style.paddingRight = 6;
            accept.style.paddingTop = accept.style.paddingBottom = 3;
            details.Add(accept);
            desktop.Insert(cursor != null ? desktop.IndexOf(cursor) : desktop.childCount, window);
            Refresh();
        }

        void Refresh()
        {
            if (heading == null) return;
            bool ru = Loc.Russian;
            heading.text = ru ? "СООБЩЕНИЯ // ЗАКАЗЫ НА РЕМОНТ" : "MESSAGES // REPAIR ORDERS";
            queueHeading.text = ru ? "ВХОДЯЩИЕ" : "INBOX";
            for (int i = 0; i < rows.Length; i++)
            {
                int index = page * PageSize + i;
                bool present = index < orders.Count;
                rows[i].style.display = present ? DisplayStyle.Flex : DisplayStyle.None;
                if (!present) continue;
                bool done = orders[index].completed;
                rows[i].text = $"{index + 1:00}  {Company}";
                rows[i].style.color = done ? Muted : Text;
                strikes[i].style.display = done ? DisplayStyle.Flex : DisplayStyle.None;
                rows[i].style.backgroundColor = index == selected ? new Color(1, 1, 1, 0.12f) : Color.clear;
            }
            previous.style.visibility = page > 0 ? Visibility.Visible : Visibility.Hidden;
            next.style.visibility = (page + 1) * PageSize < orders.Count ? Visibility.Visible : Visibility.Hidden;
            customer.text = ru ? $"ЗАКАЗЧИК   {Company}" : $"CUSTOMER   {Company}";
            if (orders.Count == 0)
            {
                parts.text = ru ? "РЕМОНТНЫЙ ПОСТ НЕДОСТУПЕН" : "REPAIR BAY OFFLINE";
                description.text = "";
                RefreshAction();
                return;
            }
            var order = orders[selected];
            var items = new List<string>();
            for (int i = 0; i < order.missingCounts.Length; i++)
                if (order.missingCounts[i] > 0)
                    items.Add($"{SalvageField.PartName(dropper.parts[i].prefab)} ×{order.missingCounts[i]}");
            parts.text = (ru ? "НЕДОСТАЮЩИЕ ДЕТАЛИ\n" : "MISSING PARTS\n") + string.Join("  /  ", items);
            description.text = ru
                ? "Съешь ещё этих мягких французских булок, да выпей чаю. Пример короткого описания для проверки шрифта."
                : "Lorem ipsum dolor sit amet, consectetur adipiscing elit. The quick brown fox jumps over the lazy dog.";
            preview.MarkDirtyRepaint();
            RefreshAction();
        }

        void RefreshAction()
        {
            bool ru = Loc.Russian;
            bool hasOrder = orders.Count > 0;
            accept.style.display = hasOrder ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasOrder) return;
            var order = orders[selected];
            bool canAccept = !order.completed && accepted == null && dropper && dropper.CanDropOrder;
            accept.text = order.completed ? (ru ? "[ВЫПОЛНЕНО]" : "[COMPLETE]")
                : order == accepted ? (ru ? "[В РАБОТЕ]" : "[IN PROGRESS]")
                : canAccept ? (ru ? "[ПРИНЯТЬ ЗАКАЗ]" : "[ACCEPT ORDER]")
                : (ru ? "[ПОСТ ЗАНЯТ]" : "[BAY BUSY]");
            Border(accept, canAccept ? Text : Edge);
            accept.style.color = canAccept || order.completed ? new Color(0.55f, 1f, 0.6f) : Muted;
        }
    }
}
