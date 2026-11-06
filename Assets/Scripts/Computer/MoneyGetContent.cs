using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [Serializable]
    public sealed class MoneyGetStock
    {
        public string company;
        [Min(.5f)] public float startingPrice=8;
        [Range(.005f,.15f)] public float volatility=.04f;
        public string goodNews,goodNewsRu,badNews,badNewsRu;
    }

    [CreateAssetMenu(menuName="Moon Game/MoneyGetApp Content")]
    public sealed class MoneyGetContent : ScriptableObject
    {
        [Min(1)] public int price=40;
        public GameObject shopModel;
        public VectorImage desktopIcon;
        [Min(.5f)] public float quoteInterval=3;
        public MoneyGetStock[] stocks;
        public string Title=>Loc.Russian?"ДенежноеДрево":"MoneyGetApp";
        public string Description=>Loc.Russian?"Лунная биржа. Лонги, шорты, прибыль и убытки.":"Lunar stock exchange. Go long or short. Win or lose credits.";
    }
}
