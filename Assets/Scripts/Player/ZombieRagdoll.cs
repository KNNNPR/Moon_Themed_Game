using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class ZombieRagdoll : MonoBehaviour
    {
        [Tooltip("Impulse in suit metres, scaled with the spawned zombie. Applied to the struck bone.")]
        [Min(0)] public float deathImpulse = 8;
        public Rigidbody[] bodies = new Rigidbody[0];
        public bool IsActive { get; private set; }

        public static ZombieRagdoll Configure(ZombieActor actor)
        {
            var ragdoll = actor.GetComponent<ZombieRagdoll>();
            if (!ragdoll) ragdoll = actor.gameObject.AddComponent<ZombieRagdoll>();
            var zones = actor.GetComponentsInChildren<ZombieHitZone>(true);
            var bodies = new List<Rigidbody>();
            foreach (var zone in zones)
            {
                var bone = zone.transform.parent;
                var body = bone.GetComponent<Rigidbody>();
                if (!body) body = bone.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.mass = zone.bodyPart == ZombieBodyPart.Torso ? 28 : zone.bodyPart == ZombieBodyPart.Head ? 6
                    : zone.bodyPart == ZombieBodyPart.Arm ? (zone.name.StartsWith("Upper") ? 3 : 2)
                    : (zone.name.StartsWith("Thigh") ? 8 : 5);
                body.linearDamping = .12f;
                body.angularDamping = 1.25f;
                body.solverIterations = 12;
                body.solverVelocityIterations = 8;
                body.maxAngularVelocity = 12;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                if (!bodies.Contains(body)) bodies.Add(body);
            }
            foreach (var zone in zones)
            {
                if (zone.bodyPart == ZombieBodyPart.Torso) continue;
                var bone = zone.transform.parent;
                Rigidbody parentBody = null;
                for (var parent = bone.parent; parent && parent != actor.transform; parent = parent.parent)
                    if (parent.TryGetComponent(out parentBody)) break;
                if (!parentBody) continue;
                var joint = bone.GetComponent<CharacterJoint>();
                if (!joint) joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parentBody;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = parentBody.transform.InverseTransformPoint(bone.position);
                bool hinge = zone.name.StartsWith("Forearm") || zone.name.StartsWith("Shin");
                var axis = hinge ? actor.transform.right : zone.transform.up;
                joint.axis = bone.InverseTransformDirection(axis).normalized;
                joint.swingAxis = Vector3.ProjectOnPlane(bone.InverseTransformDirection(hinge ? actor.transform.forward : actor.transform.right), joint.axis).normalized;
                joint.lowTwistLimit = new SoftJointLimit { limit = hinge ? -5 : -35 };
                joint.highTwistLimit = new SoftJointLimit { limit = hinge ? 125 : 35 };
                joint.swing1Limit = new SoftJointLimit { limit = hinge ? 8 : zone.bodyPart == ZombieBodyPart.Arm ? 70 : 45 };
                joint.swing2Limit = new SoftJointLimit { limit = hinge ? 8 : zone.bodyPart == ZombieBodyPart.Head ? 35 : 50 };
                joint.enablePreprocessing = false;
                joint.enableCollision = false;
                joint.enableProjection = true;
                joint.projectionDistance = .08f;
                joint.projectionAngle = 15;
            }
            ragdoll.bodies = bodies.ToArray();
            return ragdoll;
        }

        void Start()
        {
            // The existing hit capsules overlap at the shoulders, neck and thighs.
            // Keep suit parts from fighting each other while retaining world collisions.
            var zones = GetComponentsInChildren<ZombieHitZone>(true);
            for (int i = 0; i < zones.Length; i++)
                for (int j = i + 1; j < zones.Length; j++)
                    Physics.IgnoreCollision(zones[i].GetComponent<Collider>(), zones[j].GetComponent<Collider>());
        }

        public void Activate(ZombieHitZone struck, Vector3 point, Vector3 direction, Vector3 velocity)
        {
            if (IsActive) return;
            IsActive = true;
            float scale = Mathf.Max(.01f, Mathf.Abs(transform.lossyScale.y));
            Physics.SyncTransforms();
            foreach (var body in bodies)
                if (body && body.TryGetComponent<CharacterJoint>(out var joint) && joint.connectedBody)
                    joint.connectedAnchor = joint.connectedBody.transform.InverseTransformPoint(body.position);
            foreach (var body in bodies)
            {
                if (!body) continue;
                if (body.TryGetComponent<CharacterJoint>(out var joint)) joint.projectionDistance = .08f * scale;
                body.isKinematic = false;
                body.useGravity = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearVelocity = velocity;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
            var hitBody = struck ? struck.GetComponentInParent<Rigidbody>() : null;
            if (hitBody && direction.sqrMagnitude > .0001f)
                hitBody.AddForceAtPosition(direction.normalized * (deathImpulse * scale), point, ForceMode.Impulse);
        }

        public void AddVehicleTumble(Vector3 direction,float speed,Vector3 impactPoint)
        {
            if(!IsActive)return;
            float scale=Mathf.Max(.01f,Mathf.Abs(transform.lossyScale.y));
            var axis=Vector3.Cross(Vector3.up,direction).normalized;
            float spin=Mathf.Clamp(3+speed/scale*.28f,3,9);
            float side=Vector3.Dot(impactPoint-transform.position,transform.right);
            var angular=axis*spin+Vector3.up*Mathf.Clamp(side/scale,-1,1)*2;
            Vector3 center=Vector3.zero;float mass=0;
            foreach(var body in bodies)
                if(body&&!body.isKinematic){center+=body.worldCenterOfMass*body.mass;mass+=body.mass;}
            if(mass<=0)return;center/=mass;
            foreach(var body in bodies)
                if(body&&!body.isKinematic)
                {
                    // Matching point velocities let the joined suit rotate as a whole;
                    // spinning each bone in place is otherwise cancelled by the joints.
                    body.linearVelocity+=Vector3.Cross(angular,body.worldCenterOfMass-center);
                    body.angularVelocity=angular;
                }
        }
    }
}
