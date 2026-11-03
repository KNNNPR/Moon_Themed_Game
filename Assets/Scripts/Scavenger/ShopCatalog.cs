using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Part models shown in the SHOP; loaded from Resources/ShopCatalog.
    [CreateAssetMenu(menuName = "Moon Game/Shop Catalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        public GameObject battery;
        public GameObject wheel;
        public GameObject headlights;
        public GameObject cargoBay;

        public GameObject Model(Upgrade upgrade) => upgrade switch
        {
            Upgrade.Capacity or Upgrade.Drain => battery,
            Upgrade.Speed => wheel,
            Upgrade.Headlights => headlights,
            _ => cargoBay
        };
    }
}
