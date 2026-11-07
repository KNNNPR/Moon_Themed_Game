using System.Collections.Generic;
using MoonThemedGame.Repair;
using MoonThemedGame.Weather;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Only the active order's missing parts lie outside. SCAVENGER picks them up with E and unloads them
    // at the charge station; they then lie in the repair bay for the player to fit.
    [DisallowMultipleComponent]
    public sealed class SalvageField : MonoBehaviour
    {
        [Tooltip("Maximum required parts at once in the random fallback. Authored layouts use their selected order points.")]
        [Min(0)] public int maxPieces = 8;
        public Vector2 spawnDistance = new Vector2(12, 55);
        public LayerMask groundLayers = ~0;
        [Min(1)] public float targetSpacing=18;
        [Range(0,1)] public float manipulatorCacheChance=.5f;

        ScavengerLink link;
        Vector3 center;
        float nextSupplyCheck;
        int orderNumber=1;
        RepairMessagesApp messages;
        public int OrderTier {get;private set;}
        readonly Dictionary<GameObject,int> orderNeeds=new Dictionary<GameObject,int>();
        readonly List<GameObject> cargo = new List<GameObject>();
        readonly List<SalvageSpawnPoint> spawnPoints=new List<SalvageSpawnPoint>();
        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly Dictionary<SalvageSpawnPoint,GameObject> orderAssignments=new Dictionary<SalvageSpawnPoint,GameObject>();
        readonly List<SalvagePiece> fallbackPieces=new List<SalvagePiece>();
        readonly HashSet<SalvageSpawnPoint> usedThisOrder=new HashSet<SalvageSpawnPoint>();
        readonly HashSet<SalvageSpawnPoint> previousOrderPoints=new HashSet<SalvageSpawnPoint>();
        float orderEndurance;
        bool chooseManipulatorCache;
        bool HasOrder=>messages&&messages.HasSalvageOrder;

        public IReadOnlyList<GameObject> Cargo => cargo;
        public bool Full => link && link.state && cargo.Count >= link.state.cargoCapacity;
        public SalvagePiece Near { get; private set; }
        public SalvagePiece Closest { get; private set; }
        public float ClosestDistance { get; private set; }

        public static string PartName(GameObject prefab)
        {
            if (!prefab) return "";
            string name = prefab.name.ToLowerInvariant();
            string label = Loc.T(name.Contains("wheel") ? "WHEEL"
                : name.Contains("battery") ? "BATTERY"
                : name.Contains("headlight") ? "HEADLIGHTS"
                : name.Contains("body") ? "BODY PANEL"
                : name.Contains("window") ? "WINDOW"
                : prefab.name.ToUpperInvariant());
            var variant = prefab.GetComponent<RepairPartVariant>();
            return variant ? label + " [" + variant.robotName + "]" : label;
        }

        public void Setup(ScavengerLink owner, Vector3 home)
        {
            link = owner;
            messages=owner.GetComponent<RepairMessagesApp>();
            var weather = MoonWeatherSystem.Active;
            center = weather ? weather.shelterCenter : home;
            var layout=SalvageSpawnLayout.Ensure(gameObject.scene);
            spawnPoints.Clear();
            if(layout)spawnPoints.AddRange(layout.GetComponentsInChildren<SalvageSpawnPoint>());
            else spawnPoints.AddRange(SalvageSpawnPoint.All);
            ClearOrderSupply();
        }

        public void ClearOrderSupply()
        {
            foreach(var piece in new List<SalvagePiece>(SalvagePiece.All))
                if(piece&&piece.gameObject.scene==gameObject.scene)RemovePiece(piece);
            foreach(var point in spawnPoints)if(point)point.Piece=null;
            orderAssignments.Clear();fallbackPieces.Clear();orderNeeds.Clear();
            Near=Closest=null;ClosestDistance=float.MaxValue;
        }

        bool Allowed(SalvageSpawnPoint point)=>point && point.isActiveAndEnabled && point.progressionTier>=OrderTier &&
            (!point.requiresManipulator || RobotUpgrades.HasArm);
        bool Owns(SalvagePiece piece)=>piece&&(piece.SpawnPoint?
            orderAssignments.TryGetValue(piece.SpawnPoint,out var assigned)&&assigned==piece.prefab&&piece.SpawnPoint.Piece==piece:
            fallbackPieces.Contains(piece));
        public bool IsOrderTarget(SalvagePiece piece)=>HasOrder&&piece&&!piece.Collected&&Owns(piece);
        float Endurance=>link && link.state?link.state.capacity/Mathf.Max(.01f,link.state.drainPerSecond)*RobotUpgrades.SpeedScale*.93f:93;
        bool CanSupply(SalvageSpawnPoint point)=>Allowed(point) && point.estimatedLoopSeconds<=orderEndurance &&
            (point.requiresManipulator ? point.minimumOrderTier<=OrderTier : point.orderRoute && point.progressionTier==OrderTier);
        public void ApplyOrderProgression(int number)
        {
            orderNumber=Mathf.Max(orderNumber,number);
            // Six story repairs use six progressively farther safe supply routes.
            int wanted=Mathf.Clamp(orderNumber-1,0,5);
            float endurance=Endurance;
            int available=0;
            foreach(var point in spawnPoints)
                if(point && point.isActiveAndEnabled && point.orderRoute && !point.requiresManipulator && point.progressionTier<=wanted && point.estimatedLoopSeconds<=endurance)
                    available=Mathf.Max(available,point.progressionTier);
            OrderTier=Mathf.Max(OrderTier,available);
            orderEndurance=endurance;
            previousOrderPoints.Clear();previousOrderPoints.UnionWith(usedThisOrder);usedThisOrder.Clear();
            // Freeze the new plan until collection or the next accepted order; purchasing an
            // upgrade must not move markers while the rover is on its way to them.
            ClearOrderSupply();
            chooseManipulatorCache=RobotUpgrades.HasArm && Random.value<manipulatorCacheChance;
            EnsureOrderSupply();
        }
        void RemovePiece(SalvagePiece piece)
        {
            if(!piece)return;
            var point=piece.SpawnPoint;
            if(point&&point.Piece==piece){orderAssignments.Remove(point);point.Piece=null;}
            fallbackPieces.Remove(piece);
            if(Near==piece)Near=null;if(Closest==piece)Closest=null;
            piece.gameObject.SetActive(false);Destroy(piece.gameObject);
        }
        void EnsureOrderSupply()
        {
            if(!HasOrder){ClearOrderSupply();return;}
            messages.GetUnstockedSalvageNeeds(orderNeeds);
            // Keep exactly the currently unstocked quantities, not a second set of
            // generic loot. Cargo, tube deliveries and bay inventory already count.
            foreach(var piece in new List<SalvagePiece>(SalvagePiece.All))
            {
                if(!piece||piece.gameObject.scene!=gameObject.scene)continue;
                if(Owns(piece)&&(!piece.SpawnPoint||CanSupply(piece.SpawnPoint))&&
                    orderNeeds.TryGetValue(piece.prefab,out int count)&&count>0)
                    orderNeeds[piece.prefab]=count-1;
                else RemovePiece(piece);
            }
            fallbackPieces.RemoveAll(p=>!p||p.Collected||!p.gameObject.activeInHierarchy);
            // Randomize across equally eligible routes, keeping this order's power budget.
            // A difficult cache replaces at most one safe target.
            for(int attempt=0;attempt<(spawnPoints.Count>0?spawnPoints.Count:maxPieces);attempt++)
            {
                var needed=NextNeededPart();
                if(!needed)break;
                if(spawnPoints.Count==0)
                {
                    if(!TrySpawn(needed))break;
                    orderNeeds[needed]--;continue;
                }
                var point=chooseManipulatorCache?ChooseSupplyPoint(true):null;
                if(!point)point=ChooseSupplyPoint(false);
                if(!point)break;
                SpawnAt(point,needed);
                if(!point.Piece)continue;
                orderAssignments[point]=needed;usedThisOrder.Add(point);orderNeeds[needed]--;
                if(point.requiresManipulator)chooseManipulatorCache=false;
            }
        }
        GameObject NextNeededPart()
        {
            int total=0;foreach(var pair in orderNeeds)total+=Mathf.Max(0,pair.Value);
            if(total==0)return null;
            int pick=Random.Range(0,total);
            foreach(var pair in orderNeeds){pick-=Mathf.Max(0,pair.Value);if(pick<0)return pair.Key;}
            return null;
        }
        SalvageSpawnPoint ChooseSupplyPoint(bool manipulator)
        {
            SalvageSpawnPoint best=null;float bestScore=-1;
            foreach(var point in spawnPoints)
            {
                if(!CanSupply(point) || point.requiresManipulator!=manipulator ||
                    (link.robot && Flat(link.robot.position-point.transform.position).magnitude<4))continue;
                if(orderAssignments.ContainsKey(point) && !point.Free)continue;
                float separation=targetSpacing*3;
                foreach(var used in usedThisOrder)
                    if(used && used!=point)separation=Mathf.Min(separation,Flat(used.transform.position-point.transform.position).magnitude);
                float score=Random.Range(.85f,1.15f)*separation;
                if(separation<targetSpacing)score*=.15f;
                if(previousOrderPoints.Contains(point))score*=.25f;
                if(usedThisOrder.Contains(point))score*=.02f;
                if(score>bestScore){bestScore=score;best=point;}
            }
            return best;
        }
        void SpawnAt(SalvageSpawnPoint point,GameObject requiredPart)
        {
            if(!HasOrder||!requiredPart||!Allowed(point)||!point.Free)return;
            var position = point.transform.position;
            var normal = point.transform.up;
            if (Ground(position + Vector3.up, 100, out var ground, out var groundNormal))
            {
                position = ground;
                normal = groundNormal;
            }
            point.Piece = SalvagePiece.Create(requiredPart, position, normal, link.robotScale * 0.5f);
            point.Piece.SpawnPoint=point;
        }

        bool TrySpawn(GameObject requiredPart)
        {
            if(!HasOrder||!requiredPart||fallbackPieces.Count>=maxPieces)return false;
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
                fallbackPieces.Add(SalvagePiece.Create(requiredPart,point,normal,link.robotScale*.5f));
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
            Near = Closest = null;
            ClosestDistance = float.MaxValue;
            foreach (var piece in SalvagePiece.All)
            {
                float distance = Flat(piece.transform.position - link.robot.position).magnitude;
                if (distance < ClosestDistance) { ClosestDistance = distance; Closest = piece; }
                if ((!Near || distance < Flat(Near.transform.position - link.robot.position).magnitude) && CanPickUp(piece)) Near = piece;
            }
            if (link.state) link.state.Cargo = cargo.Count;
            if (cargo.Count > 0 && link.InChargeBuilding) Unload();
        }

        public void PickUp(ScavengerFeedHud hud)
        {
            if (!Near || !CanPickUp(Near)) return;
            messages.GetUnstockedSalvageNeeds(orderNeeds);
            if(!orderNeeds.TryGetValue(Near.prefab,out int needed)||needed<=0){EnsureOrderSupply();return;}
            if (Full) { hud.Notify(Loc.T("CARGO FULL - UNLOAD AT THE CHARGE STATION")); return; }
            var piece = Near;
            cargo.Add(piece.prefab);
            if(link.state)link.state.Cargo=cargo.Count;
            piece.Collect(link.robot);
            Near = null;
            hud.Notify(Loc.F("{0} LOADED  CARGO {1}/{2}", PartName(piece.prefab), cargo.Count, link.state.cargoCapacity));
        }

        internal bool CanPickUp(SalvagePiece piece)
        {
            if(!HasOrder||!Owns(piece)||!piece || piece.Collected || !link || !link.robot ||
                (piece.SpawnPoint && piece.SpawnPoint.requiresManipulator && !RobotUpgrades.HasArm))return false;
            float length=link.Drive?link.Drive.Length:1.5f;
            var origin=link.robot.position+Vector3.up*(length*.25f);
            var offset=piece.PickupPosition-origin;
            if(offset.magnitude>length*1.1f+1.5f+piece.radius)return false;
            int count=Physics.RaycastNonAlloc(origin,offset.normalized,hits,offset.magnitude,groundLayers,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return false;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];
                if(hit.transform.IsChildOf(link.robot) || hit.transform.IsChildOf(piece.transform) ||
                    hit.collider.GetComponentInParent<ScavengerRobotState>() || hit.collider.GetComponentInParent<FirstPersonController>())continue;
                return false;
            }
            return true;
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
