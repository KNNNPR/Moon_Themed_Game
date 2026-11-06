using System;
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
        const int CreditsPerMissingPart = 2;
        static readonly (string part, int count)[][] InitialOrders =
        {
            new[] { ("Wheel", 2), ("Battery", 1) },
            new[] { ("Headlights", 1), ("Window_Robot", 1) },
            new[] { ("Back_Body_Robot", 1), ("Wheel", 3) },
            new[] { ("Battery", 1), ("Headlights", 1), ("Window_Robot", 1) },
            new[] { ("Back_Body_Robot", 1), ("Headlights", 1), ("Wheel", 2) },
            new[] { ("Battery", 1), ("Window_Robot", 1), ("Wheel", 3) }
        };

        sealed class Order
        {
            public int[] missingSlots;
            public int[] missingCounts;
            public Wireframe frame;
            public bool completed;
            public int Reward => missingSlots.Length * CreditsPerMissingPart;
        }

        PcMonitorTerminal terminal;
        RepairRobotDropper dropper;
        RepairRobotTarget delivery;
        Order accepted;
        readonly List<Order> orders = new List<Order>();
        List<RepairRobotTarget.Slot> slots;
        VisualElement desktopRoot, window, preview, endlessSection;
        Label heading, queueHeading, close, customer, parts, description, reward, accept, endlessHeading, endlessRow, reroll;
        readonly Label[] rows = new Label[InitialOrders.Length];
        readonly VisualElement[] strikes = new VisualElement[InitialOrders.Length];
        int selected;
        readonly System.Random endlessRandom = new System.Random();
        int EndlessIndex => InitialOrders.Length;
        bool HasEndless => orders.Count > EndlessIndex;
        float yaw = 30f;

        public bool IsOpen => window != null && window.style.display == DisplayStyle.Flex;
        internal void GetSalvageNeeds(Dictionary<GameObject,int> counts)
        {
            counts.Clear();
            if(accepted==null || !delivery)return;
            delivery.FillMissingParts(counts);
        }

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
            if(MoonThemedGame.Interactions.DeveloperConsole.BlockGameplayInput)return;
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
                    else if (HasEndless && reroll.worldBound.Contains(point)) RerollEndless();
                    else if (HasEndless && endlessRow.worldBound.Contains(point))
                    {
                        selected = EndlessIndex;
                        Refresh();
                    }
                    else
                    {
                        for (int i = 0; i < rows.Length; i++)
                        {
                            int index = i;
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
                Border(rows[i], selected == i || (pointer && rows[i].worldBound.Contains(point)) ? Text : Edge);
            RefreshAction();
            if (terminal.IsFocused)
            {
                yaw += Time.deltaTime * (pointer && preview.worldBound.Contains(point) ? 60f : 12f);
                preview.MarkDirtyRepaint();
            }
        }

        internal void Open()
        {
            if (!terminal || terminal.Root == null) return;
            if (desktopRoot != terminal.Root) Build();
            GetComponent<MoonFinderApp>()?.Close();
            GetComponent<MoneyGetApp>()?.Close();
            if (TryGetComponent(out ShopApp shop)) shop.Close();
            if (TryGetComponent(out SatelliteConsoleApp satellites)) satellites.Close();
            if (orders.Count == 0)
            {
                dropper = RepairRobotDropper.Active;
                if (dropper && dropper.gameObject.scene == gameObject.scene && dropper.robotPrefab && dropper.parts != null)
                {
                    slots = RepairRobotTarget.FindSlots(dropper.robotPrefab.transform, dropper.parts);
                    if (slots.Count > 0)
                        for (int i = 0; i < InitialOrders.Length; i++)
                            orders.Add(BuildOrder(SelectMainSlots(i)));
                }
            }
            window.style.display = DisplayStyle.Flex;
            window.BringToFront();
            desktopRoot.Q("screen-cursor")?.BringToFront();
            Refresh();
        }

        Order BuildOrder(int[] missing)
        {
            // Keep a stable damage plan for the preview, text and eventual delivery.
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
            return new Order { missingSlots = missing, missingCounts = counts, frame = frame };
        }

        int[] SelectMainSlots(int orderIndex)
        {
            var chosen = new List<int>();
            foreach (var (part, count) in InitialOrders[orderIndex])
            {
                int remaining = count;
                for (int i = 0; i < slots.Count && remaining > 0; i++)
                    if (dropper.parts[slots[i].part].prefab.name == part)
                    {
                        chosen.Add(i);
                        remaining--;
                    }
                if (remaining > 0)
                    return RepairDamagePlan.Select(slots.Count, dropper.minimumMissingParts,
                        dropper.maximumMissingParts, 7301 + orderIndex * 7919);
            }
            return chosen.ToArray();
        }

        Order GenerateEndless(Order previous)
        {
            int minimum = Mathf.Clamp(dropper.minimumMissingParts, 1, slots.Count);
            int maximum = Mathf.Clamp(dropper.maximumMissingParts, minimum, slots.Count);
            var missing = RepairDamagePlan.Select(slots.Count, minimum, maximum, endlessRandom.Next());
            if (previous != null)
            {
                var counts = new int[dropper.parts.Length];
                foreach (int index in missing) counts[slots[index].part]++;
                bool same = true;
                for (int i = 0; i < counts.Length; i++)
                    same &= counts[i] == previous.missingCounts[i];
                if (same) missing = MakeDifferent(missing, minimum, maximum);
            }
            return BuildOrder(missing);
        }

        int[] MakeDifferent(int[] rolled, int minimum, int maximum)
        {
            // Swap one slot for another kind so the parts list changes as well as the model.
            var selectedSlots = new HashSet<int>(rolled);
            for (int i = 0; i < rolled.Length; i++)
                for (int slot = 0; slot < slots.Count; slot++)
                    if (!selectedSlots.Contains(slot) && slots[slot].part != slots[rolled[i]].part)
                    {
                        rolled[i] = slot;
                        return rolled;
                    }
            int alternateCount = rolled.Length < maximum ? rolled.Length + 1 : rolled.Length - 1;
            return alternateCount >= minimum
                ? RepairDamagePlan.Select(slots.Count, alternateCount, alternateCount, endlessRandom.Next())
                : rolled;
        }

        void RerollEndless()
        {
            if (!HasEndless || !dropper || accepted == orders[EndlessIndex]) return;
            orders[EndlessIndex] = GenerateEndless(orders[EndlessIndex]);
            selected = EndlessIndex;
            Refresh();
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
            var completed = accepted;
            bool repaired = delivery;
            accepted = null;
            delivery = null;
            if (repaired && !completed.completed)
            {
                completed.completed = true;
                if (terminal)
                    terminal.SetCredits((int)Math.Min(int.MaxValue, (long)terminal.Credits + completed.Reward));

                if (dropper && HasEndless && orders[EndlessIndex] == completed)
                    orders[EndlessIndex] = GenerateEndless(completed);
                else UnlockEndlessIfReady();
            }
            Refresh();
        }

        void UnlockEndlessIfReady()
        {
            if (!dropper || orders.Count != InitialOrders.Length || !orders.TrueForAll(order => order.completed)) return;
            orders.Add(GenerateEndless(null));
            selected = EndlessIndex;
        }

        [ContextMenu("Debug/Complete Main Orders and Open Endless")]
        void CompleteMainOrdersForTesting()
        {
            if (!Application.isPlaying) return;
            Open();
            if (!dropper || orders.Count < InitialOrders.Length)
            {
                Debug.LogWarning("Repair orders are unavailable in this scene.", this);
                return;
            }
            for (int i = 0; i < InitialOrders.Length; i++)
                orders[i].completed = true;
            UnlockEndlessIfReady();
            selected = EndlessIndex;
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

            endlessSection = Box();
            endlessSection.style.marginTop = 6;
            endlessHeading = MakeLabel("", 13, Muted);
            endlessHeading.style.marginBottom = 6;
            endlessSection.Add(endlessHeading);
            endlessRow = MakeLabel("", 14, Text);
            endlessRow.style.height = 36;
            endlessRow.style.paddingLeft = 8;
            endlessRow.style.unityTextAlign = TextAnchor.MiddleLeft;
            endlessSection.Add(endlessRow);
            reroll = MakeLabel("", 14, Text);
            reroll.style.marginTop = 6;
            reroll.style.paddingLeft = 6;
            reroll.style.alignSelf = Align.FlexStart;
            endlessSection.Add(reroll);
            queue.Add(endlessSection);

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
            reward = MakeLabel("", 15, new Color(0.55f, 1f, 0.6f));
            reward.style.marginTop = 4;
            details.Add(reward);
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
            queueHeading.text = ru ? "ОСНОВНЫЕ ЗАКАЗЫ" : "MAIN ORDERS";
            for (int i = 0; i < rows.Length; i++)
            {
                int index = i;
                bool present = index < orders.Count;
                rows[i].style.display = present ? DisplayStyle.Flex : DisplayStyle.None;
                if (!present) continue;
                bool done = orders[index].completed;
                rows[i].text = $"{index + 1:00}  {Company}";
                rows[i].style.color = done ? Muted : Text;
                strikes[i].style.display = done ? DisplayStyle.Flex : DisplayStyle.None;
                rows[i].style.backgroundColor = index == selected ? new Color(1, 1, 1, 0.12f) : Color.clear;
            }
            endlessSection.style.display = HasEndless ? DisplayStyle.Flex : DisplayStyle.None;
            if (HasEndless)
            {
                endlessHeading.text = "ENDLESS MODE";
                endlessRow.text = ru ? "СЛУЧАЙНЫЙ ЗАКАЗ" : "RANDOM ORDER";
                Border(endlessRow, selected == EndlessIndex ? Text : Edge);
                endlessRow.style.backgroundColor = selected == EndlessIndex ? new Color(1, 1, 1, 0.12f) : Color.clear;
                reroll.text = ru ? "[ПЕРЕБРОСИТЬ]" : "[REROLL]";
                bool canReroll = accepted != orders[EndlessIndex];
                reroll.style.color = canReroll ? Text : Muted;
                Border(reroll, canReroll ? Text : Edge);
            }
            customer.text = ru ? $"ЗАКАЗЧИК   {Company}" : $"CUSTOMER   {Company}";
            if (orders.Count == 0)
            {
                parts.text = ru ? "РЕМОНТНЫЙ ПОСТ НЕДОСТУПЕН" : "REPAIR BAY OFFLINE";
                description.text = "";
                reward.text = "";
                RefreshAction();
                return;
            }
            var order = orders[selected];
            var items = new List<string>();
            for (int i = 0; i < order.missingCounts.Length; i++)
                if (order.missingCounts[i] > 0)
                    items.Add($"{SalvageField.PartName(dropper.parts[i].prefab)} ×{order.missingCounts[i]}");
            parts.text = (ru ? "НЕДОСТАЮЩИЕ ДЕТАЛИ\n" : "MISSING PARTS\n") + string.Join("  /  ", items);
            reward.text = ru ? $"НАГРАДА  +{order.Reward} CR" : $"REWARD  +{order.Reward} CR";
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
