using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Anything the robot can use with E. The robot picks the closest one in reach.
    public abstract class ScavengerInteractable : MonoBehaviour
    {
        public static readonly List<ScavengerInteractable> All = new();

        [Min(0.1f)] public float reach = 1.5f;

        // Null hides the prompt and blocks E.
        public abstract string GetPrompt(ScavengerRobot robot);
        public abstract void Interact(ScavengerRobot robot);

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => All.Clear();
    }
}
