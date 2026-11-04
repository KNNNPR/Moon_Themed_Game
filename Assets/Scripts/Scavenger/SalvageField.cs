using System.Collections.Generic;
using MoonThemedGame.Repair;
using MoonThemedGame.Weather;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Robot parts scattered around the base. SCAVENGER picks them up with E and unloads them
    // at the charge station; they then lie in the repair bay for the player to fit.
    [DisallowMultipleComponent]
    public sealed class SalvageField : MonoBehaviour
    {
        [Min(0)] public int maxPieces = 8;
        [Min(0)] public int startPieces = 6;
        public Vector2 respawnInterval = new Vector2(25, 40);
        public Vector2 spawnDistance = new Vector2(12, 55);
        [Min(0)] public int creditsPerPart = 3;
        public LayerMask groundLayers = ~0;

        ScavengerLink link;
        GameObject[] kinds;
        Vector3 center;
        float nextSpawn;
        readonly List<GameObject> cargo = new List<GameObject>();
        readonly RaycastHit[] hits = new RaycastHit[32];

        public IReadOnlyList<GameObject> Cargo => cargo;
        public bool Full => link && link.state && cargo.Count >= link.state.cargoCapacity;
        public SalvagePiece Near { get; private set; }
        public SalvagePiece Closest { get; private set; }
        public float ClosestDistance { get; private set; }

        public static string PartName(GameObject prefab)
        {
            if (!prefab) return "";
            string name = prefab.name.ToLowerInvariant();
            return Loc.T(name.Contains("wheel") ? "WHEEL"
                : name.Contains("battery") ? "BATTERY"
                : name.Contains("headlight") ? "HEADLIGHTS"
                : name.Contains("body") ? "BODY PANEL"
                : name.Contains("window") ? "WINDOW"
                : prefab.name.ToUpperInvariant());
        }

        public void Setup(ScavengerLink owner, Vector3 home)
        {
            link = owner;
            var weather = MoonWeatherSystem.Active;
            center = weather ? weather.shelterCenter : home;
            kinds = FindKinds();
            if (kinds.Length == 0) { Debug.LogWarning("[Scavenger] No part prefabs for salvage.", this); return; }
            for (int i = 0; i < startPieces; i++) TrySpawn();
            nextSpawn = Time.time + Random.Range(respawnInterval.x, respawnInterval.y);
        }

        static GameObject[] FindKinds()
        {
            var bay = RepairBay.Active ? RepairBay.Active : FindAnyObjectByType<RepairBay>();
            if (bay && bay.Parts.Length > 0) return bay.Parts;
            var dropper = FindAnyObjectByType<RepairRobotDropper>();
            var list = new List<GameObject>();
            if (dropper && dropper.parts != null)
                foreach (var part in dropper.parts) if (part != null && part.prefab) list.Add(part.prefab);
            if (list.Count == 0)
            {
                var catalog = Resources.Load<ShopCatalog>("ShopCatalog");
                if (catalog)
                    foreach (var prefab in new[] { catalog.battery, catalog.wheel, catalog.headlights, catalog.cargoBay })
                        if (prefab) list.Add(prefab);
            }
            return list.ToArray();
        }

        bool TrySpawn()
        {
            if (kinds == null || kinds.Length == 0 || SalvagePiece.All.Count >= maxPieces) return false;
            var weather = MoonWeatherSystem.Active;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var direction = Random.insideUnitCircle.normalized;
                var candidate = center + new Vector3(direction.x, 0, direction.y) * Random.Range(spawnDistance.x, spawnDistance.y);
                if (weather && weather.IsBaseFootprint(candidate, 4)) continue;
                if (link.chargeBuilding)
                {
                    var building = link.chargeBuilding.bounds;
                    building.Expand(4);
                    if (building.Contains(new Vector3(candidate.x, building.center.y, candidate.z))) continue;
                }
                if (TooClose(candidate)) continue;
                if (!Ground(candidate, out var point, out var normal)) continue;
                SalvagePiece.Create(kinds[Random.Range(0, kinds.Length)], point, normal, link.robotScale);
                return true;
            }
            return false;
        }

        bool TooClose(Vector3 candidate)
        {
            foreach (var piece in SalvagePiece.All)
                if (Flat(piece.transform.position - candidate).magnitude < 5) return true;
            foreach (var lever in RobotLever.All)
                if (Flat(lever.transform.position - candidate).magnitude < 4) return true;
            return link.robot && Flat(link.robot.position - candidate).magnitude < 4;
        }

        bool Ground(Vector3 candidate, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            int count = Physics.RaycastNonAlloc(candidate + Vector3.up * 100, Vector3.down, hits, 200, groundLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.normal.y < 0.7f || hit.distance >= nearest) continue;
                if (hit.collider.GetComponentInParent<ScavengerRobotState>() || hit.collider.GetComponentInParent<FirstPersonController>()) continue;
                nearest = hit.distance;
                point = hit.point;
                normal = hit.normal;
            }
            return nearest < float.MaxValue;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void Update()
        {
            if (!link || !link.robot) return;
            if (Time.time >= nextSpawn)
            {
                nextSpawn = Time.time + Random.Range(respawnInterval.x, respawnInterval.y);
                TrySpawn();
            }
            float reach = link.Drive ? link.Drive.Length * 0.5f + 0.6f : 1.5f;
            Near = Closest = null;
            ClosestDistance = float.MaxValue;
            foreach (var piece in SalvagePiece.All)
            {
                float distance = Flat(piece.transform.position - link.robot.position).magnitude;
                if (distance < ClosestDistance) { ClosestDistance = distance; Closest = piece; }
                if (distance <= reach + piece.radius && (!Near || distance < Flat(Near.transform.position - link.robot.position).magnitude)) Near = piece;
            }
            if (link.state) link.state.Cargo = cargo.Count;
            if (cargo.Count > 0 && link.InChargeBuilding) Unload();
        }

        public void PickUp(ScavengerFeedHud hud)
        {
            if (!Near) return;
            if (Full) { hud.Notify(Loc.T("CARGO FULL - UNLOAD AT THE CHARGE STATION")); return; }
            var piece = Near;
            cargo.Add(piece.prefab);
            piece.Collect(link.robot);
            Near = null;
            hud.Notify(Loc.F("{0} LOADED  CARGO {1}/{2}", PartName(piece.prefab), cargo.Count, link.state.cargoCapacity));
        }

        void Unload()
        {
            int count = cargo.Count;
            var bay = RepairBay.Active;
            foreach (var prefab in cargo) if (bay) bay.Deliver(prefab);
            cargo.Clear();
            if (link.state) link.state.Cargo = 0;
            int credits = count * creditsPerPart;
            if (link.terminal && credits > 0) link.terminal.SetCredits(link.terminal.Credits + credits);
            var hud = link.GetComponent<ScavengerFeedHud>();
            if (hud) hud.Notify(Loc.F("UNLOADED {0} PARTS TO THE REPAIR BAY  +{1} CR", count, credits));
        }
    }
}
