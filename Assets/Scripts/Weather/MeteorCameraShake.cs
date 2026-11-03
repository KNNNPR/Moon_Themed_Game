using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Weather
{
    [RequireComponent(typeof(Camera)), DisallowMultipleComponent]
    public sealed class MeteorCameraShake : MonoBehaviour
    {
        Camera view;
        Vector3 savedPosition;
        Quaternion savedRotation;
        float until, strength;
        bool applied;
        public bool IsShaking => Time.time < until;
        public static void Kick(Vector3 impact, float range)
        {
            foreach (var camera in Camera.allCameras)
            {
                float proximity = 1 - Vector3.Distance(camera.transform.position, impact) / Mathf.Max(1, range);
                if (proximity <= 0 || camera.cameraType != CameraType.Game) continue;
                if (!camera.TryGetComponent<MeteorCameraShake>(out var shake)) shake = camera.gameObject.AddComponent<MeteorCameraShake>();
                shake.strength = Mathf.Max(shake.strength, proximity * 0.045f);
                shake.until = Time.time + 0.55f;
            }
        }
        void OnEnable()
        {
            view = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }
        void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view || !IsShaking || applied) return;
            savedPosition = transform.position; savedRotation = transform.rotation; applied = true;
            float amount = strength * Mathf.Clamp01((until - Time.time) / 0.55f);
            float x = Mathf.Sin(Time.time * 67), y = Mathf.Sin(Time.time * 83 + 1);
            transform.position += transform.right * (x * amount) + transform.up * (y * amount * 0.65f);
            transform.rotation *= Quaternion.Euler(y * amount * 7, x * amount * 5, x * amount * 9);
        }
        void End(ScriptableRenderContext context, Camera camera) { if (camera == view) Restore(); }
        void Restore()
        {
            if (!applied) return; transform.SetPositionAndRotation(savedPosition, savedRotation); applied = false;
        }
        void Update() { if (!IsShaking) strength = 0; }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End; Restore();
        }
    }
}
