using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [CreateAssetMenu(menuName="Moon Game/Story Finale Content")]
    public sealed class StoryFinaleContent : ScriptableObject
    {
        public VectorImage logo;
        public string customer="SCAV_BOT_AURINKO";
        [TextArea(5,12)] public string english="I lived a long life, long enough to get sick. You know how it goes in a machine? They begin to rust, but not physically, no, not like that... It starts from the head, in the brain, in the circulating tubes and optical fiber, diesel makes you shudder. My friend, please, take care of me.";
        [TextArea(5,12)] public string russian="Я прожил долгую жизнь, достаточно долгую, чтобы успеть приболеть. Знаешь как бывает у машин? Они начинают ржаветь, но не физически, нет, не физически... Это в голове, в мозгах, в циркулирующих трубках и оптоволокне, солярка пробирает до дрожи. Друг, пожалуйста, позаботься обо мне.";
        public string thanksEnglish="Thank you",thanksRussian="Спасибо тебе";
        [System.Serializable] public struct Credit
        {
            public string name;
            [TextArea] public string englishRole,russianRole;
        }
        public Credit[] credits=new[]
        {
            new Credit{name="APPLEJOLLYGARDEN",englishRole="3D ART\nGAME DESIGN\nEFFECTS / SHADERS",russianRole="3D-ГРАФИКА\nГЕЙМДИЗАЙН\nЭФФЕКТЫ / ШЕЙДЕРЫ"},
            new Credit{name="ZHELE",englishRole="SOUND DESIGN\nMUSIC",russianRole="ЗВУКОВОЙ ДИЗАЙН\nМУЗЫКА"},
            new Credit{name="KNNPR, NIKITA",englishRole="CODE\nSCENE SETTINGS",russianRole="КОД\nНАСТРОЙКА СЦЕН"}
        };
        public string closingEnglish="Always have an apple from your jolly garden.\nDear Player.",closingRussian="Пусть у тебя всегда будет яблоко из твоего весёлого сада.\nДорогой игрок.";
    }
}
