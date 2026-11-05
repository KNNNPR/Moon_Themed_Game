using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [Serializable]
    public sealed class MoonFinderText
    {
        [TextArea(1,5)] public string english;
        [TextArea(1,5)] public string russian;
        public string Value=>Loc.Russian && !string.IsNullOrEmpty(russian)?russian:english;
        public MoonFinderText(string en,string ru){english=en;russian=ru;}
    }
    public enum MoonFinderEmotion { Neutral, Angry, Love }
    public enum MoonFinderReward { GarageKeys, CerberusTruce, GiantApple }
    [Serializable]
    public sealed class MoonFinderChoice
    {
        public MoonFinderText text;
        [Tooltip("ID of the reply node, in this profile.")] public string next;
    }
    [Serializable]
    public sealed class MoonFinderNode
    {
        public string id;
        public MoonFinderText reply;
        public MoonFinderEmotion emotion;
        [Tooltip("Awards this profile's reward once.")] public bool romance;
        public MoonFinderChoice[] choices=Array.Empty<MoonFinderChoice>();
    }
    [Serializable]
    public sealed class MoonFinderProfile
    {
        public string id;
        public MoonFinderText name, description;
        public Texture2D portrait, blink, angry, love;
        public bool apple;
        [Tooltip("Shuffle displayed choices on entering a node; translations keep the same order.")] public bool shuffleChoices;
        public MoonFinderReward reward;
        public string firstNode="hello";
        public MoonFinderNode[] nodes=Array.Empty<MoonFinderNode>();
        public MoonFinderNode Find(string nodeId)=>Array.Find(nodes,n=>n!=null && n.id==nodeId);
    }
    [CreateAssetMenu(menuName="Moon Game/MoonFinder Content")]
    public sealed class MoonFinderContent : ScriptableObject
    {
        [Min(0)] public int price=40;
        public MoonFinderText shopDescription=new MoonFinderText("You can find your love even on the moon","Даже на луне ты можешь найти любовь");
        public GameObject shopModel, appleModel;
        public VectorImage moonIcon, appleIcon;
        [Header("Editable English / Russian dialogue graph")]
        public MoonFinderProfile[] profiles=Array.Empty<MoonFinderProfile>();
        [Header("Apple chat")]
        [Min(1)] public int appleClicksRequired=12;
        public MoonFinderText appleMessage=new MoonFinderText("Apple.","Яблоко.");
        public MoonFinderText appleReply=new MoonFinderText("Apple!","Яблоко!");
        public MoonFinderText appleEnding=new MoonFinderText("A very large apple is waiting outside. Love grows.","Снаружи тебя ждёт очень большое яблоко. Любовь растёт.");
        public MoonFinderProfile Find(string id)=>Array.Find(profiles,p=>p!=null && p.id==id);
    }
}
