using System;
using UnityEngine;

namespace MoonThemedGame.Computer
{
    [CreateAssetMenu(menuName="Moon Game/Operator Tutorial")]
    public sealed class OperatorTutorialContent : ScriptableObject
    {
        [Serializable] public sealed class Line
        {
            [TextArea(2,5)] public string english,russian;
            [Min(1)] public float seconds=5;
            public string Text=>Loc.Russian?russian:english;
        }
        public Line[] radioLines={
            new Line{english="Good morning, Operator A.",russian="Доброе утро, Оператор Я.",seconds=3.2f},
            new Line{english="Unlike other lunar bases, your base is falling behind in mobile module repairs.",russian="В отличие от других лунных баз, ваша база отстаёт по показателям починки передвигаемых модулей.",seconds=6.5f},
            new Line{english="Pale Horse management is not particularly pleased with these results.",russian="Руководство Pale Horse не слишком довольно такими результатами.",seconds=5.3f},
            new Line{english="Take a seat at the computer. Accept orders in MESSAGES, then use the SCAVENGER to find spare parts.",russian="Садитесь за компьютер, принимайте заказы в СООБЩЕНИЯХ, а далее ищите запчасти при помощи СБОРЩИКА.",seconds=8}
        };
        public const string DefaultEnglishGuide="REPAIR ORDERS\n\n1  PC > MESSAGES.\n   Accept an order.\n2  Open SCAVENGER.\n   Collect marked parts.\n   Cargo: 2 parts.\n3  Return to base and\n   unload your cargo.\n4  Fit the missing parts.\n   Hammer wheel modules in.\n5  Hold MMB: toolbox.\n   Wrench: LMB + drag right.\n   Screwdriver: LMB +\n   clockwise circles.\n6  Tighten all fasteners.\n   Run the final test.\n   Red button: call crane.";
        public const string DefaultRussianGuide="ПАМЯТКА ОПЕРАТОРА\n\n1  ПК > СООБЩЕНИЯ.\n   Примите заказ.\n2  Откройте СБОРЩИК.\n   Соберите детали\n   по меткам. Груз: 2.\n3  Вернитесь на базу\n   и выгрузите груз.\n4  Установите детали.\n   Колёсные модули\n   подбейте молотком.\n5  Зажмите СКМ: тулбокс.\n   Ключ: ЛКМ + вправо.\n   Отвёртка: ЛКМ + круг\n   по часовой стрелке.\n6  Затяните крепления.\n   Проверьте бота.\n   Красная кнопка: кран.";
        [TextArea(18,24)] public string guideEnglish=DefaultEnglishGuide;
        [TextArea(18,24)] public string guideRussian=DefaultRussianGuide;
        public string Guide=>Loc.Russian?guideRussian:guideEnglish;
    }
}
