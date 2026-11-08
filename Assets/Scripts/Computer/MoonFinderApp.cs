using MoonThemedGame.Audio;
using System;
using System.Collections.Generic;
using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [DisallowMultipleComponent]
    public sealed class MoonFinderApp : MonoBehaviour
    {
        sealed class Line { public MoonFinderText text;public bool player,pending;public int shown;public float begins;public bool romance; }
        sealed class Chat
        {
            public string node;
            public bool romanced;
            public int appleClicks;
            public string choiceNode;
            public int[] choiceOrder;
            public readonly List<Line> history=new List<Line>();
        }
        readonly Dictionary<string,Chat> chats=new Dictionary<string,Chat>();
        readonly List<(VisualElement element,Action action)> buttons=new List<(VisualElement,Action)>();
        readonly List<(Image image,MoonFinderProfile profile)> portraits=new List<(Image,MoonFinderProfile)>();
        PcMonitorTerminal terminal;
        VisualElement icon,window,people,conversation,choices,appleButton;
        VisualElement boundRoot;
        Label profileName,description,state,heading,subtitle,close;
        Image avatar;
        ScrollView transcript;
        string selected;
        bool scrollToLatest;
        bool typingRefreshPending;
        float typingRefreshAt;
        int scrollAfterFrame;
        MoonFinderWorldRewards rewards;
        public MoonFinderContent content;
        [Min(5)] public float charactersPerSecond=60;
        public Vector2 replyDelay=new Vector2(.6f,1.1f);
        public bool IsTyping=>selected!=null&&chats.TryGetValue(selected,out var chat)&&chat.history.Exists(l=>l.pending);
        public bool Purchased {get;private set;}
        public bool IsOpen=>window!=null && window.style.display==DisplayStyle.Flex;
        public VisualElement Window=>window;
        public string Selected=>selected;
        public MoonFinderNode CurrentNode=>content && selected!=null?content.Find(selected)?.Find(ChatFor(selected).node):null;
        public bool IsRomanced(string id)=>chats.TryGetValue(id,out var chat) && chat.romanced;
        public int AppleClicks=>chats.TryGetValue("apple",out var chat)?chat.appleClicks:0;
        public int MessageCount(string id)=>chats.TryGetValue(id,out var chat)?chat.history.Count:0;
        void Awake()
        {
            terminal=GetComponent<PcMonitorTerminal>();
            if(!content)content=Resources.Load<MoonFinderContent>("MoonFinderContent");
            rewards=MoonFinderWorldRewards.Ensure(gameObject.scene);
        }
        void OnEnable()=>Loc.Changed+=Refresh;
        void OnDisable(){Loc.Changed-=Refresh;Close();}
        void OnDestroy(){icon?.RemoveFromHierarchy();window?.RemoveFromHierarchy();}
        public bool Purchase()
        {
            if(Purchased || !terminal || !content || content.profiles.Length==0 || terminal.Credits<content.price)return false;
            terminal.SetCredits(terminal.Credits-content.price);Purchased=true;
            if(window==null && terminal.Root!=null)Build();
            if(icon!=null)icon.style.display=DisplayStyle.Flex;
            return true;
        }
        Chat ChatFor(string id)
        {
            if(chats.TryGetValue(id,out var chat))return chat;
            var profile=content.Find(id);chat=new Chat{node=profile.firstNode};
            var first=profile.Find(chat.node);if(first!=null)chat.history.Add(new Line{text=first.reply,pending=true,begins=float.PositiveInfinity});
            chats.Add(id,chat);return chat;
        }
        public bool SelectProfile(string id)
        {
            if(!Purchased || !content || content.Find(id)==null)return false;
            selected=id;var chat=ChatFor(id);
            foreach(var line in chat.history)if(line.pending&&float.IsPositiveInfinity(line.begins))line.begins=Time.unscaledTime+UnityEngine.Random.Range(replyDelay.x,replyDelay.y);
            Refresh();return true;
        }
        public bool Choose(int index)
        {
            if(!Purchased || IsTyping || CurrentNode==null || index<0 || index>=CurrentNode.choices.Length)return false;
            var profile=content.Find(selected);if(profile.apple)return false;
            var option=CurrentNode.choices[index];var next=profile.Find(option.next);if(next==null)return false;
            var chat=ChatFor(selected);chat.history.Add(new Line{text=option.text,player=true});
            chat.node=next.id;chat.history.Add(Reply(next.reply,next.romance));
            if(chat.history.Count>200)chat.history.RemoveRange(0,chat.history.Count-200);
            GameAudio.Ui(next.emotion==MoonFinderEmotion.Angry?Sound.Error:Sound.Confirm);
            Refresh();return true;
        }
        public bool ClickApple()
        {
            if(!Purchased || IsTyping || selected==null || !content.Find(selected).apple)return false;
            var chat=ChatFor(selected);if(chat.romanced)return false;
            chat.appleClicks++;chat.history.Add(new Line{text=content.appleMessage,player=true});
            chat.history.Add(Reply(content.appleReply));GameAudio.Ui(Sound.Confirm);
            if(chat.appleClicks>=content.appleClicksRequired)
            {
                chat.romanced=true;var ending=Reply(content.appleEnding);ending.begins+=content.appleReply.Value.Length/Mathf.Max(5,charactersPerSecond)+.7f;chat.history.Add(ending);
                rewards.Grant(content.Find(selected).reward);
            }
            Refresh();return true;
        }
        public void SetOpen(bool open)
        {
            if(!Purchased)return;
            if(window==null && terminal.Root!=null)Build();if(window==null)return;
            window.style.display=open?DisplayStyle.Flex:DisplayStyle.None;
            if(!open)return;
            GetComponent<ShopApp>()?.Close();GetComponent<SatelliteConsoleApp>()?.Close();
            GetComponent<RepairMessagesApp>()?.Close();
            GetComponent<MoneyGetApp>()?.Close();
            GetComponent<RoutePlannerApp>()?.Close();
            GetComponent<CrazyCosmonautsApp>()?.Close();
            window.BringToFront();terminal.Root.Q("screen-cursor")?.BringToFront();
            if(selected==null && content.profiles.Length>0)SelectProfile(content.profiles[0].id);else Refresh();
        }
        public void Close(){if(window!=null)window.style.display=DisplayStyle.None;}
        void Update()
        {
            if(!content || !terminal || terminal.Root==null)return;
            UpdateTyping();
            if(boundRoot!=terminal.Root)
            {
                icon?.RemoveFromHierarchy();window?.RemoveFromHierarchy();icon=null;window=null;buttons.Clear();portraits.Clear();Build();
            }
            if(GameSettingsMenu.BlockGameplayInput || terminal.RouteMapOpen || (GetComponent<CrazyCosmonautsApp>()?.IsOpen ?? false))return;
            bool pointer=terminal.IsFocused && terminal.PointerActive && PcLogin.IsUnlocked(terminal);
            var point=terminal.PointerPosition;
            bool desktopFree=!(GetComponent<ShopApp>()?.IsOpen??false) && !(GetComponent<SatelliteConsoleApp>()?.IsOpen??false) && !(GetComponent<RepairMessagesApp>()?.IsOpen??false) && !(GetComponent<MoneyGetApp>()?.IsOpen??false);
            bool overIcon=Purchased && !IsOpen && desktopFree && pointer && icon.worldBound.Contains(point);
            icon.EnableInClassList("pointer-over",overIcon);
            if(IsOpen)
            {
                foreach(var entry in buttons)entry.element.EnableInClassList("mf-hover",pointer && entry.element.worldBound.Contains(point));
                foreach(var portrait in portraits)UpdatePortrait(portrait.image,portrait.profile);
                if(pointer && window.worldBound.Contains(point) && Mouse.current!=null)
                {
                    float wheel=Mouse.current.scroll.ReadValue().y;
                    if(Mathf.Abs(wheel)>0.01f){scrollToLatest=false;transcript.scrollOffset+=Vector2.up*(-wheel*0.35f);}
                }
            }
            object audioHover=overIcon?(object)icon:null;
            if(IsOpen&&pointer)foreach(var audioAction in buttons)if(true&&audioAction.element.worldBound.Contains(point))audioHover=string.IsNullOrEmpty(audioAction.element.name)?(object)audioAction.element:audioAction.element.name;
            GameAudio.Hover(this,audioHover);
            if(audioHover!=null&&pointer&&Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame)GameAudio.Ui(Sound.Icon);
            if(!pointer || Mouse.current==null || !Mouse.current.leftButton.wasPressedThisFrame)return;
            if(overIcon){SetOpen(true);return;}
            if(!IsOpen)return;
            // Refresh rebuilds the action list; invoke only one action per click.
            for(int i=buttons.Count-1;i>=0;i--)if(buttons[i].element.worldBound.Contains(point)){buttons[i].action();break;}
        }
        Line Reply(MoonFinderText text,bool romance=false)=>new Line{text=text,pending=true,begins=Time.unscaledTime+UnityEngine.Random.Range(replyDelay.x,replyDelay.y),romance=romance};
        void UpdateTyping()
        {
            if(GameSettingsMenu.BlockGameplayInput)return;
            bool refresh=false,completed=false;
            foreach(var pair in chats)
            {
                var chat=pair.Value;
                foreach(var line in chat.history)
                {
                    if(!line.pending||Time.unscaledTime<line.begins)continue;
                    int shown=Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime-line.begins)*Mathf.Max(5,charactersPerSecond)),0,line.text.Value.Length);
                    if(line.shown!=shown)
                    {
                        if(IsOpen&&selected==pair.Key&&terminal.IsFocused){GameAudio.Ui(Sound.Keyboard);refresh=true;}
                        line.shown=shown;
                    }
                    if(shown<line.text.Value.Length)continue;
                    line.pending=false;completed=true;if(selected==pair.Key)refresh=true;
                    if(line.romance&&!chat.romanced){chat.romanced=true;rewards.Grant(content.Find(pair.Key).reward);}
                }
            }
            if(refresh)typingRefreshPending=true;
            if(typingRefreshPending&&(completed||Time.unscaledTime>=typingRefreshAt))
            {typingRefreshPending=false;typingRefreshAt=Time.unscaledTime+.07f;Refresh();}
        }
        void LateUpdate()
        {
            if(!scrollToLatest || !IsOpen || Time.frameCount<scrollAfterFrame || transcript.contentContainer.childCount==0)return;
            var last=transcript.contentContainer[transcript.contentContainer.childCount-1];
            if(last.layout.height<=0)return;
            transcript.scrollOffset=new Vector2(0,Mathf.Max(0,transcript.verticalScroller.highValue));
            if(last.worldBound.yMax<=transcript.contentViewport.worldBound.yMax+1)scrollToLatest=false;
        }
        void Build()
        {
            if(window!=null)return;
            var desktop=terminal.Root.Q("desktop");if(desktop==null)return;
            boundRoot=terminal.Root;
            terminal.Root.styleSheets.Add(Resources.Load<StyleSheet>("MoonFinder"));
            icon=Box("desktop-icon");icon.name="moonfinder";icon.style.position=Position.Absolute;
            icon.style.left=(terminal.assets.resolution.x-164)*0.5f;icon.style.top=(terminal.assets.resolution.y-128)*0.5f;
            icon.Add(new Image{vectorImage=content.moonIcon,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore});icon[0].AddToClassList("icon-image");
            icon.Add(Text("MoonFinder","icon-label"));icon.style.display=Purchased?DisplayStyle.Flex:DisplayStyle.None;desktop.Add(icon);
            window=Box("mf-window");window.name="moonfinder-window";window.style.display=DisplayStyle.None;desktop.Add(window);
            var header=Box("mf-header");window.Add(header);heading=Text("MoonFinder","mf-heading");header.Add(heading);
            subtitle=Text("","mf-muted");subtitle.style.flexGrow=1;header.Add(subtitle);close=Text("[X]","mf-close");header.Add(close);
            var body=Box("mf-body");window.Add(body);people=Box("mf-people");body.Add(people);
            conversation=Box("mf-chat");body.Add(conversation);
            var identity=Box("mf-identity");conversation.Add(identity);avatar=new Image{scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};avatar.AddToClassList("mf-avatar");identity.Add(avatar);
            var about=Box("mf-about");identity.Add(about);profileName=Text("","mf-name");description=Text("","mf-description");about.Add(profileName);about.Add(description);
            transcript=new ScrollView(ScrollViewMode.Vertical){name="moonfinder-transcript",pickingMode=PickingMode.Ignore,verticalScrollerVisibility=ScrollerVisibility.Hidden};transcript.AddToClassList("mf-transcript");conversation.Add(transcript);
            choices=Box("mf-choices");conversation.Add(choices);state=Text("","mf-state");conversation.Add(state);
            terminal.Root.Q("screen-cursor")?.BringToFront();Refresh();
        }
        void Refresh()
        {
            if(window==null || !content)return;
            subtitle.text=L("  [PRIVATE LUNAR CONNECTIONS]","  [ЛИЧНЫЕ ЛУННЫЕ СВЯЗИ]");
            buttons.Clear();portraits.Clear();buttons.Add((close,Close));people.Clear();
            foreach(var profile in content.profiles)
            {
                var row=Box("mf-person");row.name="moonfinder-profile-"+profile.id;row.EnableInClassList("mf-selected",selected==profile.id);
                Image portrait=new Image{scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};portrait.AddToClassList("mf-small-avatar");SetPortrait(portrait,profile);row.Add(portrait);
                row.Add(Text(profile.name.Value,"mf-person-name"));row.Add(Text(IsRomanced(profile.id)?"<3":L("OPEN CHAT","ОТКРЫТЬ ЧАТ"),"mf-muted"));people.Add(row);
                string id=profile.id;buttons.Add((row,()=>SelectProfile(id)));
            }
            if(selected==null){conversation.style.visibility=Visibility.Hidden;return;}
            conversation.style.visibility=Visibility.Visible;
            var person=content.Find(selected);var chat=ChatFor(selected);
            profileName.text=person.name.Value;description.text=person.description.Value;SetPortrait(avatar,person);
            transcript.Clear();
            foreach(var line in chat.history)
            {
                var message=Text((line.player?L("YOU","ВЫ"):person.name.Value)+": "+line.text.Value,line.player?"mf-message mf-outgoing":"mf-message");
                if(line.pending)message.text=(line.player?L("YOU","ВЫ"):person.name.Value)+": "+(line.shown<=0?L("Typing...","Печатает..."):line.text.Value.Substring(0,Mathf.Min(line.shown,line.text.Value.Length)));
                transcript.Add(message);
            }
            choices.Clear();appleButton=null;
            if(person.apple&&!IsTyping)
            {
                appleButton=Box("mf-apple");appleButton.name="moonfinder-apple";
                appleButton.Add(new Image{vectorImage=content.appleIcon,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore});
                if(chat.romanced)appleButton.Add(Text("<3","mf-muted"));choices.Add(appleButton);
                buttons.Add((appleButton,()=>ClickApple()));
            }
            else if(CurrentNode!=null&&!IsTyping)
            {
                if(chat.choiceNode!=chat.node || chat.choiceOrder==null || chat.choiceOrder.Length!=CurrentNode.choices.Length)
                {
                    chat.choiceNode=chat.node;chat.choiceOrder=new int[CurrentNode.choices.Length];
                    for(int i=0;i<chat.choiceOrder.Length;i++)chat.choiceOrder[i]=i;
                    if(person.shuffleChoices)
                        for(int i=chat.choiceOrder.Length-1;i>0;i--)
                        {
                            int j=UnityEngine.Random.Range(0,i+1);
                            (chat.choiceOrder[i],chat.choiceOrder[j])=(chat.choiceOrder[j],chat.choiceOrder[i]);
                        }
                }
                for(int i=0;i<CurrentNode.choices.Length;i++)
                {
                    int option=chat.choiceOrder[i];var button=Text("> "+CurrentNode.choices[option].text.Value,"mf-option");button.name="moonfinder-choice-"+i;choices.Add(button);buttons.Add((button,()=>Choose(option)));
                }
            }
            state.text=IsTyping?L("INCOMING MESSAGE [TYPING]","ВХОДЯЩЕЕ СООБЩЕНИЕ [ПЕЧАТАЕТ]"):chat.romanced?L("<3 CONNECTION ESTABLISHED","<3 СВЯЗЬ УСТАНОВЛЕНА"):L("ENCRYPTED [NO READ RECEIPTS]","ЗАШИФРОВАНО [БЕЗ ОТМЕТОК О ПРОЧТЕНИИ]");
            // The next layout pass measures wrapped messages and updates the scroll range.
            scrollToLatest=true;scrollAfterFrame=Time.frameCount+2;
        }
        void SetPortrait(Image image,MoonFinderProfile profile)
        {
            image.image=null;image.vectorImage=null;
            if(profile.apple)image.vectorImage=content.appleIcon;
            else {portraits.Add((image,profile));UpdatePortrait(image,profile);}
        }
        void UpdatePortrait(Image image,MoonFinderProfile profile)
        {
            var chat=ChatFor(profile.id);var node=profile.Find(chat.node);var emotion=chat.romanced?MoonFinderEmotion.Love:node?.emotion??MoonFinderEmotion.Neutral;
            Texture2D texture=emotion==MoonFinderEmotion.Love?profile.love:emotion==MoonFinderEmotion.Angry?profile.angry:profile.portrait;
            // An occasional short blink; expressions remain visible between blinks.
            if(emotion==MoonFinderEmotion.Neutral && Mathf.Repeat(Time.time+Array.IndexOf(content.profiles,profile)*1.27f,3.7f)<0.13f && profile.blink)texture=profile.blink;
            image.image=texture?texture:profile.portrait;
        }
        static string L(string en,string ru)=>Loc.Russian?ru:en;
        static VisualElement Box(string css){var e=new VisualElement{pickingMode=PickingMode.Ignore};e.AddToClassList(css);return e;}
        static Label Text(string text,string css){var e=new Label(text){pickingMode=PickingMode.Ignore};foreach(var c in css.Split(' '))e.AddToClassList(c);return e;}
    }
}
