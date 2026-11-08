using System;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [CreateAssetMenu(menuName="Moon Game/Repair Special Steps")]
    public sealed class RepairSpecialStepSettings : ScriptableObject
    {
        public bool enableSpecialRepairs=true;
        [Min(1)] public int panelHits=2;
        [Min(40)] public float alignmentPixels=150;
        [Range(.08f,.3f)] public float alignmentTolerance=.16f;
        [Min(30)] public float seatingPixels=90;
        [Serializable] public struct DentMesh {public Mesh source,blend;}
        public DentMesh[] dentMeshes=Array.Empty<DentMesh>();
        public Mesh DentFor(Mesh source){foreach(var pair in dentMeshes)if(pair.source==source)return pair.blend;return null;}
    }
}
