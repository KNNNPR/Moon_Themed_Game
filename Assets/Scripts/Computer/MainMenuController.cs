using System;
using System.Collections.Generic;
using MoonThemedGame.Interactions;
using MoonThemedGame.Weather;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor=UnityEngine.Cursor;

namespace MoonThemedGame.Computer
{
    [DefaultExecutionOrder(-9000),DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        public MainMenuAssets assets;
        public WeatherMonitorDisplay monitor;
        public Camera viewCamera;
        public Transform powerButton;
        public MeshRenderer[] floatingMonitorParts;
        [Min(1)] public float overviewDistance=3.1f;
        [Min(.1f)] public float focusDuration=1.15f;
        [Range(20,70)] public float overviewFov=50;
        public bool useAuthoredCamera;
        public Vector3 overviewPosition;
        public Vector3 overviewEuler;

        public static bool IsMenuScene=>SceneManager.GetActiveScene().name.Equals("MENU",StringComparison.OrdinalIgnoreCase);
        public static MainMenuController Active {get;private set;}
        public bool IsFocused {get;private set;}
        public bool Powered {get;private set;}=true;
        public bool IsLoading {get;private set;}
        public VisualElement ScreenRoot=>screenRoot;
        public RenderTexture DisplayTexture=>display;
        public MainMenuTitle Title=>title;
        public float FocusAmount=>focus;
        public int FloatingAppleCount=>floatingApples.Count;

        readonly Dictionary<string,Button> buttons=new Dictionary<string,Button>();
        readonly Queue<MenuZeroGravity> floatingApples=new Queue<MenuZeroGravity>();
        Transform appleRoot;
        int appleSequence;
        const int MaximumFloatingApples=6;
        MeshRenderer surface;
        Collider surfaceCollider,powerCollider;
        InteractionOutline highlight;
        Material originalMaterial,screenMaterial;
        RenderTexture display;
        PanelSettings screenPanel,overlayPanel;
        VisualElement screenRoot,overlayRoot;
        Label status,hint;
        MainMenuTitle title;
        GameObject floatingRig;
        Vector3 screenLocalCenter,screenLocalNormal,screenLocalUp;
        Vector3 anchorCenter,anchorNormal,anchorUp,anchorRight;
        Vector2 screenSize;
        Quaternion authoredRotation;
        float focus,started,powerOff;
        bool initialized;
        bool modeSelection;
        int keyboardButton=-1;
        const float FocusFov=38;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState(){Active=null;SceneManager.sceneLoaded-=Loaded;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void WatchScenes(){SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;}
        static void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(!IsMenuScene)return;
            var menu=FindFirstObjectByType<MainMenuController>();
            if(!menu)menu=new GameObject("PALE HORSE / Main Menu").AddComponent<MainMenuController>();
            menu.Initialize();
        }
        void Awake(){if(IsMenuScene)Initialize();}
        void Start(){if(IsMenuScene)DisableGameplay();}

        public void Initialize()
        {
            if(initialized || !IsMenuScene)return;
            assets=assets?assets:Resources.Load<MainMenuAssets>("MainMenuAssets");
            monitor=monitor?monitor:FindFirstObjectByType<WeatherMonitorDisplay>();
            if(!viewCamera && monitor)
            {
                float nearest=float.MaxValue;
                foreach(var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    float distance=Vector3.Distance(camera.transform.position,monitor.transform.position);
                    if(distance<nearest){nearest=distance;viewCamera=camera;}
                }
            }
            if(!assets || !monitor || !monitor.assets || !viewCamera)
            {Debug.LogError("MENU requires MainMenuAssets, Smaller_monitor and a camera.",this);return;}
            initialized=true;Active=this;started=Time.unscaledTime;
            surface=monitor.screenRenderer;
            screenLocalCenter=monitor.screenCenter;screenLocalNormal=monitor.screenNormal;screenLocalUp=monitor.screenUp;screenSize=monitor.screenSize;
            anchorCenter=surface.transform.TransformPoint(screenLocalCenter);
            anchorNormal=surface.transform.TransformDirection(screenLocalNormal).normalized;
            anchorUp=surface.transform.TransformDirection(screenLocalUp).normalized;
            anchorRight=Vector3.Cross(anchorNormal,anchorUp).normalized;
            authoredRotation=Quaternion.Euler(overviewEuler);
            DisableGameplay();
            foreach(var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))camera.enabled=camera==viewCamera;
            foreach(var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))listener.enabled=listener.gameObject==viewCamera.gameObject;
            if(!viewCamera.GetComponent<AudioListener>())viewCamera.gameObject.AddComponent<AudioListener>();
            foreach(var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)){body.isKinematic=true;body.useGravity=false;}
            // A BoxCollider has no surface UVs; pointer mapping needs the rounded screen mesh.
            var screenMesh=surface.GetComponent<MeshCollider>();
            if(!screenMesh)screenMesh=surface.gameObject.AddComponent<MeshCollider>();
            screenMesh.sharedMesh=surface.GetComponent<MeshFilter>().sharedMesh;surfaceCollider=screenMesh;
            surfaceCollider.enabled=true;
            ResolvePowerButton();
            ApplyCamera(0,0);
            BuildMonitor();BuildOverlay();BuildFloatingMonitor();
            ApplyCamera(0,0);BuildApples();
            Loc.Changed+=Relocalize;Relocalize();
            Time.timeScale=1;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        void DisableGameplay()
        {
            foreach(var script in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if(script==this || script is GameSettingsMenu || script is DeveloperConsole || script is MenuZeroGravity ||
                    script is InteractionOutline || script is Rendering.VisualStyleSwitcher || script is Scavenger.RepairBayFogVolume ||
                    script is ChargeMonitorDisplay)continue;
                if(script is FirstPersonController || script is FirstPersonCameraEffects || (script.GetType().Namespace??"").StartsWith("MoonThemedGame",StringComparison.Ordinal))script.enabled=false;
            }
        }
        void ResolvePowerButton()
        {
            if(!powerButton)
            {
                float nearest=float.MaxValue;
                foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    string key=renderer.name.ToLowerInvariant();
                    bool red=false;
                    foreach(var material in renderer.sharedMaterials)if(material && material.name.Equals("RED",StringComparison.OrdinalIgnoreCase))red=true;
                    if(!red && !key.Contains("red"))continue;
                    float distance=Vector3.Distance(renderer.bounds.center,anchorCenter);
                    if(distance<nearest && distance<1.4f){nearest=distance;powerButton=renderer.transform;}
                }
            }
            if(!powerButton)return;
            powerCollider=powerButton.GetComponent<Collider>();
            if(!powerCollider)
            {
                var filter=powerButton.GetComponent<MeshFilter>();
                if(filter && filter.sharedMesh){var collider=powerButton.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;powerCollider=collider;}
            }
            if(powerCollider)powerCollider.enabled=true;
        }
        void BuildMonitor()
        {
            var weather=monitor.assets;
            display=new RenderTexture(640,1080,0,RenderTextureFormat.ARGB32){name="MENU / live terminal",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};display.Create();
            originalMaterial=surface.sharedMaterial;
            screenMaterial=new Material(weather.screenMaterial){name="MENU / CRT"};screenMaterial.SetTexture("_BaseMap",display);
            if(screenMaterial.HasProperty("_WaveStrength"))screenMaterial.SetFloat("_WaveStrength",.00085f);
            if(screenMaterial.HasProperty("_PowerOff"))screenMaterial.SetFloat("_PowerOff",0);
            surface.sharedMaterial=screenMaterial;
            screenPanel=ScriptableObject.CreateInstance<PanelSettings>();screenPanel.themeStyleSheet=weather.theme;
            screenPanel.targetTexture=display;screenPanel.scaleMode=PanelScaleMode.ConstantPixelSize;screenPanel.scale=1;
            screenPanel.clearColor=true;screenPanel.colorClearValue=Color.black;
            screenPanel.SetScreenToPanelSpaceFunction(_=>new Vector2(-10000,-10000));
            var host=new GameObject("Menu / monitor UI");host.transform.SetParent(transform,false);
            var doc=host.AddComponent<UIDocument>();doc.panelSettings=screenPanel;screenRoot=doc.rootVisualElement;
            screenRoot.name="main-menu-screen";screenRoot.style.width=640;screenRoot.style.height=1080;
            screenRoot.style.unityFontDefinition=FontDefinition.FromFont(weather.font);screenRoot.styleSheets.Add(assets.screenStyle);screenRoot.AddToClassList("menu-screen");
            Text(screenRoot,"PALE.HORSE.BUILD_RUV","menu-kicker");Text(screenRoot,"STATION 7","menu-heading");
            screenRoot.Q<Label>(className:"menu-heading").name="menu-station";
            var subtitle=Text(screenRoot,"OPERATOR TERMINAL","menu-subtitle");subtitle.name="menu-subtitle";
            AddButton("wake",()=>ShowModes(true));
            AddButton("story",()=>StartGame(StationGameMode.Story));AddButton("endless",()=>StartGame(StationGameMode.Endless));AddButton("back",()=>ShowModes(false));
            AddButton("settings",OpenSettings);AddButton("exit",RequestExit);
            var language=new Button(Loc.Toggle){name="menu-language"};language.AddToClassList("menu-language");screenRoot.Add(language);buttons.Add("language",language);
            status=Text(screenRoot,"AWAITING OPERATOR _","menu-status");
            var footer=Text(screenRoot,"ZERO-G [SYSTEM READY]","menu-footer");footer.name="menu-footer";
            ShowModes(false);
        }
        static Label Text(VisualElement parent,string text,string css)
        {var label=new Label(text);label.AddToClassList(css);parent.Add(label);return label;}
        void AddButton(string id,Action action)
        {
            var button=new Button(action){name="menu-"+id};button.AddToClassList("menu-action");screenRoot.Add(button);buttons.Add(id,button);
            if(id=="story"||id=="endless"||id=="back")button.style.fontSize=32;
        }
        void BuildOverlay()
        {
            overlayPanel=ScriptableObject.CreateInstance<PanelSettings>();overlayPanel.themeStyleSheet=monitor.assets.theme;
            overlayPanel.scaleMode=PanelScaleMode.ScaleWithScreenSize;overlayPanel.referenceResolution=new Vector2Int(1280,800);overlayPanel.match=.5f;
            var host=new GameObject("Menu / independent title overlay");host.transform.SetParent(transform,false);
            var doc=host.AddComponent<UIDocument>();doc.panelSettings=overlayPanel;doc.sortingOrder=5;
            overlayRoot=doc.rootVisualElement;overlayRoot.style.flexGrow=1;overlayRoot.pickingMode=PickingMode.Ignore;
            overlayRoot.style.unityFontDefinition=FontDefinition.FromFont(monitor.assets.font);
            title=new MainMenuTitle(overlayRoot,transform,viewCamera,assets);
            hint=new Label {pickingMode=PickingMode.Ignore};hint.style.position=Position.Absolute;hint.style.left=Length.Percent(6);hint.style.bottom=28;
            hint.style.fontSize=13;hint.style.color=new Color(.73f,.8f,.73f);overlayRoot.Add(hint);
        }
        void BuildFloatingMonitor()
        {
            floatingRig=new GameObject("Menu / floating smaller monitor");floatingRig.transform.position=anchorCenter;
            floatingRig.transform.SetParent(transform,true);
            var parts=new HashSet<Transform> {surface.transform};
            if(floatingMonitorParts==null || floatingMonitorParts.Length==0)
            {
                var nearby=new List<MeshRenderer>();
                foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if((renderer.name=="Cube" || renderer.name=="Cylinder.001") && Vector3.Distance(renderer.bounds.center,anchorCenter)<1.5f)nearby.Add(renderer);
                floatingMonitorParts=nearby.ToArray();
            }
            if(powerButton)parts.Add(powerButton);
            if(floatingMonitorParts!=null)foreach(var part in floatingMonitorParts)if(part)parts.Add(part.transform);
            foreach(var part in parts)part.SetParent(floatingRig.transform,true);
            var drift=floatingRig.AddComponent<MenuZeroGravity>();drift.amplitude=.012f;drift.rotationAmplitude=.32f;drift.phase=.7f;
            highlight=surface.GetComponent<InteractionOutline>();if(!highlight)highlight=surface.gameObject.AddComponent<InteractionOutline>();
            var targets=new List<MeshRenderer>();foreach(var part in parts){var renderer=part.GetComponent<MeshRenderer>();if(renderer)targets.Add(renderer);}
            highlight.targets=targets.ToArray();highlight.widthPixels=2;highlight.outlineColor=new Color(.78f,.95f,.8f);
        }
        void BuildApples()
        {
            if(!assets.applePrefab)return;
            var root=new GameObject("Menu / drifting apples");root.transform.SetParent(transform,false);root.SetActive(false);
            appleRoot=root.transform;
            var positions=new[]{new Vector3(.08f,.31f,2.3f),new Vector3(.87f,.79f,2.8f),new Vector3(.84f,.2f,2.5f)};
            for(int i=0;i<positions.Length;i++)
            {
                CreateApple(viewCamera.ViewportToWorldPoint(positions[i]),assets.appleDiameter+i*.065f);
            }
            root.SetActive(true);
        }
        MenuZeroGravity CreateApple(Vector3 position,float diameter)
        {
            var apple=Instantiate(assets.applePrefab,appleRoot);apple.name="Menu Floating Apple "+(++appleSequence);
            foreach(var script in apple.GetComponentsInChildren<MonoBehaviour>())script.enabled=false;
            foreach(var collider in apple.GetComponentsInChildren<Collider>())collider.enabled=false;
            foreach(var body in apple.GetComponentsInChildren<Rigidbody>()){body.isKinematic=true;body.useGravity=false;}
            var renderer=apple.GetComponentInChildren<Renderer>();
            if(!renderer){Destroy(apple);return null;}
            var size=renderer.bounds.size;
            apple.transform.localScale*=diameter/Mathf.Max(.001f,Mathf.Max(size.x,Mathf.Max(size.y,size.z)));
            apple.transform.position=position;
            var drift=apple.AddComponent<MenuZeroGravity>();drift.amplitude=.055f;drift.rotationAmplitude=5;
            drift.phase=appleSequence*2.1f;drift.spin=new Vector3(4,6,3);
            floatingApples.Enqueue(drift);return drift;
        }
        bool PointerOverMonitor(Ray ray)
        {
            if(!highlight)return false;
            foreach(var renderer in highlight.targets)
            {
                if(!renderer)continue;
                var local=new Ray(renderer.transform.InverseTransformPoint(ray.origin),renderer.transform.InverseTransformVector(ray.direction));
                if(renderer.localBounds.IntersectRay(local))return true;
            }
            return false;
        }
        void TossApple(Ray ray)
        {
            if(!appleRoot || !assets.applePrefab)return;
            // Throw from just below the camera, rather than spawning at the cursor.
            var launch=viewCamera.transform.position+viewCamera.transform.rotation*new Vector3(.2f,-.23f,.72f);
            bool replace=floatingApples.Count>=MaximumFloatingApples;
            var drift=replace?floatingApples.Dequeue():CreateApple(launch,assets.appleDiameter);
            if(!drift)return;
            if(replace)floatingApples.Enqueue(drift);
            var velocity=(ray.GetPoint(UnityEngine.Random.Range(3.2f,3.8f))-launch)*1.05f;
            drift.spin=new Vector3(UnityEngine.Random.Range(-24f,24f),UnityEngine.Random.Range(20f,45f),UnityEngine.Random.Range(-18f,18f));
            drift.Toss(launch,UnityEngine.Random.rotation,velocity,replace);
        }
        void Update()
        {
            if(!initialized)return;
            bool blocked=GameSettingsMenu.BlockGameplayInput || IsLoading;
            var mouse=Mouse.current;var keys=Keyboard.current;
            Vector2 pointer=mouse!=null?mouse.position.ReadValue():new Vector2(-10000,-10000);
            bool click=!blocked && mouse!=null && mouse.leftButton.wasPressedThisFrame;
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
            powerOff=Mathf.MoveTowards(powerOff,Powered?0:1,dt/.65f);
            if(screenMaterial.HasProperty("_PowerOff"))screenMaterial.SetFloat("_PowerOff",powerOff);
            focus=Mathf.MoveTowards(focus,IsFocused?1:0,dt/Mathf.Max(.1f,focusDuration));
            if(!blocked && keys!=null && keys.escapeKey.wasPressedThisFrame && IsFocused){if(modeSelection)ShowModes(false);else SetFocused(false);}
            if(!blocked && viewCamera)
            {
                if(mouse!=null && mouse.delta.ReadValue().sqrMagnitude>0)keyboardButton=-1;
                var ray=viewCamera.ScreenPointToRay(pointer);
                bool power=powerCollider && powerCollider.Raycast(ray,out _,20);
                bool hit=TryScreenPoint(pointer,out var panelPoint);
                highlight.SetHighlighted(hit || power);
                hint.text=power?(Loc.Russian?"[ЛКМ] "+(Powered?"ВЫКЛЮЧИТЬ ЭКРАН":"ВКЛЮЧИТЬ ЭКРАН"):"[LMB] "+(Powered?"POWER OFF":"POWER ON")):
                    IsFocused?(Loc.Russian?"[ESC] НАЗАД":"[ESC] BACK"):(Loc.Russian?"[ЛКМ] ТЕРМИНАЛ":"[LMB] TERMINAL");
                if(click && power)TogglePower();
                else if(click && hit && !IsFocused)SetFocused(true);
                else if(click && !hit && !PointerOverMonitor(ray))TossApple(ray);
                else if(Powered && powerOff<.02f && IsFocused && focus>.97f)
                {
                    int index=0;
                    var visible=VisibleButtons();
                    foreach(var pair in visible)
                    {
                        bool hover=hit && pair.Value.worldBound.Contains(panelPoint);
                        pair.Value.EnableInClassList("is-hovered",keyboardButton>=0?index==keyboardButton:hover);
                        if(click && hover)Activate(pair.Key);index++;
                    }
                    if(keys!=null && (keys.downArrowKey.wasPressedThisFrame || keys.upArrowKey.wasPressedThisFrame))
                        keyboardButton=keyboardButton<0?(keys.downArrowKey.wasPressedThisFrame?0:visible.Count-1):(keyboardButton+(keys.downArrowKey.wasPressedThisFrame?1:visible.Count-1))%visible.Count;
                    if(keys!=null && keys.enterKey.wasPressedThisFrame && keyboardButton>=0)
                    {int i=0;foreach(var pair in visible)if(i++==keyboardButton){Activate(pair.Key);break;}}
                }
            }
            else if(highlight)highlight.SetHighlighted(false);
            float t=Time.unscaledTime-started;
            title.Animate(pointer,!blocked && mouse!=null,click,focus,dt,t);
            overlayRoot.style.opacity=GameSettingsMenu.IsOpen?0:1;
            if(status!=null)status.text=IsLoading?(Loc.Russian?"ПРОБУЖДЕНИЕ...":"WAKING UP..."):(Loc.Russian?"ОЖИДАНИЕ ОПЕРАТОРА":"AWAITING OPERATOR")+(Mathf.Repeat(t,1)<.5f?" _":"  ");
        }
        void LateUpdate(){if(initialized)ApplyCamera(focus,Time.unscaledTime-started);}
        void ApplyCamera(float amount,float time)
        {
            float t=Mathf.SmoothStep(0,1,amount);
            float tangent=Mathf.Tan(overviewFov*.5f*Mathf.Deg2Rad);
            var overview=useAuthoredCamera?overviewPosition:anchorCenter+anchorNormal*overviewDistance+anchorRight*(overviewDistance*tangent*viewCamera.aspect*.47f);
            var rotation=useAuthoredCamera?authoredRotation:Quaternion.LookRotation(-anchorNormal,anchorUp);
            float focusDistance=Mathf.Max(screenSize.y/(2*Mathf.Tan(FocusFov*.5f*Mathf.Deg2Rad)),screenSize.x/(2*Mathf.Tan(FocusFov*.5f*Mathf.Deg2Rad)*viewCamera.aspect))*1.36f;
            var center=surface.transform.TransformPoint(screenLocalCenter);
            var normal=surface.transform.TransformDirection(screenLocalNormal).normalized;
            var up=surface.transform.TransformDirection(screenLocalUp).normalized;
            var close=center+normal*focusDistance;
            var sway=(anchorRight*Mathf.Sin(time*.37f)*.012f+anchorUp*Mathf.Sin(time*.51f)*.009f)*(1-t*.8f);
            viewCamera.transform.SetPositionAndRotation(Vector3.Lerp(overview,close,t)+sway,Quaternion.Slerp(rotation,Quaternion.LookRotation(-normal,up),t)*Quaternion.Euler(Mathf.Sin(time*.42f)*.12f,Mathf.Cos(time*.31f)*.15f,Mathf.Sin(time*.27f)*.08f));
            viewCamera.fieldOfView=Mathf.Lerp(overviewFov,FocusFov,t);viewCamera.nearClipPlane=.03f;
        }
        public bool TryScreenPoint(Vector2 pointer,out Vector2 point)
        {
            point=default;if(!surfaceCollider || !viewCamera)return false;
            var ray=viewCamera.ScreenPointToRay(pointer);
            if(!surfaceCollider.Raycast(ray,out var hit,20) || Vector3.Dot(ray.direction,surface.transform.TransformDirection(screenLocalNormal))>=0)return false;
            var uv=PcMonitorTerminal.WarpUV(hit.textureCoord,Time.time,screenMaterial);
            point=new Vector2(uv.x*640,(1-uv.y)*1080);return uv.x>=0 && uv.x<=1 && uv.y>=0 && uv.y<=1;
        }
        public void SetFocused(bool value)
        {
            if(IsLoading || (value && !Powered))return;
            IsFocused=value;keyboardButton=-1;
            foreach(var button in buttons.Values)button.RemoveFromClassList("is-hovered");
        }
        public void TogglePower()
        {
            if(IsLoading)return;
            Powered=!Powered;
            if(!screenMaterial.HasProperty("_PowerOff"))screenMaterial.SetTexture("_BaseMap",Powered?display:Texture2D.blackTexture);
            if(!Powered)SetFocused(false);
        }
        public void Activate(string id)
        {
            if(!Powered || IsLoading)return;
            switch(id){case "wake":ShowModes(true);break;case "story":StartGame(StationGameMode.Story);break;case "endless":StartGame(StationGameMode.Endless);break;case "back":ShowModes(false);break;case "settings":OpenSettings();break;case "exit":RequestExit();break;case "language":Loc.Toggle();break;}
        }
        List<KeyValuePair<string,Button>> VisibleButtons()
        {
            var result=new List<KeyValuePair<string,Button>>();foreach(var pair in buttons)if(pair.Value.enabledSelf&&pair.Value.style.display.value!=DisplayStyle.None)result.Add(pair);return result;
        }
        public void ShowModes(bool visible)
        {
            modeSelection=visible;keyboardButton=-1;
            foreach(var pair in buttons)
            {
                bool mode=pair.Key=="story"||pair.Key=="endless"||pair.Key=="back";
                pair.Value.style.display=pair.Key=="language" || mode==visible?DisplayStyle.Flex:DisplayStyle.None;
            }
            if(buttons.ContainsKey("endless"))buttons["endless"].SetEnabled(StationRun.EndlessUnlocked);
        }
        public void OpenSettings(){var settings=FindFirstObjectByType<GameSettingsMenu>();if(settings)settings.Open();}
        public void RequestExit(){var settings=FindFirstObjectByType<GameSettingsMenu>();if(settings){settings.Open();settings.RequestQuit();}}
        public void StartGame()=>StartGame(StationGameMode.Story);
        public void StartGame(StationGameMode mode)
        {
            if(IsLoading || !Powered)return;
            if(!Application.CanStreamedLevelBeLoaded(assets.gameplayScene)){Debug.LogError("Add "+assets.gameplayScene+" to the build scene list.",this);return;}
            if(!StationRun.Select(mode))return;
            IsLoading=true;foreach(var button in buttons.Values)button.SetEnabled(false);
            OperatorWakeUp.Request(assets.gameplayScene);
            Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;Time.timeScale=1;
            SceneManager.LoadSceneAsync(assets.gameplayScene,LoadSceneMode.Single);
        }
        void Relocalize()
        {
            if(screenRoot==null)return;
            buttons["wake"].text=Loc.Russian?"ПРОСНУТЬСЯ":"WAKE UP";
            buttons["settings"].text=Loc.Russian?"НАСТРОЙКИ":"SETTINGS";
            buttons["exit"].text=Loc.Russian?"ВЫХОД":"EXIT";
            buttons["language"].text=Loc.Russian?"ЯЗЫК  [RU / EN]":"LANGUAGE  [EN / RU]";
            buttons["story"].text=Loc.Russian?"СЮЖЕТНЫЙ РЕЖИМ":"STORY MODE";
            buttons["endless"].text=(Loc.Russian?"БЕСКОНЕЧНЫЙ РЕЖИМ":"ENDLESS MODE")+(StationRun.EndlessUnlocked?"":(Loc.Russian?" [ЗАКРЫТО]":" [LOCKED]"));
            buttons["back"].text=Loc.Russian?"НАЗАД":"BACK";
            screenRoot.Q<Label>("menu-station").text=Loc.Russian?"СТАНЦИЯ 7":"STATION 7";
            screenRoot.Q<Label>("menu-subtitle").text=Loc.Russian?"ТЕРМИНАЛ ОПЕРАТОРА":"OPERATOR TERMINAL";
            screenRoot.Q<Label>("menu-footer").text=Loc.Russian?"НЕВЕСОМОСТЬ [СИСТЕМА ГОТОВА]":"ZERO-G [SYSTEM READY]";
            title?.Relocalize();
        }
        void OnDestroy()
        {
            Loc.Changed-=Relocalize;if(Active==this)Active=null;
            title?.Dispose();
            if(surface && surface.sharedMaterial==screenMaterial)surface.sharedMaterial=originalMaterial;
            if(screenMaterial)Destroy(screenMaterial);if(screenPanel)Destroy(screenPanel);if(overlayPanel)Destroy(overlayPanel);
            if(display){display.Release();Destroy(display);}
        }
    }
}
