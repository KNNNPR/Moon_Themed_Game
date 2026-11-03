using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

namespace MoonThemedGame.Interactions
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class HeldProp : Interactable
    {
        public AudioClip radioTrack;
        public float throwSpeed = 5;
        Rigidbody body;
        AudioSource audioSource;
        Camera hand;
        Collider[] playerColliders, propColliders;
        Quaternion heldRotation;
        Vector3 center;
        float radius;
        static HeldProp held;
        bool radioOn;
        Coroutine collisionRestore;
        public override string Prompt => radioTrack
            ? "E — Take radio\n← / → — radio11   ↑ — Play   ↓ — Off"
            : "E — Take apple";
        public bool IsHeld => hand;
        void Awake()
        {
            body = GetComponent<Rigidbody>(); body.interpolation = RigidbodyInterpolation.Interpolate;
            body.useGravity = false;
            body.linearDamping = 0.05f; body.angularDamping = 0.05f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (!GetComponent<Collider>())
            {
                var mesh = GetComponent<MeshFilter>().sharedMesh;
                var box = gameObject.AddComponent<BoxCollider>(); box.center = mesh.bounds.center; box.size = mesh.bounds.size;
            }
            propColliders = GetComponentsInChildren<Collider>();
            var filter = GetComponent<MeshFilter>(); center = filter.sharedMesh.bounds.center;
            radius = GetComponent<Renderer>().bounds.extents.magnitude;
            // Nearby authored furniture needs solid surfaces for the newly physical props.
            var supportArea = GetComponent<Renderer>().bounds; supportArea.Expand(new Vector3(1,2,1));
            foreach (var surface in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (surface == GetComponent<Renderer>() || surface.GetComponentInParent<Rigidbody>() || surface.GetComponent<Collider>() ||
                    !surface.bounds.Intersects(supportArea) || surface.name.ToLowerInvariant().Contains("monitor") ||
                    !surface.sharedMaterial || surface.sharedMaterial.renderQueue >= 2500) continue;
                var supportMesh = surface.GetComponent<MeshFilter>();
                if (supportMesh && supportMesh.sharedMesh) surface.gameObject.AddComponent<MeshCollider>().sharedMesh = supportMesh.sharedMesh;
            }
            if (radioTrack)
            {
                audioSource = gameObject.AddComponent<AudioSource>(); audioSource.clip = radioTrack;
                audioSource.loop = true; audioSource.playOnAwake = false; audioSource.spatialBlend = 1;
                audioSource.minDistance = 2; audioSource.maxDistance = 22; audioSource.rolloffMode = AudioRolloffMode.Linear;
                audioSource.volume = 0.65f;
            }
        }
        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !held;
        void Start() => body.Sleep();
        public override void Interact()
        {
            if (held || !Camera.main) return;
            if (collisionRestore != null) { StopCoroutine(collisionRestore); collisionRestore = null; }
            held = this; hand = Camera.main; heldRotation = Quaternion.Inverse(hand.transform.rotation) * transform.rotation;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true;
            var player = hand.GetComponentInParent<FirstPersonController>();
            playerColliders = player ? player.GetComponentsInChildren<Collider>() : new Collider[0];
            foreach (var a in propColliders) foreach (var b in playerColliders) Physics.IgnoreCollision(a,b,true);
            SetHighlighted(false);
        }
        public void RadioInput()
        {
            if (!radioTrack || GameSettingsMenu.BlockGameplayInput || Keyboard.current == null) return;
            var key = Keyboard.current;
            if (key.leftArrowKey.wasPressedThisFrame || key.rightArrowKey.wasPressedThisFrame || key.upArrowKey.wasPressedThisFrame)
            { if (!radioOn) { audioSource.Play(); radioOn = true; } }
            if (key.downArrowKey.wasPressedThisFrame) { audioSource.Stop(); radioOn = false; }
        }
        void LateUpdate()
        {
            if (!hand) return;
            if (GameSettingsMenu.BlockGameplayInput) return;
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) { Drop(); return; }
            var player = hand.GetComponentInParent<FirstPersonController>();
            if (!hand.enabled || (player && !player.enabled)) { Drop(false); return; }
            RadioInput();
            Vector3 origin = hand.transform.position;
            Vector3 desired = origin + hand.transform.forward * Mathf.Clamp(0.65f + radius, 0.85f, 1.6f) + hand.transform.right * 0.25f - hand.transform.up * 0.22f;
            // Ignore our own shapes, but shorten the hold distance at walls.
            float limit = Vector3.Distance(origin, desired);
            foreach (var hit in Physics.SphereCastAll(origin, Mathf.Clamp(radius * 0.65f,0.08f,0.35f), (desired-origin).normalized, limit, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == body || (player && hit.transform.IsChildOf(player.transform))) continue;
                limit = Mathf.Min(limit, Mathf.Max(0.25f, hit.distance - 0.05f));
            }
            transform.rotation = hand.transform.rotation * heldRotation;
            transform.position = origin + (desired-origin).normalized * limit - transform.TransformVector(center);
        }
        public void Drop(bool toss = true)
        {
            if (!hand) return;
            Vector3 direction = hand.transform.forward;
            hand = null; held = null; body.isKinematic = false;
            body.linearVelocity = toss ? direction * throwSpeed : Vector3.zero;
            if (toss && gameObject.activeInHierarchy) collisionRestore = StartCoroutine(RestoreAfterThrow());
            else RestorePlayerCollisions();
        }
        IEnumerator RestoreAfterThrow()
        {
            for (int i=0;i<30;i++)
            {
                yield return new WaitForFixedUpdate();
                if (hand) yield break;
                bool touching = false;
                foreach (var a in propColliders) foreach (var b in playerColliders)
                    if (a && b && a.bounds.Intersects(b.bounds)) touching = true;
                if (!touching) break;
            }
            RestorePlayerCollisions(); collisionRestore = null;
        }
        void RestorePlayerCollisions()
        {
            if (playerColliders == null) return;
            foreach (var a in propColliders) foreach (var b in playerColliders) if (a && b) Physics.IgnoreCollision(a,b,false);
        }
        protected override void OnDisable() { Drop(false); base.OnDisable(); }
        void OnGUI()
        {
            if (hand && !GameSettingsMenu.IsOpen) GUI.Label(new Rect(Screen.width/2f-150,Screen.height-70,300,50), "G — Throw" + (radioTrack ? "    ↑ Play / ↓ Off" : ""));
        }
    }
}
