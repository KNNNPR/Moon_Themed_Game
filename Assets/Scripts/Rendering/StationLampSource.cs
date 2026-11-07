using UnityEngine;

namespace MoonThemedGame.Rendering
{
    // Explicit source: the ceiling spotlight breaks with its glass; screen spill is independent.
    public sealed class StationLampSource : MonoBehaviour
    {
        public bool ceilingFixture;
        public Renderer glass;
    }
}
