using MoonThemedGame.Audio;
using System.Collections.Generic;
using MoonThemedGame.Interactions;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [RequireComponent(typeof(CharacterController)), DisallowMultipleComponent]
    public sealed class ZombieActor : MonoBehaviour
    {
        [Header("Behaviour")]
        [Min(1)] public float maximumHealth = 100;
        [Min(0)] public float walkSpeed = .95f;
        [Min(1)] public float detectionRange = 28;
        [Min(.2f)] public float attackReach = 1.25f;
        [Min(.1f)] public float attackInterval = 1.4f;
        [Min(0)] public float corpseLifetime = 35;
        public Material impactMaterial;
        public ZombieRagdoll ragdoll;
        [HideInInspector] public Transform targetOverride;
        public System.Action<ZombieActor> ContactAttack;
        public System.Func<Vector3> NavigationTarget;
        [Header("Pose")]
        public Transform model;
        public Transform head, spine, leftUpperArm, rightUpperArm, leftForearm, rightForearm, leftThigh, rightThigh, leftShin, rightShin;
        public float Health { get; private set; }
        public bool IsDead => Health <= 0;
        public int ShotsReceived { get; private set; }
        public int Attacks { get; private set; }
        public int LegHits { get; private set; }
        public ZombieBodyPart LastHitPart { get; private set; }
        public float HitReactionAmount => Mathf.Clamp01((reactionUntil - Time.time) / .65f);

        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        CharacterController motor;
        FirstPersonController player;
        Vector3 lastSeen, knockback, modelPosition;
        float memory, vertical, stride, moving, nextAttack, stagger, nextFind;
        float reactionUntil;
        Transform hitBone;
        Vector3 hitDirection;
        float Unit => Mathf.Abs(transform.lossyScale.y);
        static readonly float[] AvoidanceAngles = { 0f, -35, 35, -70, 70, -105, 105 };

        void Awake()
        {
            Health = maximumHealth;GameAudio.WatchZombie(this);
            motor = GetComponent<CharacterController>();
            if (!ragdoll) ragdoll = GetComponent<ZombieRagdoll>();
            if (!ragdoll) ragdoll = ZombieRagdoll.Configure(this);
            foreach (var collider in GetComponentsInChildren<Collider>())
                if (collider != motor) Physics.IgnoreCollision(motor, collider);
            if (model)
            {
                modelPosition = model.localPosition;
                foreach (var t in model.GetComponentsInChildren<Transform>()) rest[t] = t.localRotation;
            }
        }

        void Update()
        {
            if (Time.deltaTime <= 0 || GameSettingsMenu.BlockGameplayInput || DeveloperConsole.BlockGameplayInput) return;
            float dt = Time.deltaTime;
            if (IsDead) return;
            if (!targetOverride && (!player || !player.isActiveAndEnabled) && Time.time >= nextFind)
            {
                player = FindFirstObjectByType<FirstPersonController>(); nextFind = Time.time + 1;
            }
            var velocity = Vector3.zero;
            var target = targetOverride ? targetOverride : player && player.isActiveAndEnabled ? player.transform : null;
            if (target && target.gameObject.activeInHierarchy)
            {
                var delta = target.position - transform.position;
                if (delta.magnitude < detectionRange * Unit && (targetOverride || CanSeeTarget(target))) { lastSeen = target.position; memory = 6; }
                else memory = Mathf.Max(0, memory - dt);
                if (memory > 0)
                {
                    var flat = Vector3.ProjectOnPlane((NavigationTarget != null ? NavigationTarget() : lastSeen) - transform.position, Vector3.up);
                    if (flat.magnitude > attackReach * Unit * .85f)
                    {
                        var direction = Steer(flat.normalized);
                        if (direction.sqrMagnitude > .01f)
                        {
                            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 110 * dt);
                            float limp = Mathf.Max(.28f, 1 - LegHits * .18f);
                            velocity = transform.forward * (walkSpeed * limp * Unit * (Time.time < stagger ? .15f : 1));
                        }
                    }
                    if (delta.magnitude < attackReach * Unit && Time.time >= nextAttack && CanSeeTarget(target))
                    {
                        nextAttack = Time.time + attackInterval; Attacks++;
                        if (targetOverride) ContactAttack?.Invoke(this);
                        else
                        {
                            player.AddViewKick(-3, Random.Range(-2f, 2f));
                            var capsule = player.GetComponent<CharacterController>();
                            if (capsule && capsule.enabled) capsule.Move(delta.normalized * (.18f * Unit));
                        }
                    }
                }
            }
            moving = Mathf.MoveTowards(moving, velocity.magnitude > .01f ? 1 : 0, dt * 3);
            stride += dt * (2.8f + walkSpeed);
            vertical = motor.isGrounded && vertical < 0 ? -2 * Unit : vertical - 12 * Unit * dt;
            motor.Move((velocity + knockback + Vector3.up * vertical) * dt);
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, Unit * 5 * dt);
        }

        bool CanSeeTarget(Transform targetRoot)
        {
            var eye = transform.position + Vector3.up * (1.45f * Unit);
            var capsule = targetRoot.GetComponent<CharacterController>();
            var target = capsule ? capsule.bounds.center : targetRoot.position + Vector3.up * Unit;
            var delta = target - eye;
            foreach (var hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(targetRoot)) return false;
            return true;
        }

        Vector3 Steer(Vector3 wanted)
        {
            var origin = transform.position + Vector3.up * (motor.height * .45f * Unit);
            foreach (float angle in AvoidanceAngles)
            {
                var direction = Quaternion.AngleAxis(angle, Vector3.up) * wanted;
                bool blocked = false;
                foreach (var hit in Physics.SphereCastAll(origin, motor.radius * Unit * .85f, direction, .7f * Unit, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.transform.IsChildOf(transform) && (!targetOverride || !hit.transform.IsChildOf(targetOverride)) && (!player || !hit.transform.IsChildOf(player.transform))) { blocked = true; break; }
                if (!blocked) return direction;
            }
            return Vector3.zero;
        }

        public bool ReceiveHit(ZombieHitZone zone, float damage, Vector3 point, Vector3 normal, Vector3 direction)
        {
            if (IsDead || !zone || zone.zombie != this || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return false;
            Health = Mathf.Max(0, Health - damage * zone.damageMultiplier);
            ShotsReceived++; LastHitPart = zone.bodyPart;
            hitBone = zone.transform.parent;
            hitDirection = direction.normalized;
            reactionUntil = Time.time + .65f;
            if (zone.bodyPart == ZombieBodyPart.Leg) LegHits++;
            stagger = Time.time + .3f;
            knockback += Vector3.ProjectOnPlane(direction, Vector3.up).normalized * (.7f * Unit);
            ZombieImpact.Emit(point, normal, Unit, impactMaterial);
            if (IsDead)
            {
                var velocity = motor.enabled ? motor.velocity : Vector3.zero;
                memory = 0; motor.enabled = false;
                ragdoll.Activate(zone, point, direction, velocity);
                Destroy(gameObject, corpseLifetime);
            }
            return true;
        }

        public float PushByVehicle(Vector3 direction,float distance)
        {
            if(IsDead||!motor||!motor.enabled||distance<=0)return 0;
            direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            if(direction.sqrMagnitude<.01f)return 0;
            var before=transform.position;
            motor.Move(direction*distance);
            float moved=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;
            if(moved>.001f)
            {
                knockback=direction*Mathf.Max(knockback.magnitude,Unit*.8f);
                stagger=Time.time+.25f;
                LastHitPart=ZombieBodyPart.Torso;hitDirection=direction;reactionUntil=Time.time+.3f;
            }
            return moved;
        }

        public bool RunOver(Vector3 point,Vector3 direction,Vector3 vehicleVelocity)
        {
            if(IsDead)return false;
            var zone=System.Array.Find(GetComponentsInChildren<ZombieHitZone>(),z=>z.bodyPart==ZombieBodyPart.Torso);
            if(!zone)return false;
            Health=0;memory=0;
            var velocity=motor.enabled?motor.velocity:Vector3.zero;motor.enabled=false;
            ZombieImpact.Emit(point,-direction,Unit,impactMaterial);
            var planar=Vector3.ProjectOnPlane(vehicleVelocity,Vector3.up);
            float speed=planar.magnitude;
            var forward=planar.sqrMagnitude>.01f?planar.normalized:Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            float lift=Mathf.Min(4*Unit,speed*.22f+.7f*Unit);
            var launch=velocity*.2f+forward*Mathf.Min(speed*.95f,24*Unit)+Vector3.up*lift;
            ragdoll.Activate(zone,point,forward,launch);
            ragdoll.AddVehicleTumble(forward,speed,point);
            Destroy(gameObject,corpseLifetime);return true;
        }

        void LateUpdate()
        {
            if (!model || IsDead) return;
            foreach (var pair in rest) pair.Key.localRotation = pair.Value;
            float t = Time.time;
            model.localPosition = modelPosition + Vector3.up * (Mathf.Sin(stride * 2) * .014f * moving);
            Rotate(spine, transform.right, 8 + Mathf.Sin(t * .7f) * 2);
            Arm(leftUpperArm, leftForearm, -.22f); Arm(rightUpperArm, rightForearm, .22f);
            Rotate(leftThigh, transform.right, Mathf.Sin(stride) * 18 * moving);
            Rotate(rightThigh, transform.right, -Mathf.Sin(stride) * 18 * moving);
            Rotate(leftShin, transform.right, Mathf.Max(0, -Mathf.Sin(stride)) * 20 * moving);
            Rotate(rightShin, transform.right, Mathf.Max(0, Mathf.Sin(stride)) * 20 * moving);
            Rotate(head, transform.forward, Mathf.Sin(t * .8f) * 4);
            float reaction = HitReactionAmount;
            reaction = reaction * reaction * (3 - 2 * reaction);
            if (reaction > 0)
            {
                var side = Vector3.Dot(hitDirection, transform.right);
                Rotate(spine, transform.forward, -side * 11 * reaction);
                switch (LastHitPart)
                {
                    case ZombieBodyPart.Head:
                        Rotate(head, transform.right, -24 * reaction);
                        Rotate(head, transform.up, side * 28 * reaction);
                        Rotate(spine, transform.right, -9 * reaction);
                        break;
                    case ZombieBodyPart.Torso:
                        Rotate(spine, transform.right, -17 * reaction);
                        Rotate(head, transform.right, 10 * reaction);
                        break;
                    case ZombieBodyPart.Arm:
                        Rotate(hitBone, transform.right, -35 * reaction);
                        Rotate(hitBone, transform.forward, (hitBone && hitBone.name.EndsWith(".L") ? 1 : -1) * 18 * reaction);
                        break;
                    case ZombieBodyPart.Leg:
                        Rotate(hitBone, transform.right, 30 * reaction);
                        model.localPosition -= Vector3.up * (.09f * reaction);
                        Rotate(spine, transform.right, 12 * reaction);
                        break;
                }
            }
            void Arm(Transform upper, Transform forearm, float side)
            {
                if (!upper || !forearm) return;
                var hand = forearm.childCount > 0 ? forearm.GetChild(0) : null;
                var desired = (transform.forward + transform.right * side + transform.up * (.03f + Mathf.Sin(t * 1.2f + side) * .04f)).normalized;
                upper.rotation = Quaternion.FromToRotation(forearm.position - upper.position, desired) * upper.rotation;
                if (hand) forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, desired + transform.up * .07f) * forearm.rotation;
            }
        }

        static void Rotate(Transform bone, Vector3 axis, float degrees)
        { if (bone) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }
    }
}
