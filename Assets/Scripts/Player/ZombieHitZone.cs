using UnityEngine;

namespace MoonThemedGame.Repair
{
    public enum ZombieBodyPart { Head, Torso, Arm, Leg }

    public sealed class ZombieHitZone : MonoBehaviour
    {
        public ZombieActor zombie;
        public ZombieBodyPart bodyPart;
        [Min(0)] public float damageMultiplier = 1;

        public bool Hit(float damage, Vector3 point, Vector3 normal, Vector3 direction)
            => zombie && zombie.ReceiveHit(this, damage, point, normal, direction);
    }
}
