using MoonThemedGame.Audio;
using MoonThemedGame.Interactions;
using MoonThemedGame.Repair;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonThemedGame.Computer
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class CrazyCosmonautsPlayer : MonoBehaviour
    {
        public CrazyCosmonautsApp game;
        public Camera view;
        public int Ammo { get; private set; } = 8;
        public bool Reloading => reloadTime > 0;
        public int ShotsFired { get; private set; }
        CharacterController motor;
        Transform weapon, muzzle, slide;
        Material fx, impact, brass;
        AnimationClip reload;
        float pitch, vertical, nextShot, reloadTime, recoil;
        readonly List<Material> materials = new List<Material>();

        public void Initialize(CrazyCosmonautsApp app, RenderTexture texture)
        {
            game = app; motor = GetComponent<CharacterController>();GameAudio.Footsteps(gameObject,true);
            motor.height = 1.8f; motor.radius = .28f; motor.center = Vector3.up * .9f; motor.stepOffset = .25f;
            var cameraObject = new GameObject("Crazy Cosmonauts / camera"); cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = Vector3.up * 1.65f;
            view = cameraObject.AddComponent<Camera>(); view.fieldOfView = 74; view.nearClipPlane = .035f; view.farClipPlane = 140;
            view.targetTexture = texture; view.depth = -20; view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.025f, .035f, .04f);
            cameraObject.AddComponent<CrazyCosmonautsCamera>();
            if (app.content.pistolPrefab)
            {
                var gun = Instantiate(app.content.pistolPrefab, cameraObject.transform, false); gun.name = "Pistol / PC game";
                weapon = gun.transform; weapon.localPosition = new Vector3(.17f, -.19f, .42f); weapon.localRotation = Quaternion.identity;
                var source = gun.GetComponent<PlayerPistol>();
                if (source) { muzzle = source.muzzle; slide = source.slide; fx = source.effectMaterial; impact = source.impactMaterial; brass = source.casingMaterial; reload = source.reloadClip; source.enabled = false; Destroy(source); }
                foreach (var collider in gun.GetComponentsInChildren<Collider>()) { collider.enabled = false; Destroy(collider); }
                foreach (var body in gun.GetComponentsInChildren<Rigidbody>()) { body.isKinematic = true; Destroy(body); }
                foreach (var animation in gun.GetComponentsInChildren<Animation>()) animation.enabled = false;
                foreach (var node in gun.GetComponentsInChildren<Transform>()) node.gameObject.layer = 30;
                foreach (var renderer in gun.GetComponentsInChildren<Renderer>())
                {
                    var copies = renderer.sharedMaterials;
                    for (int i = 0; i < copies.Length; i++)
                    {
                        var original = copies[i]; var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        if (original && original.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                        if (original && original.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", original.GetColor("_BaseColor"));
                        mat.SetFloat("_Metallic", .25f); mat.SetFloat("_Smoothness", .35f); materials.Add(mat); copies[i] = mat;
                    }
                    renderer.sharedMaterials = copies;
                }
            }
        }

        void Update()
        {
            if (!game || !game.GameplayActive || !motor || !view) return;
            float dt = Time.deltaTime;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var delta = mouse.delta.ReadValue()*ComfortSettings.HumanSensitivity; transform.Rotate(Vector3.up, delta.x * .11f);
                pitch = Mathf.Clamp(pitch - delta.y * .11f, -80, 80);
            }
            view.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            bool aim = mouse != null && mouse.rightButton.isPressed;
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, aim ? 52 : 74, 1 - Mathf.Exp(-dt * 12));
            var input = GameControls.Movement;
            vertical = motor.isGrounded && vertical < 0 ? -2 : vertical - 18 * dt;
            if (motor.isGrounded && GameControls.Pressed(GameCommand.Jump)) vertical = 5.5f;
            var velocity = (transform.right * input.x + transform.forward * input.y) * (GameControls.Held(GameCommand.Sprint) ? 5.2f : 3.6f);
            motor.Move((velocity + Vector3.up * vertical) * dt);
            if (transform.position.y < game.ArenaFloor - 8) game.TakeDamage(game.Health);
            if (reloadTime > 0)
            {
                reloadTime = Mathf.Max(0, reloadTime - dt);
                if (reload && weapon) reload.SampleAnimation(weapon.gameObject, (1 - reloadTime / 2.2f) * reload.length);
                if (reloadTime == 0) { Ammo = 8; if (reload && weapon) reload.SampleAnimation(weapon.gameObject, 0); }
            }
            if (GameControls.Pressed(GameCommand.Reload)) Reload();
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Fire();
            recoil = Mathf.MoveTowards(recoil, 0, dt * 35);
            if (weapon)
            {
                weapon.localPosition = Vector3.Lerp(weapon.localPosition, aim ? new Vector3(0, -.13f, .4f) : new Vector3(.17f, -.19f, .42f), 1 - Mathf.Exp(-dt * 12));
                weapon.localRotation = Quaternion.Euler(-recoil, 0, recoil * .2f);
            }
        }

        public bool Reload()
        {
            if (!game || !game.GameplayActive || Reloading || Ammo == 8) return false;
            reloadTime = 2.2f;GameAudio.Ui(Sound.GameReload); return true;
        }
        public bool Fire()
        {
            if (!game || !game.GameplayActive || !game.FireInputReady || !view || Reloading || Ammo <= 0 || Time.time < nextShot) return false;
            nextShot = Time.time + .19f; Ammo--; ShotsFired++;GameAudio.Ui(Sound.GameShot);
            var direction = view.transform.forward;
            RaycastHit nearest = default; bool found = false;
            foreach (var hit in Physics.RaycastAll(view.transform.position, direction, 100, 1 << 30, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || (hit.collider is CharacterController && hit.collider.GetComponent<ZombieActor>())) continue;
                if (!found || hit.distance < nearest.distance) { nearest = hit; found = true; }
            }
            if (found)
            {
                var zone = nearest.collider.GetComponent<ZombieHitZone>();
                if (!zone || !zone.Hit(34, nearest.point, nearest.normal, direction))
                {
                    if (nearest.rigidbody && !nearest.rigidbody.isKinematic) nearest.rigidbody.AddForceAtPosition(direction * 5, nearest.point, ForceMode.Impulse);
                    PistolEffects.Impact(nearest, 1, fx, impact);
                }
            }
            PistolEffects.Shot(muzzle ? muzzle.position : view.transform.position + direction * .4f, view.transform.rotation, 1, fx, brass);
            recoil = Mathf.Min(16, recoil + 8); pitch = Mathf.Max(-80, pitch - 1.8f);
            return true;
        }
        void OnDestroy() { foreach (var material in materials) if (material) Destroy(material); }
    }
}
