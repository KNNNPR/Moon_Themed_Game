using UnityEngine;
using MoonThemedGame.Interactions;

namespace MoonThemedGame.Weather
{
    [DisallowMultipleComponent, RequireComponent(typeof(WeatherMonitorDisplay))]
    public sealed class WeatherMonitorInteractable : Interactable
    {
        WeatherMonitorDisplay monitor;
        public override string Prompt => "E — View weather";
        void Awake() => monitor = GetComponent<WeatherMonitorDisplay>();
        public override bool CanInteract(Camera camera)
        {
            return base.CanInteract(camera) && monitor && monitor.isActiveAndEnabled &&
                !monitor.IsFocused && !monitor.IsTransitioning &&
                Vector3.Dot(camera.transform.forward, transform.TransformDirection(monitor.screenNormal)) < 0;
        }
        public override void Interact() { if (monitor) monitor.Focus(); }
    }
}
