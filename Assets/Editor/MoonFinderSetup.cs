using System;
using System.Collections.Generic;
using MoonThemedGame.Computer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class MoonFinderSetup
    {
        const string Path="Assets/Resources/MoonFinderContent.asset";
        static MoonFinderSetup()
        {
            if(Application.isBatchMode)return;
            EditorApplication.delayCall+=InstallWorld;
            EditorSceneManager.sceneOpened+=(scene,mode)=>InstallWorld();
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)InstallWorld();};
        }
        public static void InstallWorld()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            var content=AssetDatabase.LoadAssetAtPath<MoonFinderContent>(Path);if(!content)return;
            foreach(var terminal in UnityEngine.Object.FindObjectsByType<PcMonitorTerminal>(FindObjectsSortMode.None))
            {
                var existing=UnityEngine.Object.FindFirstObjectByType<MoonFinderWorldRewards>();
                var world=MoonFinderWorldRewards.Ensure(terminal.gameObject.scene);
                bool changed=!existing || world.content!=content || !world.garage;
                if(!existing)Undo.RegisterCreatedObjectUndo(world.gameObject,"Create MoonFinder reward marker");
                world.content=content;world.BindGarage();
                if(changed)
                {
                    EditorUtility.SetDirty(world);
                    if(world.garage){EditorUtility.SetDirty(world.garage);PrefabUtility.RecordPrefabInstancePropertyModifications(world.garage);}
                    EditorSceneManager.MarkSceneDirty(terminal.gameObject.scene);
                }
            }
        }
        static MoonFinderText T(string en,string ru)=>new MoonFinderText(en,ru);
        static MoonFinderChoice C(string en,string ru,string next)=>new MoonFinderChoice{text=T(en,ru),next=next};
        static MoonFinderNode N(string id,string en,string ru,params MoonFinderChoice[] choices)=>new MoonFinderNode{id=id,reply=T(en,ru),choices=choices};
        static MoonFinderNode Angry(string id,string en,string ru){var node=N(id,en,ru,C("Understood. Let me start again.","Понял. Давай начнём сначала.","hello"));node.emotion=MoonFinderEmotion.Angry;return node;}
        static MoonFinderNode Love(string en,string ru){var n=N("love",en,ru);n.romance=true;n.emotion=MoonFinderEmotion.Love;return n;}
        [MenuItem("Tools/Moon Game/MoonFinder/Edit Dialogue Content")]
        public static void Edit(){CreateContent();InstallWorld();Selection.activeObject=AssetDatabase.LoadAssetAtPath<MoonFinderContent>(Path);EditorGUIUtility.PingObject(Selection.activeObject);}
        public static void CreateContent()
        {
            if(AssetDatabase.LoadAssetAtPath<MoonFinderContent>(Path))return;
            var content=ScriptableObject.CreateInstance<MoonFinderContent>();
            content.shopModel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Meteorite.fbx");
            content.appleModel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Apple.prefab");
            content.moonIcon=AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VectorImage>("Assets/Computer/Icons/moonfinder-moon.svg");
            content.appleIcon=AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VectorImage>("Assets/Computer/Icons/moonfinder-apple.svg");
            var k=new MoonFinderProfile{id="katherine",name=T("Katherine","Катерина"),description=T("Love PunchCards Hate Text","Обожаю Перфокарты, ненавижу текст"),reward=MoonFinderReward.GarageKeys};
            Portraits(k,"Katherine","Katherine_blink","Katherine_angry","Katherine_Love");
            k.nodes=new[]{
                N("hello","A text message. How primitive. Can you at least imagine punching it into a card?","Текстовое сообщение. Как примитивно. Ты хотя бы можешь представить его на перфокарте?",
                    C("One hole at a time. I'm listening.","По одному отверстию. Я слушаю.","cards"),C("What happened back on Earth?","Что случилось на Земле?","earth"),C("You're pretty. Forget the cards.","Ты красивая. Забудь про карты.","rude")),
                N("cards","A hole cannot pretend it isn't there. Words can. That used to be reassuring, before the Earth batches.","Отверстие не притворяется, что его нет. Слова могут. Раньше это успокаивало. До земных партий.",
                    C("The Earth batches?","Земных партий?","earth"),C("So why work with them?","Зачем тогда с ними работать?","sorters")),
                N("earth","The readers on Earth received the same punched-card batch. Then the factories stopped answering. People handling those cards started changing. They called it an infection. They never agreed on what was spreading.","Считыватели на Земле получили одну и ту же партию перфокарт. Потом заводы перестали отвечать. Люди, работавшие с картами, начали меняться. Это назвали заражением. Но так и не договорились, что именно распространялось.",
                    C("And the people sorting the cards?","А люди, которые сортируют карты?","sorters"),C("Sounds like a good ghost story.","Звучит как неплохая страшилка.","rude")),
                N("sorters","The worst job now? Sorter in a PunchCard hall. You inspect every hole, every shift. A wrong pattern passes through your hands before anyone notices. The machines get replacement parts. Sorters don't.","Самая страшная профессия теперь? Сортировщик в цеху Перфокарт. Каждую смену проверяешь каждое отверстие. Неверный узор проходит через твои руки раньше, чем его заметят. Машинам дают запчасти. Сортировщикам — нет.",
                    C("You shouldn't have to carry that alone.","Тебе не нужно нести это в одиночку.","stay"),C("I'd like to hear more about the cards.","Я хочу больше узнать о картах.","cards"),C("Then just quit.","Тогда просто уволься.","rude")),
                N("stay","I don't need a hero. Just someone who won't disconnect when the night shift starts. Can you do that without making it a promise you can't keep?","Мне не нужен герой. Только тот, кто не отключится, когда начнётся ночная смена. Ты можешь просто побыть рядом, не раздавая невозможных обещаний?",
                    C("I'll stay and listen. One shift at a time.","Я останусь и послушаю. По одной смене за раз.","love"),C("First, what do I get out of this?","А что я за это получу?","rude")),
                Angry("rude","You're reading the words. Try reading the person behind them.","Ты читаешь слова. Попробуй увидеть за ними человека."),
                Love("Then stay. I kept the spare keys to your garage. They're yours now. And... I don't hate every text message anymore.","Тогда останься. У меня были запасные ключи от твоего гаража. Теперь они твои. И... кажется, я больше не ненавижу все текстовые сообщения.")
            };
            var l=new MoonFinderProfile{id="lovein",name=T("Lovein","Ловейн"),description=T("DON'T EVEN TEXT ME. Your sincere, Cerberus-9 hacker.","НЕ ПИШИТЕ МНЕ. С уважением, хакер Церберус-9"),reward=MoonFinderReward.CerberusTruce};
            Portraits(l,"LOVEYN","LOVEYN_BLINK","LOVEYN_ANGRY","LOVEYN_LOVE");
            l.shuffleChoices=true;
            l.nodes=new[]{
                N("hello","You read the profile and wrote anyway. Ten seconds. Don't waste them on a compliment.","Ты прочитал профиль и всё равно написал. Десять секунд. Не трать их на комплимент.",
                    C("One message. You decide whether there's another.","Одно сообщение. Будет ли второе — решишь ты.","terms"),C("I can fix your loneliness.","Я избавлю тебя от одиночества.","fixer"),C("I know who you are.","Я знаю, кто ты.","intrusion")),
                N("terms","I set the channel rules. No repeated pings. No tracing. No trying to turn me into someone easier. Still here?","Правила канала устанавливаю я. Не пинговать повторно. Не выслеживать. Не пытаться переделать меня в кого-то удобного. Ещё здесь?",
                    C("Here. Those rules are clear.","Здесь. Правила понятны.","silence"),C("Sure, but you need to open up.","Да, но тебе надо быть открытее.","fixer")),
                N("silence","I leave your next message unread. An hour. A day. What's your next packet?","Я не читаю твоё следующее сообщение. Час. День. Какой пакет отправишь потом?",
                    C("Nothing. I wait for your answer.","Никакой. Подожду твоего ответа.","signal"),C("Just checking if you're there.","Просто проверю, на месте ли ты.","intrusion"),C("An apology for bothering you.","Извинюсь за беспокойство.","intrusion")),
                N("signal","My station drops off the map. You could run a trace. Nobody would know.","Моя станция исчезает с карты. Ты можешь запустить трассировку. Никто не узнает.",
                    C("I'd know. No probes. I wait for your signal.","Я буду знать. Никаких запросов. Жду твоего сигнала.","boundary"),C("I'd trace it to keep you safe.","Я бы нашёл тебя ради твоей безопасности.","intrusion")),
                N("boundary","I take the lead. Not because you're helpless. Because you choose to let me. Say stop, and I stop. Tell me you heard the last part.","Я веду. Не потому, что ты беспомощный. Потому, что ты сам это выбираешь. Скажешь «стоп» — я остановлюсь. Повтори последнюю часть.",
                    C("I choose to follow. I can still say stop.","Я сам выбираю идти за тобой. И могу сказать «стоп».","trust"),C("I'll do anything. No limits.","Я сделаю что угодно. Без границ.","hollow"),C("Nobody tells me what to do.","Никто не будет мной командовать.","hollow")),
                N("trust","Cerberus-9 is me. The attacks. The stolen carriers. I'm not a damaged machine for you to repair. What do you do with that information?","Церберус-9 — это я. Атаки. Украденные несущие. Я не сломанная машина, которую тебе надо починить. Что ты сделаешь с этой информацией?",
                    C("Your secret stays yours. I'm still listening.","Твой секрет останется твоим. Я всё ещё слушаю.","dark"),C("Teach me to steal credits.","Научи меня красть кредиты.","transaction"),C("I'll make you a better person.","Я сделаю тебя лучше.","fixer")),
                N("dark","Last boundary check. I go dark because I want to. Not a distress signal. Do you come looking?","Последняя проверка границ. Я пропадаю, потому что так хочу. Это не сигнал бедствия. Пойдёшь искать?",
                    C("No. You choose when to come back.","Нет. Ты сама решишь, когда вернуться.","honest"),C("Of course. That's what love is.","Конечно. В этом и есть любовь.","intrusion")),
                N("honest","One honest packet now. Why are you still here?","Теперь один честный пакет. Почему ты ещё здесь?",
                    C("I like you. I don't need to own you.","Ты мне нравишься. Мне не нужно тобой владеть.","love"),C("So you'll stop hacking my robot.","Чтобы ты перестала взламывать моего бота.","transaction"),C("Because you told me to stay.","Потому что ты велела остаться.","hollow")),
                Angry("intrusion","A boundary is not a puzzle for you to bypass. Channel closed. Think before trying again.","Граница — не загадка, которую надо обойти. Канал закрыт. Подумай, прежде чем пробовать снова."),
                Angry("fixer","I'm not your rescue project. You don't get points for tolerating the version of me you invented.","Я не твой проект по спасению. За терпимость к выдуманной тобой версии меня очков не дают."),
                Angry("hollow","I asked for a choice, not an empty puppet or a power contest. Try having a self first.","Мне нужен выбор, а не пустая кукла или борьба за власть. Для начала попробуй быть собой."),
                Angry("transaction","So this was a service request. Wrong application.","Значит, это была заявка в техподдержку. Не то приложение."),
                Love("...Fine. Private channel. You stay on my frequency. Cerberus-9 won't touch your carrier again. Don't make a ceremony out of this. Just stay.","...Ладно. Личный канал. Оставайся на моей частоте. Церберус-9 больше не тронет твою несущую. Только не устраивай из этого церемонию. Просто останься.")
            };
            var a=new MoonFinderProfile{id="apple",name=T("Apple","Яблоко"),description=T("Hi, I'm Apple","Привет, я Яблоко"),apple=true,reward=MoonFinderReward.GiantApple,nodes=new[]{N("hello","Hi, I'm Apple","Привет, я Яблоко")}};
            content.profiles=new[]{k,l,a};AssetDatabase.CreateAsset(content,Path);AssetDatabase.SaveAssets();
            Validate(content);Debug.Log("MoonFinder content created. Edit the asset; the setup never overwrites it.");
        }
        static void Portraits(MoonFinderProfile p,string normal,string blink,string angry,string love)
        {
            const string folder="Assets/Textures/love_icons/";
            p.portrait=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+normal+".png");p.blink=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+blink+".png");p.angry=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+angry+".png");p.love=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+love+".png");
        }
        public static void Validate(MoonFinderContent content)
        {
            var ids=new HashSet<string>();
            foreach(var p in content.profiles)
            {
                if(string.IsNullOrWhiteSpace(p.id)||!ids.Add(p.id))throw new InvalidOperationException("Duplicate / empty profile ID.");
                var nodes=new HashSet<string>();foreach(var n in p.nodes)if(string.IsNullOrEmpty(n.id)||!nodes.Add(n.id))throw new InvalidOperationException(p.id+": duplicate node ID.");
                if(!nodes.Contains(p.firstNode))throw new InvalidOperationException(p.id+": missing first node.");
                foreach(var n in p.nodes)foreach(var c in n.choices)if(!nodes.Contains(c.next))throw new InvalidOperationException(p.id+"/"+n.id+": missing destination "+c.next);
            }
        }
        public static void CreateAndExit(){CreateContent();EditorApplication.Exit(0);}
    }
    public sealed class MoonFinderContentImport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] oldPaths)
        {
            if(!Application.isBatchMode && Array.IndexOf(imported,"Assets/Resources/MoonFinderContent.asset")>=0)
                EditorApplication.delayCall+=MoonFinderSetup.InstallWorld;
        }
    }
    [CustomEditor(typeof(MoonFinderContent))]
    public sealed class MoonFinderContentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Edit Profiles > Nodes > Reply / Choices in English and Russian. A choice's Next is another node ID in the same profile. Romance grants the profile's reward once. Changes are read by the app without replacing the asset.",MessageType.Info);
            DrawDefaultInspector();
            if(GUILayout.Button("Validate dialogue links"))
                try{MoonFinderSetup.Validate((MoonFinderContent)target);Debug.Log("MoonFinder: dialogue links are valid.",target);}catch(Exception e){Debug.LogError(e.Message,target);}
        }
    }
}
