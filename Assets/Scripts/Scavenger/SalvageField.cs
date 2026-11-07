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
        [Tooltip("Random fallback limit. An authored layout populates its own fixed points.")]
        [Min(0)] public int maxPieces = 8;
        [Tooltip("Random fallback count, used only when there are no authored spawn points.")]
        [Min(0)] public int startPieces = 6;
        public Vector2 respawnInterval = new Vector2(25, 40);
        public Vector2 spawnDistance = new Vector2(12, 55);
        public LayerMask groundLayers = ~0;

        ScavengerLink link;
        GameObject[] kinds;
        Vector3 center;
        float nextSpawn;
        float nextSupplyCheck;
        int orderNumber=1;
        RepairMessagesApp messages;
        public int OrderTier {get;private set;}
        readonly Dictionary<GameObject,int> orderNeeds=new Dictionary<GameObject,int>();
        readonly List<GameObject> cargo = new List<GameObject>();
        readonly List<SalvageSpawnPoint> spawnPoints=new List<SalvageSpawnPoint>();
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
            messages=owner.GetComponent<RepairMessagesApp>();
            var weather = MoonWeatherSystem.Active;
            center = weather ? weather.shelterCenter : home;
            kinds = FindKinds();
            if (kinds.Length == 0) { Debug.LogWarning("[Scavenger] No part prefabs for salvage.", this); return; }
            var layout=SalvageSpawnLayout.Ensure(gameObject.scene);
            spawnPoints.Clear();
            if(layout)spawnPoints.AddRange(layout.GetComponentsInChildren<SalvageSpawnPoint>());
            else spawnPoints.AddRange(SalvageSpawnPoint.All);
            if (spawnPoints.Count > 0)
                foreach (var point in spawnPoints) SpawnAt(point);
            else
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

        void SpawnAt(SalvageSpawnPoint point)
        {
            SpawnAt(point,null);
        }
        bool Allowed(SalvageSpawnPoint point)=>point && point.isActiveAndEnabled && point.progressionTier>=OrderTier;
        public bool IsOrderTarget(SalvagePiece piece)=>messages && messages.HasActiveOrder && piece && piece.SpawnPoint &&
            piece.SpawnPoint.orderRoute && piece.SpawnPoint.progressionTier==OrderTier;
        public void ApplyOrderProgression(int number)
        {
            orderNumber=Mathf.Max(orderNumber,number);
            int wanted=Mathf.Clamp((orderNumber-1)/2,0,4);
            float endurance=link && link.state?link.state.capacity/Mathf.Max(.01f,link.state.drainPerSecond)*RobotUpgrades.SpeedScale*.93f:93;
            int available=0;
            foreach(var point in spawnPoints)
                if(point && point.orderRoute && point.progressionTier<=wanted && point.estimatedLoopSeconds<=endurance)
                    available=Mathf.Max(available,point.progressionTier);
            OrderTier=Mathf.Max(OrderTier,available);
            foreach(var point in spawnPoints)
                if(point && !Allowed(point) && point.Piece && !point.Piece.Collected)RemovePiece(point);
            EnsureOrderSupply();
        }
        void RemovePiece(SalvageSpawnPoint point)
        {
            var piece=point.Piece;point.Piece=null;
            if(piece){piece.gameObject.SetActive(false);Destroy(piece.gameObject);}
        }
        void EnsureOrderSupply()
        {
            if(!messages || !messages.HasActiveOrder)return;
            messages.GetUnstockedSalvageNeeds(orderNeeds);
            var candidates=new List<SalvageSpawnPoint>();
            foreach(var point in spawnPoints)
            {
                if(!Allowed(point) || !point.orderRoute || point.progressionTier!=OrderTier)continue;
                if(!point.Free && point.Piece && orderNeeds.TryGetValue(point.Piece.prefab,out int count) && count>0)
                {orderNeeds[point.Piece.prefab]=count-1;continue;}
                candidates.Add(point);
            }
            foreach(var point in candidates)
            {
                if(link.robot && Flat(link.robot.position-point.transform.position).magnitude<4)continue;
                GameObject needed=null;
                foreach(var pair in orderNeeds)if(pair.Value>0){needed=pair.Key;break;}
                if(!needed)break;
                if(point.Piece && !point.Piece.Collected)RemovePiece(point);
                SpawnAt(point,needed);orderNeeds[needed]--;
            }
        }
        void SpawnAt(SalvageSpawnPoint point,GameObject requiredPart)
        {
            if(!Allowed(point) || !point.Free)return;
            var position = point.transform.position;
            var normal = point.transform.up;
            if (Ground(position + Vector3.up, 100, out var ground, out var groundNormal))
            {
                position = ground;
                normal = groundNormal;
            }
            var prefab = requiredPart?requiredPart:point.part ? point.part : kinds[Random.Range(0, kinds.Length)];
            point.Piece = SalvagePiece.Create(prefab, position, normal, link.robotScale * 0.5f);
            point.Piece.SpawnPoint=point;
        }

        // Refills one empty point the robot isn't standing on.
        void RespawnAtPoint()
        {
            EnsureOrderSupply();
            var free = new List<SalvageSpawnPoint>();
            foreach (var point in spawnPoints)
                if (Allowed(point) && point.Free && !(messages && messages.HasActiveOrder && point.orderRoute && point.progressionTier==OrderTier) && (!link.robot || Flat(link.robot.position - point.transform.position).magnitude >= 4)) free.Add(point);
            if (free.Count > 0) SpawnAt(free[Random.Range(0, free.Count)]);
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
                if (!Ground(candidate + Vector3.up * 100, 200, out var point, out var normal)) continue;
                SalvagePiece.Create(kinds[Random.Range(0, kinds.Length)], point, normal, link.robotScale * 0.5f);
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

        bool Ground(Vector3 origin, float distance, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, distance, groundLayers, QueryTriggerInteraction.Ignore);
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
            if(Time.time>=nextSupplyCheck){nextSupplyCheck=Time.time+1;EnsureOrderSupply();}
            if (Time.time >= nextSpawn)
            {
                nextSpawn = Time.time + Random.Range(respawnInterval.x, respawnInterval.y);
                if (spawnPoints.Count > 0) RespawnAtPoint();
                else TrySpawn();
            }
            float reach = link.Drive ? link.Drive.Length * 1.1f + 1.5f : 3;
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
            int count = 0;
            var bay = RepairBay.Active;
            if(!bay)return;
            for(int i=0;i<cargo.Count;)
            {
                if(bay.Deliver(cargo[i])){cargo.RemoveAt(i);count++;}
                else i++;
            }
            if(count==0)return;
            if (link.state) link.state.Cargo = cargo.Count;
            var hud = link.GetComponent<ScavengerFeedHud>();
            if (hud) hud.Notify(Loc.Russian
                ? $"ВЫГРУЖЕНО ДЕТАЛЕЙ: {count} - В РЕМОНТНЫЙ ОТСЕК"
                : $"UNLOADED {count} PARTS TO THE REPAIR BAY");
        }
    }
}
