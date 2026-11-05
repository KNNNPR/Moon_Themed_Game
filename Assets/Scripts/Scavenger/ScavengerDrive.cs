using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // Arcade rally dynamics on the existing sphere-cast wheel suspension.
    // Each wheel is a sphere
    // of its own radius, so its rim catches the edge of a step and rolls up onto it like a
    // car onto a curb (up to about one wheel radius). Wheels are supported only by
    // real colliders, so bridge edges and drops do not create invisible ground.
    [DisallowMultipleComponent]
    public sealed class ScavengerDrive : MonoBehaviour
    {
        sealed class Wheel
        {
            public Transform transform;
            public Vector3 anchor;
            public Vector3 centerInParent, pivotInParent, axleInParent;
            public Quaternion restRotation;
            public float radius, length, spin, visualLift;
            public bool grounded;
            public bool frontAxle;
            public bool rearAxle;
            public Vector3 contact, normal;
        }

        [Header("Drive (robot lengths)")]
        public float maxSpeed = 4.2f;
        public float reverseFactor = 0.5f;
        public float acceleration = 4.1f;
        [Tooltip("Maximum yaw speed in degrees per second; steering follows a car-like arc.")]
        public float turnSpeed = 165;
        [Tooltip("How fast throttle follows the keys, per second.")]
        public float throttleResponse = 12;
        public float steerResponse = 16;
        [Range(0, 1)] public float lateralGrip = 0.14f;
        public float coastDrag = 0.6f;
        [Min(0)] public float rollingResistance = 0.18f;
        [Min(0.1f)] public float braking = 8.5f;
        [Range(5, 55)] public float steeringAngle = 35;
        [Range(0, 0.8f)] public float highSpeedSteeringReduction = 0.5f;
        [Min(1)] public float yawResponse = 16;

        [Header("Rally / handbrake")]
        [Range(0.05f, 1)] public float driftRearGrip = 0.07f;
        [Min(0.1f)] public float gripRecovery = 2.1f;
        [Min(0)] public float handbrakeDeceleration = 0.7f;
        [Range(0, 2)] public float handbrakeYawBoost = 0.9f;
        [Range(0, 2)] public float speedDownforce = 1.25f;

        [Header("Stability")]
        [Tooltip("Wheel contacts whose normal points up less than this (world Y) are ignored as walls.")]
        [Range(0, 1)] public float maxGroundSteepness = 0.3f;
        [Tooltip("Body tilt (up.y) where traction starts to fade and where it is gone.")]
        public Vector2 tractionTilt = new Vector2(0.8f, 0.55f);
        [Tooltip("How far drive and brake forces are raised from the tyre contact toward the centre of mass; 1 = no pitching.")]
        [Range(0, 1)] public float pitchFreeDrive = 0.9f;
        [Range(0, 1)] public float rollFreeGrip = 0.6f;

        [Header("Body")]
        public float mass = 400;
        public float gravity = 4;
        [Tooltip("Suspension travel as a share of the wheel radius.")]
        public float travel = 0.6f;
        [Range(0, 2)] public float dampingRatio = 1.1f;

        public Vector3 localForward = Vector3.forward;
        public bool controllable;
        [Min(0.1f)] public float recoveryDelay = 0.6f;

        readonly List<Wheel> wheels = new List<Wheel>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly Collider[] recoveryOverlaps = new Collider[32];
        Rigidbody body;
        BoxCollider hull;
        Collider[] own;
        float throttle, steer, length, wheelBase, stiffness, damper, overturnedFor;
        float drift, restClearance, driftSign=1;
        bool wasHandbrake;

        public float Length => length;
        public Vector3 Forward => transform.TransformDirection(localForward);
        public Vector3 Heading
        {
            get
            {
                var flat = Forward;
                flat.y = 0;
                return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            }
        }
        public float ForwardSpeed => body ? Vector3.Dot(body.linearVelocity, Forward) : 0;
        public Rigidbody Body => body;
        public float MaximumForwardSpeed => maxSpeed * length;
        public bool IsOverturned => body && Vector3.Dot(transform.up,Vector3.up)<0.25f;
        public float DriftAmount => drift;
        public bool IsForwardDrift => drift>0.1f && driftSign>0;
        public float SlipAngle
        {
            get
            {
                if(!body)return 0;
                var velocity=Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up);
                return velocity.sqrMagnitude>length*length*0.01f ? Vector3.SignedAngle(Heading,velocity,Vector3.up) : 0;
            }
        }
        public bool CanRecover => controllable && IsOverturned && overturnedFor>=recoveryDelay &&
            body.linearVelocity.sqrMagnitude<length*length && body.angularVelocity.sqrMagnitude<6.25f &&
            TryRecoveryPose(out _,out _);
        public float CurrentSteeringAngle => steer * steeringAngle * Mathf.Lerp(1, 1-highSpeedSteeringReduction,
            Mathf.Clamp01(Mathf.Abs(ForwardSpeed)/Mathf.Max(0.01f, MaximumForwardSpeed)));

        public void Setup(Vector3 forward)
        {
            localForward = forward;
            var renderers = GetComponentsInChildren<Renderer>();
            var local = LocalBounds(renderers);
            var world = WorldBounds(renderers);
            length = Mathf.Max(world.size.x, world.size.z);
            restClearance = transform.position.y-world.min.y;

            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            var axle = Vector3.Cross(transform.up, Forward).normalized;
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (!child.name.StartsWith("Wheel")) continue;
                var r = child.GetComponentInChildren<Renderer>();
                if (!r) continue;
                var parent = child.parent;
                var center = r.bounds.center;
                wheels.Add(new Wheel
                {
                    transform = child,
                    anchor = transform.InverseTransformPoint(center),
                    centerInParent = parent.InverseTransformPoint(center),
                    pivotInParent = child.localPosition - parent.InverseTransformPoint(center),
                    axleInParent = parent.InverseTransformDirection(axle),
                    restRotation = child.localRotation,
                    radius = r.bounds.extents.y
                });
            }

            float wheelRadius = wheels.Count > 0 ? wheels[0].radius : world.size.y * 0.25f;
            float front = float.MinValue, rear = float.MaxValue;
            foreach (var wheel in wheels)
            {
                float along = Vector3.Dot(transform.TransformPoint(wheel.anchor)-world.center, Forward.normalized);
                front = Mathf.Max(front, along); rear = Mathf.Min(rear, along);
            }
            wheelBase = wheels.Count > 1 ? Mathf.Max(length * 0.35f, front-rear) : length * 0.6f;
            foreach (var wheel in wheels)
            {
                wheel.frontAxle = Vector3.Dot(transform.TransformPoint(wheel.anchor)-world.center, Forward.normalized) >= front-wheelBase*0.2f;
                wheel.rearAxle = Vector3.Dot(transform.TransformPoint(wheel.anchor)-world.center, Forward.normalized) <= rear+wheelBase*0.2f;
            }
            foreach (var wheel in wheels) wheel.length = wheel.radius * travel;
            hull = gameObject.AddComponent<BoxCollider>();
            float bottom = local.min.y + wheelRadius * 1.5f / Mathf.Max(0.001f, transform.lossyScale.y);
            hull.center = new Vector3(local.center.x, (bottom + local.max.y) * 0.5f, local.center.z);
            hull.size = new Vector3(local.size.x * 0.85f, Mathf.Max(0.01f, local.max.y - bottom), local.size.z * 0.85f);
            hull.sharedMaterial = new PhysicsMaterial("Scavenger hull")
            {
                dynamicFriction = 0, staticFriction = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
            };
            own = new Collider[] { hull };

            body = gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0.05f;
            body.angularDamping = 1.5f;
            body.solverIterations = 12;
            body.centerOfMass = transform.InverseTransformPoint(new Vector3(world.center.x, world.min.y + wheelRadius*0.65f, world.center.z));

            int count = Mathf.Max(1, wheels.Count);
            float sag = travel * wheelRadius * 0.5f;
            stiffness = mass * gravity / count / Mathf.Max(0.001f, sag);
            damper = 2 * dampingRatio * Mathf.Sqrt(stiffness * mass / count);
        }

        Bounds LocalBounds(Renderer[] renderers)
        {
            bool first = true;
            var result = new Bounds();
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(corner);
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        static Bounds WorldBounds(Renderer[] renderers)
        {
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            throttle = steer = drift = overturnedFor = 0;
            wasHandbrake=false;driftSign=1;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        void FixedUpdate()
        {
            SimulateStep(ReadInput(), Time.fixedDeltaTime, ReadHandbrake());
        }

        // Also accepts external vehicle input (touch controls, AI or a deterministic physics run).
        public void SimulateStep(Vector2 input, float dt, bool handbrake=false)
        {
            if (!body || dt <= 0) return;
            input = controllable && !IsOverturned ? new Vector2(Mathf.Clamp(input.x,-1,1),Mathf.Clamp(input.y,-1,1)) : Vector2.zero;
            handbrake &= controllable && !IsOverturned;
            if(handbrake && !wasHandbrake)driftSign=ForwardSpeed<0?-1:1;
            wasHandbrake=handbrake;
            float planarSpeed=Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up).magnitude;
            float driftTarget=handbrake && planarSpeed>length*0.4f ? 1 : 0;
            drift=Mathf.MoveTowards(drift,driftTarget,(driftTarget>drift?7:gripRecovery)*dt);
            overturnedFor=IsOverturned?overturnedFor+dt:0;
            throttle = Mathf.MoveTowards(throttle, input.y, throttleResponse * dt);
            steer = Mathf.MoveTowards(steer, input.x, steerResponse * (Mathf.Abs(input.x)<0.01f?1.3f:1) * dt);

            body.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            int grounded = 0;
            var up = transform.up;
            foreach (var wheel in wheels)
            {
                Suspension(wheel, up, dt);
                if (wheel.grounded) grounded++;
            }
            if (grounded > 0 && !IsOverturned)
            {
                Traction(grounded, input, dt, handbrake);
                if(grounded>=2 && Vector3.Dot(up,Vector3.up)>0.5f)
                {
                    float speed=Mathf.Clamp01(Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up).magnitude/Mathf.Max(0.01f,MaximumForwardSpeed));
                    body.AddForce(-up*gravity*speedDownforce*speed*speed,ForceMode.Acceleration);
                }
            }
        }

        // `length` is how far the wheel centre hangs below its mount point.
        void Suspension(Wheel wheel, Vector3 up, float dt)
        {
            float rest = wheel.radius * travel;
            var mount = transform.TransformPoint(wheel.anchor) + up * rest;
            float lead = wheel.radius;
            var origin = mount + up * lead;
            float previous = wheel.length;
            wheel.grounded = CastGround(origin, -up, wheel.radius * 0.95f, rest * 2 + lead, out float distance, out wheel.contact, out wheel.normal);
            wheel.length = wheel.grounded ? Mathf.Max(0, distance - lead) : rest * 2;
            if (!wheel.grounded) return;
            float compression = rest * 1.5f - wheel.length;
            float velocity = (previous - wheel.length) / dt;
            // Capped so a sudden nose-dive cannot fire the bot into the air off its own springs.
            float force = Mathf.Clamp(stiffness * compression + damper * velocity, 0, stiffness * rest * 3);
            body.AddForceAtPosition(up * force, wheel.contact);
        }

        bool CastGround(Vector3 origin, Vector3 direction, float radius, float reach, out float distance, out Vector3 contact, out Vector3 normal)
        {
            distance = float.MaxValue;
            contact = origin;
            normal = Vector3.up;
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, reach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance <= 0 || System.Array.IndexOf(own, hit.collider) >= 0 || hit.collider.attachedRigidbody == body) continue;
                // A wall face is not ground: standing on it is what lets the wheels drive up vertical surfaces.
                if (hit.normal.y < maxGroundSteepness) continue;
                if (hit.distance < distance) { distance = hit.distance; contact = hit.point; normal = hit.normal; }
            }
            return distance < float.MaxValue;
        }

        void Traction(int grounded, Vector2 input, float dt, bool handbrake)
        {
            var forward = Forward;
            float speed = Vector3.Dot(body.linearVelocity, forward);
            float limit = MaximumForwardSpeed * (throttle < 0 ? reverseFactor : 1);
            float rate = (speed * input.y < -length*0.02f || !controllable) ? braking : acceleration;
            float accel = Mathf.Clamp((throttle*limit-speed)/dt, -rate*length, rate*length);
            if (controllable && Mathf.Abs(input.y)<0.01f)
                accel = Mathf.Clamp((-speed*coastDrag-Mathf.Sign(speed)*rollingResistance*length)*(handbrake?0.15f:1), -Mathf.Abs(speed)/dt, Mathf.Abs(speed)/dt);
            // Keep momentum during a handbrake turn; deliberate reverse input still brakes.
            if(handbrake && input.y*driftSign>=0)accel*=0.35f;
            float support = Mathf.Clamp01(grounded/Mathf.Max(1f,wheels.Count*0.65f));
            support *= Mathf.InverseLerp(tractionTilt.y, tractionTilt.x, transform.up.y);
            var planarVelocity=Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up);
            if(handbrake && planarVelocity.sqrMagnitude>0.0001f)
                body.AddForce(-planarVelocity.normalized*Mathf.Min(handbrakeDeceleration*length,planarVelocity.magnitude/dt)*support,ForceMode.Acceleration);
            // Reference-step grip makes the small amount of tyre slip consistent at any physics rate.

            // Side slip is taken from the body's travel, not per wheel, so skid-steering still turns.
            // Forces follow the body's own plane, not each contact's normal, so a steep contact cannot steer thrust upward.
            var up = transform.up;
            var drive = Vector3.ProjectOnPlane(forward, up).normalized;
            var side = Vector3.Cross(up, drive);
            var centre = body.worldCenterOfMass;
            foreach (var wheel in wheels)
            {
                if (!wheel.grounded) continue;
                float slip = Vector3.Dot(body.linearVelocity, side);
                float axleGrip=wheel.frontAxle?1:Mathf.Lerp(1,wheel.rearAxle?driftRearGrip:Mathf.Sqrt(driftRearGrip),drift);
                float grip=(1-Mathf.Pow(Mathf.Max(0.001f,1-lateralGrip*axleGrip),dt/0.02f))/dt;
                float share = body.mass * support / grounded;
                // Hard braking at tyre height tips the bot over its nose; raise the push toward the centre of mass.
                float height = Vector3.Dot(centre - wheel.contact, up);
                body.AddForceAtPosition(drive * accel * share, wheel.contact + up * (height * pitchFreeDrive));
                body.AddForceAtPosition(-side * slip * grip * share, wheel.contact + up * (height * rollFreeGrip));
            }

            float yaw = Vector3.Dot(body.angularVelocity, up);
            // Signed speed reverses the steering in reverse, just like an RC car.
            // Stationary steering turns the front wheels without spinning the whole rover.
            // Preserve the entry direction while sliding sideways; crossing 90 degrees must
            // not reverse the steering servo and cancel a handbrake U-turn.
            float steeringSpeed=Mathf.Lerp(speed,planarVelocity.magnitude*driftSign,drift);
            float wanted = Mathf.Clamp(steeringSpeed / Mathf.Max(0.01f,wheelBase) * Mathf.Tan(CurrentSteeringAngle*Mathf.Deg2Rad)*(1+handbrakeYawBoost*drift),
                -turnSpeed*Mathf.Deg2Rad, turnSpeed*Mathf.Deg2Rad);
            float yawAccel = Mathf.Clamp((wanted-yaw)*yawResponse, -Mathf.PI*5, Mathf.PI*5);
            body.AddTorque(up*yawAccel*support, ForceMode.Acceleration);
        }

        public bool RecoverUpright()
        {
            if(!CanRecover || !TryRecoveryPose(out var position,out var rotation))return false;
            PlaceAt(position,rotation);
            return true;
        }

        bool TryRecoveryPose(out Vector3 position,out Quaternion rotation)
        {
            position=Vector3.zero;
            rotation=Quaternion.LookRotation(Heading,Vector3.up)*Quaternion.Inverse(Quaternion.LookRotation(localForward,Vector3.up));
            var origin=body.worldCenterOfMass+Vector3.up*length*0.1f;
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,hits,length,~0,QueryTriggerInteraction.Ignore);
            float nearest=float.MaxValue;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];
                if(hit.collider.attachedRigidbody==body || hit.transform.IsChildOf(transform) || hit.normal.y<0.45f)continue;
                if(hit.distance<nearest) { nearest=hit.distance;position=hit.point; }
            }
            if(nearest==float.MaxValue)return false;
            position=new Vector3(body.position.x,position.y+restClearance+length*0.08f,body.position.z);
            var scale=transform.lossyScale;
            var center=position+rotation*Vector3.Scale(hull.center,scale);
            var half=Vector3.Scale(hull.size,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)))*0.48f;
            count=Physics.OverlapBoxNonAlloc(center,half,recoveryOverlaps,rotation,~0,QueryTriggerInteraction.Ignore);
            if(count==recoveryOverlaps.Length)return false;
            for(int i=0;i<count;i++)
                if(recoveryOverlaps[i].attachedRigidbody!=body && !recoveryOverlaps[i].transform.IsChildOf(transform))return false;
            return true;
        }

        void LateUpdate()
        {
            var up = transform.up;
            float speed = ForwardSpeed;
            foreach (var wheel in wheels)
            {
                float lift = wheel.radius * travel - wheel.length;
                wheel.visualLift = Mathf.Lerp(wheel.visualLift, lift, 1 - Mathf.Exp(-25 * Time.deltaTime));
                var parent = wheel.transform.parent;
                wheel.spin += speed / Mathf.Max(0.01f, wheel.radius) * Mathf.Rad2Deg * Time.deltaTime;
                var spin = Quaternion.AngleAxis(wheel.spin, wheel.axleInParent);
                var turn = Quaternion.AngleAxis(wheel.frontAxle ? CurrentSteeringAngle : 0, parent.InverseTransformDirection(up));
                var offset = parent.InverseTransformVector(up * wheel.visualLift);
                wheel.transform.localPosition = wheel.centerInParent + offset + turn * spin * wheel.pivotInParent;
                wheel.transform.localRotation = turn * spin * wheel.restRotation;
            }
        }

        static Vector2 ReadInput()
        {
            if(MoonThemedGame.Interactions.DeveloperConsole.BlockGameplayInput)return Vector2.zero;
            var input = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.Forward)) input.y += 1;
                if (MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.Backward)) input.y -= 1;
                if (MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.Right)) input.x += 1;
                if (MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.Left)) input.x -= 1;
            }
            if (Gamepad.current != null)
            {
                var stick=Gamepad.current.leftStick.ReadValue();
                float triggers=Gamepad.current.rightTrigger.ReadValue()-Gamepad.current.leftTrigger.ReadValue();
                if(Mathf.Abs(triggers)>Mathf.Abs(stick.y))stick.y=triggers;
                if(Mathf.Abs(stick.x)>Mathf.Abs(input.x))input.x=stick.x;
                if(Mathf.Abs(stick.y)>Mathf.Abs(input.y))input.y=stick.y;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.W)) input.y += 1;
            if (Input.GetKey(KeyCode.S)) input.y -= 1;
            if (Input.GetKey(KeyCode.D)) input.x += 1;
            if (Input.GetKey(KeyCode.A)) input.x -= 1;
#endif
            return input;
        }

        static bool ReadHandbrake()
        {
            if(MoonThemedGame.Interactions.DeveloperConsole.BlockGameplayInput)return false;
#if ENABLE_INPUT_SYSTEM
            return MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.RobotHandbrake) ||
                (Gamepad.current!=null && Gamepad.current.buttonSouth.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Space);
#else
            return false;
#endif
        }
    }
}
