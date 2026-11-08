using MoonThemedGame.Audio;
using System.Collections.Generic;
using MoonThemedGame.Interactions;
using MoonThemedGame.Repair;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor=UnityEngine.Cursor;

namespace MoonThemedGame.Computer
{
    // Owns run-level screens, not the PC's application windows. Unlock survives new runs;
    // money, software purchases and the free evacuation belong to the current run.
    public sealed class StationStoryFlow : MonoBehaviour
    {
        public static StationStoryFlow Active {get;private set;}
        public static bool IsBlocking=>Active && Active.UsesCrt;
        public static bool BotUnavailable=>Active && Active.botAbsent;
        public StoryFinaleContent content;
        public bool Fired=>screen==Screen.Fired;
        public bool CreditsVisible=>screen==Screen.Credits;
        enum Screen { None, Thanks, Credits, Fired }
        Screen screen;
        bool UsesCrt=>screen==Screen.Credits||screen==Screen.Fired;
        PcMonitorTerminal terminal;
        ScavengerLink link;
        AurinkoDismantling aurinko;
        bool botAbsent,botWasActive;
        float started,previousTimeScale;
        CursorLockMode previousLock;
        bool previousVisible;
        PanelSettings panel;
        GameObject overlay;
        VisualElement root,card,actions;
        Label title,subtitle;
        readonly List<VisualElement> creditRows=new List<VisualElement>();
        readonly List<(Button button,System.Action action)> creditActions=new List<(Button,System.Action)>();
        RenderTexture creditsSource,creditsDisplay;
        Material creditsTube;
        PanelSettings creditsPresentationPanel;
        GameObject creditsPresentation;
        VisualElement creditsPresentationRoot;
        Image creditsSurface;
        static string T(string en,string ru)=>Loc.Russian?ru:en;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){Active=null;SceneManager.sceneLoaded-=Loaded;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Watch(){SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;}
        static void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(scene.name!="Operator")return;
            if(!FindFirstObjectByType<StationStoryFlow>())new GameObject("Station / Run and story finale").AddComponent<StationStoryFlow>();
        }
        void Awake()
        {
            Active=this;content=Resources.Load<StoryFinaleContent>("StoryFinaleContent");
            if(!content)content=ScriptableObject.CreateInstance<StoryFinaleContent>();
        }
        void OnEnable()=>Active=this;
        void Resolve(){if(!terminal)terminal=FindFirstObjectByType<PcMonitorTerminal>();if(terminal&&!link)link=terminal.GetComponent<ScavengerLink>();}
        void Update()
        {
            Resolve();
            if(screen!=Screen.None&&root==null)
            {
                ClearDisplay();BuildSafely();
                if(screen==Screen.None)return;
            }
            if(screen==Screen.None)
            {
                if(!botAbsent && link && link.state && link.state.Status==RobotStatus.Offline && !link.state.CanAffordEvacuation(terminal) &&
                    !(terminal.GetComponent<CrazyCosmonautsApp>()?.Purchased??false))Show(Screen.Fired);
                return;
            }
            float elapsed=Time.unscaledTime-started;
            if(screen==Screen.Thanks)
            {
                title.text=Loc.Russian?content.thanksRussian:content.thanksEnglish;
                root.style.opacity=Mathf.Clamp01(Mathf.Min(elapsed/.7f,(4-elapsed)/.7f));
                if(elapsed>=4)Dismiss();return;
            }
            if(screen==Screen.Credits)
            {
                string heading=T("THANK YOU FOR PLAYING PALE HORSE","СПАСИБО ЗА ИГРУ В PALE HORSE");
                title.text=heading.Substring(0,Mathf.Min(heading.Length,Mathf.FloorToInt(elapsed*25)));
                foreach(var row in creditRows)
                {
                    int index=creditRows.IndexOf(row);float appear=Mathf.Clamp01((elapsed-1.4f-index*1.15f)/.8f);
                    row.style.opacity=appear;row.style.translate=new Translate(0,(1-appear)*16,0);
                }
                actions.style.opacity=Mathf.Clamp01(elapsed-6);actions.SetEnabled(elapsed>=6);
                if(elapsed>=6)UpdateCreditsPointer();
                if(elapsed>=6&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)Dismiss();
            }
            else if(screen==Screen.Fired)UpdateCreditsPointer();
        }
        public void BeginAurinko(AurinkoDismantling robot)
        {
            Resolve();aurinko=robot;
            if(link){link.Disconnect();if(link.robot){botWasActive=link.robot.gameObject.activeSelf;link.robot.gameObject.SetActive(false);}}
            botAbsent=true;
        }
        public void CancelAurinko(){aurinko=null;RestoreBot();}
        void RestoreBot()
        {
            if(botAbsent&&link&&link.robot)link.robot.gameObject.SetActive(botWasActive);
            botAbsent=false;
        }
        public void Thank(AurinkoDismantling robot){if(robot==aurinko)Show(Screen.Thanks);}
        public void Collected(AurinkoDismantling robot)
        {
            if(robot!=aurinko||!robot.Complete)return;
            StationRun.UnlockEndless();StationRun.ContinueEndless();terminal.GetComponent<RepairMessagesApp>()?.FinaleCollected();
            aurinko=null;Show(Screen.Credits);
        }
        void Show(Screen next)
        {
            Resolve();
            if(screen!=Screen.None)Dismiss(false);
            if(next==Screen.Thanks)
            {
                screen=next;started=Time.unscaledTime;BuildSafely();return;
            }
            FindFirstObjectByType<DeveloperConsole>()?.Close();
            var settings=FindFirstObjectByType<GameSettingsMenu>();if(GameSettingsMenu.IsOpen&&settings)settings.Close();
            if(link)link.Disconnect();if(terminal)terminal.ExitFocus(true);
            previousTimeScale=Time.timeScale;previousLock=Cursor.lockState;previousVisible=Cursor.visible;
            screen=next;started=Time.unscaledTime;Time.timeScale=0;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            BuildSafely();
        }
        public void Dismiss()=>Dismiss(true);
        void Dismiss(bool restoreBot)
        {
            if(screen==Screen.None)return;
            var was=screen;screen=Screen.None;
            if(was!=Screen.Thanks){Time.timeScale=previousTimeScale;Cursor.lockState=previousLock;Cursor.visible=previousVisible;}
            ClearDisplay();
            if(restoreBot&&was==Screen.Credits)RestoreBot();
        }
        void ClearDisplay()
        {
            if(root!=null)root.style.display=DisplayStyle.None;
            if(panel)panel.targetTexture=null;
            if(creditsPresentationRoot!=null)creditsPresentationRoot.style.display=DisplayStyle.None;
            if(creditsSurface!=null)creditsSurface.image=null;
            if(creditsPresentation)Destroy(creditsPresentation);
            if(creditsPresentationPanel)Destroy(creditsPresentationPanel);
            creditsPresentation=null;creditsPresentationPanel=null;creditsPresentationRoot=null;creditsSurface=null;
            if(overlay)Destroy(overlay);if(panel)Destroy(panel);root=null;creditRows.Clear();
            overlay=null;panel=null;title=null;subtitle=null;actions=null;card=null;
            creditActions.Clear();
            if(creditsSource){creditsSource.Release();Destroy(creditsSource);creditsSource=null;}
            if(creditsDisplay){creditsDisplay.Release();Destroy(creditsDisplay);creditsDisplay=null;}
            if(creditsTube){Destroy(creditsTube);creditsTube=null;}
        }
        void BuildSafely()
        {
            try{Build();}
            catch(System.Exception exception){Dismiss();Debug.LogException(exception,this);}
        }
        void Load(string scene)
        {
            if(!Application.CanStreamedLevelBeLoaded(scene))return;
            if(scene=="Operator")StationRun.Select(StationRun.Mode);
            Dismiss();RestoreBot();Time.timeScale=1;SceneManager.LoadSceneAsync(scene);
        }
        void Build()
        {
            var assets=Resources.Load<SettingsMenuAssets>("SettingsMenuAssets");
            panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=assets.theme;
            panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1280,800);panel.match=.5f;
            overlay=new GameObject("Station / "+screen+" screen");overlay.transform.SetParent(transform,false);
            var doc=overlay.AddComponent<UIDocument>();doc.panelSettings=panel;doc.sortingOrder=10000;root=doc.rootVisualElement;
            root.style.flexGrow=1;root.style.unityFontDefinition=FontDefinition.FromFont(assets.font);
            if(screen==Screen.Thanks)
            {
                root.pickingMode=PickingMode.Ignore;root.style.opacity=0;
                title=new Label(Loc.Russian?content.thanksRussian:content.thanksEnglish){pickingMode=PickingMode.Ignore};
                title.style.position=Position.Absolute;title.style.left=Length.Percent(5);title.style.right=Length.Percent(5);title.style.bottom=Length.Percent(14);
                title.style.fontSize=24;title.style.color=new Color(.94f,.96f,.94f);title.style.whiteSpace=WhiteSpace.Normal;
                title.style.unityTextAlign=TextAnchor.MiddleCenter;title.style.unityTextOutlineWidth=1.5f;title.style.unityTextOutlineColor=new Color(0,0,0,.9f);
                root.Add(title);return;
            }
            root.style.backgroundColor=new Color(.006f,.011f,.012f);
            root.style.justifyContent=Justify.Center;root.style.alignItems=Align.Center;
            card=new VisualElement();card.style.width=Length.Percent(82);card.style.maxWidth=960;root.Add(card);
            Label Text(string text,int size,Color color)
            {
                var label=new Label(text);label.style.fontSize=size;label.style.color=color;label.style.whiteSpace=WhiteSpace.Normal;card.Add(label);return label;
            }
            bool tube=UsesCrt;
            if(tube)
            {
                creditsSource=new RenderTexture(1280,800,24){name=screen+" / terminal content",filterMode=FilterMode.Bilinear};creditsSource.Create();
                creditsDisplay=new RenderTexture(1280,800,0){name=screen+" / curved CRT",filterMode=FilterMode.Bilinear};creditsDisplay.Create();
                var shader=Resources.Load<Shader>("StoryCreditsCRT");
                if(shader&&shader.isSupported){creditsTube=new Material(shader);creditsTube.SetTexture("_BaseMap",creditsSource);}
                panel.targetTexture=creditsSource;panel.clearColor=true;panel.colorClearValue=Color.black;
                panel.SetScreenToPanelSpaceFunction(_=>new Vector2(-10000,-10000));
                root.style.width=1280;root.style.height=800;root.style.backgroundColor=new Color(.004f,.006f,.002f);
                // Present with an on-screen runtime panel. The content panel above only
                // renders into a texture and cannot display itself over the paused scene.
                creditsPresentationPanel=ScriptableObject.CreateInstance<PanelSettings>();
                creditsPresentationPanel.themeStyleSheet=assets.theme;creditsPresentationPanel.sortingOrder=10000;
                creditsPresentationPanel.scaleMode=PanelScaleMode.ConstantPixelSize;
                creditsPresentation=new GameObject("Station / "+screen+" screen presentation");creditsPresentation.transform.SetParent(transform,false);
                var presentationDoc=creditsPresentation.AddComponent<UIDocument>();presentationDoc.panelSettings=creditsPresentationPanel;
                creditsPresentationRoot=presentationDoc.rootVisualElement;creditsPresentationRoot.style.flexGrow=1;
                creditsPresentationRoot.style.width=Length.Percent(100);creditsPresentationRoot.style.height=Length.Percent(100);
                creditsPresentationRoot.style.backgroundColor=Color.black;creditsPresentationRoot.pickingMode=PickingMode.Ignore;
                creditsSurface=new Image{image=creditsDisplay,scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};
                creditsSurface.style.position=Position.Absolute;creditsSurface.style.left=creditsSurface.style.right=0;
                creditsSurface.style.top=creditsSurface.style.bottom=0;creditsPresentationRoot.Add(creditsSurface);
            }
            var mint=tube?new Color(.92f,.88f,.72f):new Color(.67f,.83f,.77f);
            var dim=tube?new Color(.53f,.5f,.35f):new Color(.32f,.47f,.45f);
            var amber=tube?new Color(.96f,.9f,.66f):new Color(.86f,.72f,.43f);
            Text(T("PALE.HORSE [LUNAR TECHNICAL STATION 7]","PALE.HORSE [ЛУННАЯ ТЕХНИЧЕСКАЯ СТАНЦИЯ 7]"),15,dim).style.marginBottom=26;
            if(screen==Screen.Credits&&content.logo)
            {
                var logo=new Image{vectorImage=content.logo,scaleMode=ScaleMode.ScaleToFit};logo.style.width=100;logo.style.height=100;logo.style.marginBottom=24;card.Add(logo);
            }
            title=Text(screen==Screen.Fired?T("YOU ARE FIRED","ВЫ УВОЛЕНЫ"):screen==Screen.Thanks?(Loc.Russian?content.thanksRussian:content.thanksEnglish):"",screen==Screen.Fired?56:36,screen==Screen.Fired?amber:mint);
            subtitle=Text(screen==Screen.Credits?T("ENDLESS MODE OPENED","БЕСКОНЕЧНЫЙ РЕЖИМ ОТКРЫТ"):screen==Screen.Fired?T("CONTRACT TERMINATED [RECOVERY FUNDS UNAVAILABLE]","КОНТРАКТ РАСТОРГНУТ [НЕТ СРЕДСТВ НА ЭВАКУАЦИЮ]"):"SCAV_BOT_AURINKO [SIGNAL CLOSED]",15,dim);
            subtitle.style.marginTop=12;subtitle.style.marginBottom=26;
            if(screen==Screen.Credits)
            {
                for(int i=0;i<content.credits.Length;i++)
                {
                    var credit=content.credits[i];var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.marginBottom=20;row.style.opacity=0;card.Add(row);creditRows.Add(row);
                    var name=new Label(credit.name);name.style.width=280;name.style.fontSize=20;name.style.color=amber;row.Add(name);
                    var role=new Label(Loc.Russian?credit.russianRole:credit.englishRole);role.style.fontSize=15;role.style.color=mint;role.style.whiteSpace=WhiteSpace.Normal;row.Add(role);
                }
                var closing=Text(Loc.Russian?content.closingRussian:content.closingEnglish,15,dim);closing.style.marginTop=8;
            }
            actions=new VisualElement();actions.style.flexDirection=FlexDirection.Row;actions.style.marginTop=28;card.Add(actions);
            void Button(string label,System.Action callback)
            {
                var button=new Button(callback){text=label};button.style.backgroundColor=Color.clear;button.style.color=mint;button.style.fontSize=17;
                button.style.borderLeftWidth=button.style.borderRightWidth=button.style.borderTopWidth=button.style.borderBottomWidth=0;
                button.style.marginRight=30;button.style.paddingLeft=0;actions.Add(button);
                if(tube)creditActions.Add((button,callback));
            }
            if(screen==Screen.Credits)Button(T("[CONTINUE / ENDLESS]","[ПРОДОЛЖИТЬ / БЕСКОНЕЧНЫЙ]"),Dismiss);
            if(screen==Screen.Fired)Button(T("[START AGAIN]","[НАЧАТЬ ЗАНОВО]"),()=>Load("Operator"));
            if(screen!=Screen.Thanks)Button(T("[RETURN TO MENU]","[В МЕНЮ]"),()=>Load("MENU"));
            if(screen==Screen.Credits){actions.style.opacity=0;actions.SetEnabled(false);}
        }
        void UpdateCreditsPointer()
        {
            var mouse=Mouse.current;if(mouse==null)return;
            var position=mouse.position.ReadValue();
            var uv=new Vector2(position.x/UnityEngine.Screen.width,position.y/UnityEngine.Screen.height);
            if(creditsTube)uv=PcMonitorTerminal.WarpUV(uv,Time.unscaledTime,creditsTube);
            var point=new Vector2(uv.x*1280,(1-uv.y)*800);
            object audioHover=null;
            foreach(var entry in creditActions)
            {
                bool hover=entry.button.worldBound.Contains(point);entry.button.style.color=hover?Color.white:new Color(.92f,.88f,.72f);
                if(hover)audioHover=entry.button;
                if(hover&&mouse.leftButton.wasPressedThisFrame){GameAudio.Ui(Sound.Icon);entry.action();break;}
            }
            GameAudio.Hover(this,audioHover);
        }
        internal void RenderCredits()
        {
            if(!creditsSource||!creditsDisplay)return;
            var previous=RenderTexture.active;
            try
            {
                if(creditsTube){creditsTube.SetFloat("_PreviewTime",Time.unscaledTime);Graphics.Blit(creditsSource,creditsDisplay,creditsTube);}
                else Graphics.Blit(creditsSource,creditsDisplay);
            }
            finally{RenderTexture.active=previous;}
        }
        void LateUpdate(){if(UsesCrt)RenderCredits();}
        void OnDestroy(){if(screen!=Screen.None)Dismiss(false);if(Active==this)Active=null;}
    }
}
