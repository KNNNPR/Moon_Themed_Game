using UnityEngine;

namespace MoonThemedGame.Repair
{
    [CreateAssetMenu(menuName = "Moon Game/Repair Robot Catalog")]
    public sealed class RepairRobotCatalog : ScriptableObject
    {
        public RepairRobotDefinition[] robots = new RepairRobotDefinition[0];
    }
}
