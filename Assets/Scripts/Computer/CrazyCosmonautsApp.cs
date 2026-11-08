using System;
using System.Collections.Generic;
using MoonThemedGame.Interactions;
using MoonThemedGame.Repair;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace MoonThemedGame.Computer
{
    public enum CrazyCosmonautsState { Ready, Playing, Paused, Intermission, Dead }

    [DisallowMultipleComponent]
    public sealed class CrazyCosmonautsApp : MonoBehaviour
    {
        public const string RetryKey = "crazyCosmonauts.retryAtUtc";
        public CrazyCosmonautsContent content;
        public bool Purchased { get; private set; }
        public bool IsOpen => window != null && window.style.display == DisplayStyle.Flex;
        public CrazyCosmonautsState State { get; private set; }
        public int Wave { get; private set; }
        public int Health { get; private set; }
        public int PendingSpawns { get; private set; }
        public int AliveCount { get { int count = 0; foreach (var enemy in enemies) if (enemy && !enemy.IsDead) count++; return count; } }
        public int RetryRemaining => lockout?.Remaining ?? 0;
        public bool GameplayActive => State == CrazyCosmonautsState.Playing && IsOpen && terminal && terminal.IsFocused && !terminal.IsTransitioning && !GameSettingsMenu.BlockGameplayInput;
        public bool CapturingInput => GameplayActive;
        public bool FireInputReady => Time.frameCount > captureFrame;
        public float ArenaFloor => arena ? arena.transform.position.y : 0;
        public CrazyCosmonautsPlayer Player => player;
        public RenderTexture GameTexture => texture;
        public IReadOnlyList<ZombieActor> Enemies => enemies;

        PcMonitorTerminal terminal;
        CrazyCosmonautsLockout lockout;
        Scene gameScene;
        CrazyCosmonautsArena arena;
        NavMeshDataInstance navigation;
        CrazyCosmonautsPlayer player;
        RenderTexture texture;
        readonly List<ZombieActor> enemies = new List<ZombieActor>();
        readonly List<(VisualElement element, Action action)> actions = new List<(VisualElement, Action)>();
        VisualElement boundRoot, icon, window, overlay, scanlines;
        Label iconName, title, hp, waveLabel, ammo, controls, banner, message, primaryText, exitText;
        float nextSpawn, damageFlash;
        bool ownsCursor;
        int spawnCursor, lastRewardedWave, runId;
        int captureFrame;
        static string T(string en, string ru) => Loc.Russian ? ru : en;

        void Awake()
        {
            terminal = GetComponent<PcMonitorTerminal>();
            if (!content) content = Resources.Load<CrazyCosmonautsContent>("CrazyCosmonautsContent");
            // Credits and the other software purchases belong to this game run.
            // A legacy PlayerPrefs installation must not grant a free copy in a new run.
            Purchased = false;
            lockout = new CrazyCosmonautsLockout(RetryKey);
        }
        void OnEnable() => Loc.Changed += Refresh;
        void OnDisable() { Loc.Changed -= Refresh; Close(); }
        void OnDestroy() { DestroyRun(); icon?.RemoveFromHierarchy(); window?.RemoveFromHierarchy(); }

        public bool Purchase()
        {
            if (Purchased || !content || !terminal || terminal.Credits < content.price || !content.arenaPrefab) return false;
            terminal.SetCredits(terminal.Credits - content.price);
            Purchased = true;
            if (terminal.Root != null && boundRoot != terminal.Root) Build();
            Refresh(); return true;
        }
        public void SetOpen(bool open)
        {
            if (!open) { Close(); return; }
            if (!Purchased || !content || !terminal || terminal.Root == null) return;
            if (boundRoot != terminal.Root) Build();
            GetComponent<ShopApp>()?.Close(); GetComponent<SatelliteConsoleApp>()?.Close(); GetComponent<RepairMessagesApp>()?.Close();
            GetComponent<MoonFinderApp>()?.Close(); GetComponent<MoneyGetApp>()?.Close();
            GetComponent<RoutePlannerApp>()?.Close();
            State = RetryRemaining > 0 ? CrazyCosmonautsState.Dead : CrazyCosmonautsState.Ready;
            window.style.display = DisplayStyle.Flex; window.BringToFront();
            terminal.Root.Q("screen-cursor")?.BringToFront(); Refresh();
        }
        public void Close()
        {
            ReleaseCursor(); DestroyRun();
            if (window != null) window.style.display = DisplayStyle.None;
            State = RetryRemaining > 0 ? CrazyCosmonautsState.Dead : CrazyCosmonautsState.Ready;
        }
        public void HandleBack()
        {
            if (State == CrazyCosmonautsState.Playing) { State = CrazyCosmonautsState.Paused; ReleaseCursor(); Refresh(); }
            else Close();
        }

        public bool StartRun()
        {
            if (!Purchased || !IsOpen || !content || RetryRemaining > 0 || State == CrazyCosmonautsState.Playing) return false;
            DestroyRun();
            if (!content.arenaPrefab || !content.zombiePrefab || !content.pistolPrefab) return false;
            gameScene = SceneManager.CreateScene("zombie shooter / PC game " + GetInstanceID() + " / " + (++runId));
            var instance = Instantiate(content.arenaPrefab, new Vector3(0, 10000, 0), Quaternion.identity);
            SceneManager.MoveGameObjectToScene(instance, gameScene); arena = instance.GetComponent<CrazyCosmonautsArena>();
            if (!arena || !arena.playerSpawn || arena.zombieSpawns.Length == 0) { DestroyRun(); return false; }
            if (arena.navigation) navigation = NavMesh.AddNavMeshData(arena.navigation, arena.transform.position, arena.transform.rotation);
            texture = new RenderTexture(Mathf.Max(320, terminal.assets.resolution.x - 32), Mathf.Max(240, terminal.assets.resolution.y - 102), 24) { name = "Crazy Cosmonauts / live PC game", filterMode = FilterMode.Bilinear }; texture.Create();
            var avatar = new GameObject("Crazy Cosmonauts / player"); SceneManager.MoveGameObjectToScene(avatar, gameScene);
            avatar.transform.SetPositionAndRotation(arena.playerSpawn.position, arena.playerSpawn.rotation);
            avatar.layer = 30; player = avatar.AddComponent<CrazyCosmonautsPlayer>(); player.Initialize(this, texture);
            var image = window.Q<Image>("crazy-game-image"); if (image != null) image.image = texture;
            Health = content.startingHealth; Wave = 0; lastRewardedWave = 0;
            return ContinueWave();
        }
        public bool ContinueWave()
        {
            if (!arena || !player || (State != CrazyCosmonautsState.Intermission && Wave != 0)) return false;
            // Clear old corpses between waves, keeping the run small even in endless play.
            foreach (var enemy in enemies) if (enemy) Destroy(enemy.gameObject); enemies.Clear();
            Wave++; PendingSpawns = Mathf.Min(40, content.firstWaveCount + (Wave - 1) * content.addedPerWave);
            nextSpawn = Time.time + .8f; State = CrazyCosmonautsState.Playing; CaptureCursor(); Refresh(); return true;
        }
        public void TakeDamage(int amount)
        {
            if (!GameplayActive || amount <= 0) return;
            Health = Mathf.Max(0, Health - amount); damageFlash = .35f;
            if (Health == 0)
            {
                State = CrazyCosmonautsState.Dead; lockout.Start(content.retryDelaySeconds); ReleaseCursor();
            }
            Refresh();
        }
        void CaptureCursor()
        {
            captureFrame = Time.frameCount;
            if (!terminal || !terminal.IsFocused) return;
            terminal.HidePointer(); Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; ownsCursor = true;
        }
        void ReleaseCursor()
        {
            if (!ownsCursor) return;
            if (terminal && terminal.IsFocused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            ownsCursor = false;
        }
        void DestroyRun()
        {
            foreach (var enemy in enemies) if (enemy) enemy.gameObject.SetActive(false); enemies.Clear(); PendingSpawns = 0;
            if (arena) arena.gameObject.SetActive(false);
            if (player) { player.gameObject.SetActive(false); player.view.targetTexture = null; }
            if (navigation.valid) navigation.Remove();
            if (gameScene.IsValid() && gameScene.isLoaded) SceneManager.UnloadSceneAsync(gameScene);
            gameScene = default; arena = null; player = null;
            if (texture) { texture.Release(); Destroy(texture); texture = null; }
            if (window != null) { var image = window.Q<Image>("crazy-game-image"); if (image != null) image.image = null; }
        }
        void SpawnEnemy()
        {
            Transform point = null; float best = -1;
            for (int i = 0; i < arena.zombieSpawns.Length; i++)
            {
                var candidate = arena.zombieSpawns[(spawnCursor + i) % arena.zombieSpawns.Length];
                if (!candidate) continue;
                float distance = Vector3.Distance(candidate.position, player.transform.position);
                if (distance < 7 || Physics.CheckCapsule(candidate.position + Vector3.up * .35f, candidate.position + Vector3.up * 1.45f, .3f, 1 << 30, QueryTriggerInteraction.Ignore)) continue;
                float score = Mathf.Min(distance, 20) + UnityEngine.Random.value * 5;
                if (score > best) { best = score; point = candidate; }
            }
            if (!point) { nextSpawn = Time.time + .7f; return; }
            spawnCursor++;
            var enemy = ZombieSpawner.Create(content.zombiePrefab, point.position, point.rotation, 1);
            SceneManager.MoveGameObjectToScene(enemy.gameObject, gameScene);
            enemy.name = "Crazy Cosmonauts / wave " + Wave + " zombie";
            foreach (var node in enemy.GetComponentsInChildren<Transform>()) node.gameObject.layer = 30;
            if (content.zombieMaterial) foreach (var skin in enemy.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = content.zombieMaterial;
            enemy.targetOverride = player.transform; enemy.detectionRange = 100; enemy.walkSpeed = Mathf.Min(1.9f, 1.0f + Wave * .045f); enemy.corpseLifetime = 12;
            enemy.ContactAttack = _ => TakeDamage(content.contactDamage);
            var pursuit = enemy.gameObject.AddComponent<CrazyCosmonautsPursuit>(); pursuit.target = player.transform; enemy.NavigationTarget = pursuit.Waypoint;
            enemies.Add(enemy); PendingSpawns--; nextSpawn = Time.time + content.spawnInterval;
        }
        void Update()
        {
            if (!terminal || !content || terminal.Root == null) return;
            if (boundRoot != terminal.Root) Build();
            if (IsOpen && State == CrazyCosmonautsState.Playing && !terminal.IsFocused) { State = CrazyCosmonautsState.Paused; ReleaseCursor(); }
            foreach (var enemy in enemies) if (enemy && !enemy.IsDead) enemy.enabled = GameplayActive;
            if (GameplayActive)
            {
                if (PendingSpawns > 0 && AliveCount < content.maximumAlive && Time.time >= nextSpawn) SpawnEnemy();
                if (PendingSpawns == 0 && AliveCount == 0 && Wave > lastRewardedWave)
                {
                    lastRewardedWave = Wave;
                    terminal.SetCredits((int)Math.Min(int.MaxValue, (long)terminal.Credits + content.waveReward));
                    State = CrazyCosmonautsState.Intermission; ReleaseCursor();
                }
            }
            damageFlash = Mathf.Max(0, damageFlash - Time.unscaledDeltaTime);
            if (IsOpen) { scanlines.MarkDirtyRepaint(); Refresh(); }
            if (GameSettingsMenu.BlockGameplayInput) return;
            bool pointer = terminal.IsFocused && terminal.PointerActive && PcLogin.IsUnlocked(terminal);
            var point = terminal.PointerPosition;
            bool free = !(GetComponent<ShopApp>()?.IsOpen ?? false) && !(GetComponent<MoonFinderApp>()?.IsOpen ?? false) && !(GetComponent<MoneyGetApp>()?.IsOpen ?? false)
                && !terminal.RouteMapOpen && !(GetComponent<RepairMessagesApp>()?.IsOpen ?? false) && !(GetComponent<SatelliteConsoleApp>()?.IsOpen ?? false);
            bool hover = Purchased && !IsOpen && free && pointer && icon.worldBound.Contains(point);
            icon.EnableInClassList("pointer-over", hover);
            foreach (var action in actions) action.element.style.backgroundColor = pointer && action.element.worldBound.Contains(point) ? new Color(1, 1, 1, .14f) : Color.clear;
            if (!pointer || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            if (hover) { SetOpen(true); return; }
            if (IsOpen && State != CrazyCosmonautsState.Playing)
                for (int i = actions.Count - 1; i >= 0; i--)
                    if (actions[i].element.resolvedStyle.display != DisplayStyle.None && actions[i].element.worldBound.Contains(point)) { actions[i].action(); break; }
        }
        void Primary()
        {
            if (State == CrazyCosmonautsState.Paused) { State = CrazyCosmonautsState.Playing; CaptureCursor(); Refresh(); }
            else if (State == CrazyCosmonautsState.Intermission) ContinueWave();
            else StartRun();
        }
        VisualElement Box(string name, VisualElement parent)
        { var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; parent.Add(e); return e; }
        Label Text(string name, int size, VisualElement parent)
        { var e = new Label { name = name, pickingMode = PickingMode.Ignore }; e.style.fontSize = size; e.style.color = new Color(.87f, .94f, .89f); parent.Add(e); return e; }
        VisualElement ActionButton(string name, VisualElement parent, out Label text, Action action)
        {
            var box = Box(name, parent); box.style.paddingLeft = box.style.paddingRight = 14; box.style.paddingTop = box.style.paddingBottom = 9;
            text = Text(name + "-text", 19, box); actions.Add((box, action)); return box;
        }
        void Build()
        {
            var desktop = terminal.Root.Q("desktop"); if (desktop == null) return;
            icon?.RemoveFromHierarchy(); window?.RemoveFromHierarchy(); actions.Clear(); boundRoot = terminal.Root;
            icon = Box("crazycosmonauts", desktop); icon.AddToClassList("desktop-icon"); icon.style.position = Position.Absolute; icon.style.left = 590; icon.style.top = 330; icon.style.width = 200;
            var image = new Image { vectorImage = content.desktopIcon, tintColor = Color.white, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore }; image.AddToClassList("icon-image"); icon.Add(image); iconName = Text("crazy-icon-name", 16, icon); iconName.AddToClassList("icon-label");
            window = Box("crazycosmonauts-window", desktop); window.style.position = Position.Absolute; window.style.left = 8; window.style.top = 6; window.style.width = terminal.assets.resolution.x - 16; window.style.height = terminal.assets.resolution.y - 12;
            window.style.backgroundColor = new Color(.009f, .015f, .014f); window.style.display = DisplayStyle.None;
            var header = Box("crazy-header", window); header.style.height = 38; header.style.paddingLeft = 12; header.style.flexDirection = FlexDirection.Row; header.style.alignItems = Align.Center;
            title = Text("crazy-title", 19, header); title.style.flexGrow = 1; ActionButton("crazy-close", header, out var close, Close); close.text = "[X]";
            var viewport = Box("crazy-viewport", window); viewport.style.position = Position.Relative; viewport.style.height = terminal.assets.resolution.y - 102; viewport.style.marginLeft = viewport.style.marginRight = 8; viewport.style.overflow = Overflow.Hidden;
            var gameImage = new Image { name = "crazy-game-image", image = texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }; gameImage.style.position = Position.Absolute; gameImage.style.left = gameImage.style.top = gameImage.style.right = gameImage.style.bottom = 0; viewport.Add(gameImage);
            scanlines = Box("crazy-crt-scanlines", viewport); scanlines.style.position = Position.Absolute; scanlines.style.left = scanlines.style.top = scanlines.style.right = scanlines.style.bottom = 0;
            scanlines.generateVisualContent += context =>
            {
                var p = context.painter2D; var rect = context.visualElement.contentRect; p.strokeColor = new Color(0, 0, 0, .2f); p.lineWidth = 1;
                p.BeginPath(); for (float y = 0; y < rect.height; y += 4) { p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(rect.width, y)); } p.Stroke();
                float band = Mathf.Repeat(Time.unscaledTime * 24, rect.height + 24) - 12;
                p.lineWidth = 20; p.strokeColor = new Color(.65f, .95f, .8f, .045f); p.BeginPath(); p.MoveTo(new Vector2(0, band)); p.LineTo(new Vector2(rect.width, band)); p.Stroke();
            };
            var cross = Text("crazy-crosshair", 22, viewport); cross.text = "+"; cross.style.position = Position.Absolute;
            cross.style.width = cross.style.height = 18; cross.style.left = (terminal.assets.resolution.x - 32) * .5f - 9; cross.style.top = (terminal.assets.resolution.y - 102) * .5f - 9;
            cross.style.marginLeft = cross.style.marginRight = cross.style.marginTop = cross.style.marginBottom = 0;
            cross.style.unityTextAlign = TextAnchor.MiddleCenter; cross.style.unityTextOutlineWidth = 1; cross.style.unityTextOutlineColor = Color.black;
            waveLabel = Text("crazy-wave", 17, viewport); waveLabel.style.position = Position.Absolute; waveLabel.style.left = 14; waveLabel.style.top = 10;
            var health = Box("crazy-health", viewport); health.style.position = Position.Absolute; health.style.left = 14; health.style.bottom = 12; health.style.flexDirection = FlexDirection.Row;
            health.style.backgroundColor = new Color(0, 0, 0, .7f); health.style.paddingLeft = health.style.paddingRight = 7; health.style.paddingTop = health.style.paddingBottom = 3;
            var healthImage = new Image { vectorImage = content.healthIcon, tintColor = Color.white, scaleMode = ScaleMode.ScaleToFit }; healthImage.style.width = healthImage.style.height = 24; health.Add(healthImage); hp = Text("crazy-hp", 22, health); hp.style.marginLeft = 8;
            ammo = Text("crazy-ammo", 20, viewport); ammo.style.position = Position.Absolute; ammo.style.right = 14; ammo.style.bottom = 12;
            ammo.style.backgroundColor = new Color(0, 0, 0, .7f); ammo.style.paddingLeft = ammo.style.paddingRight = 7; ammo.style.paddingTop = ammo.style.paddingBottom = 3;
            overlay = Box("crazy-overlay", viewport); overlay.style.position = Position.Absolute; overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0; overlay.style.backgroundColor = new Color(0, 0, 0, .82f); overlay.style.alignItems = Align.Center; overlay.style.justifyContent = Justify.Center;
            banner = Text("crazy-banner", 29, overlay); message = Text("crazy-message", 17, overlay); message.style.marginTop = 14; message.style.whiteSpace = WhiteSpace.Normal; message.style.maxWidth = 780; message.style.unityTextAlign = TextAnchor.MiddleCenter;
            var primary = ActionButton("crazy-primary", overlay, out primaryText, Primary); primary.style.marginTop = 22;
            ActionButton("crazy-exit", overlay, out exitText, Close);
            controls = Text("crazy-controls", 13, window); controls.style.marginLeft = 14; controls.style.marginTop = 10;
            Refresh();
        }
        void Refresh()
        {
            if (icon == null || !content) return;
            icon.style.display = Purchased ? DisplayStyle.Flex : DisplayStyle.None; iconName.text = content.Title;
            title.text = content.Title.ToUpperInvariant() + " [PALE HORSE ARCADE]";
            hp.text = Health.ToString("000"); ammo.text = player ? player.Reloading ? T("RELOADING", "ПЕРЕЗАРЯДКА") : player.Ammo + " / 8" : "8 / 8";
            waveLabel.text = T("WAVE ", "ВОЛНА ") + Mathf.Max(1, Wave) + "   [" + T("INFECTED ", "ЗАРАЖЁННЫЕ ") + (AliveCount + PendingSpawns) + "]";
            controls.text = T("WASD MOVE   LMB FIRE   RMB AIM   [R] RELOAD   [ESC] PAUSE", "WASD ДВИЖЕНИЕ   ЛКМ ОГОНЬ   ПКМ ПРИЦЕЛ   [R] ПЕРЕЗАРЯДКА   [ESC] ПАУЗА");
            overlay.style.display = State == CrazyCosmonautsState.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            window.Q("crazy-crosshair").style.display = State == CrazyCosmonautsState.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            window.Q("crazy-primary").style.display = RetryRemaining > 0 ? DisplayStyle.None : DisplayStyle.Flex;
            banner.text = State == CrazyCosmonautsState.Dead ? T("SIGNAL LOST", "СИГНАЛ ПОТЕРЯН") : State == CrazyCosmonautsState.Intermission ? T("WAVE CLEARED", "ВОЛНА ЗАЧИЩЕНА") : State == CrazyCosmonautsState.Paused ? T("PAUSED", "ПАУЗА") : content.Title.ToUpperInvariant();
            message.text = RetryRemaining > 0 ? T("Reconnect in ", "Повторный вход через ") + TimeSpan.FromSeconds(RetryRemaining).ToString(@"mm\:ss") + T(" real time", " реального времени")
                : State == CrazyCosmonautsState.Intermission ? "+" + content.waveReward + T(" CREDITS TRANSFERRED. Continue when ready.", " КРЕДИТА НАЧИСЛЕНО. Продолжите, когда будете готовы.")
                : State == CrazyCosmonautsState.Paused ? T("Your run is paused. Resume or return to the desktop.", "Игра приостановлена. Продолжите или вернитесь на рабочий стол.")
                : "";
            message.style.display = string.IsNullOrEmpty(message.text) ? DisplayStyle.None : DisplayStyle.Flex;
            primaryText.text = State == CrazyCosmonautsState.Intermission ? T("[CONTINUE]", "[ПРОДОЛЖИТЬ]") : State == CrazyCosmonautsState.Paused ? T("[RESUME]", "[ПРОДОЛЖИТЬ]") : T("[START GAME]", "[НАЧАТЬ ИГРУ]");
            exitText.text = T("[BACK TO DESKTOP]", "[НА РАБОЧИЙ СТОЛ]");
        }
    }
}
