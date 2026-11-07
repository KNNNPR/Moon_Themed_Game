using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MoonThemedGame.Computer;
using MoonThemedGame.Repair;
using MoonThemedGame.Scavenger;
using MoonThemedGame.Weather;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor=UnityEngine.Cursor;

namespace MoonThemedGame.Interactions
{
    [DefaultExecutionOrder(-11000),DisallowMultipleComponent]
    public sealed class DeveloperConsole : MonoBehaviour
    {
        static int consumedFrame=-1;
        public static bool IsOpen {get;private set;}
        public static bool BlockGameplayInput=>IsOpen || consumedFrame==Time.frameCount;
        static readonly string[] commands={"help","credits","balance","parts","spawn","deliver","weather","repair","end","dismantle","speed","pistol","clear","close"};
        static readonly string[] weatherNames={"sunny","fog","radiation","meteors","night","cryogenic","ash"};
        readonly List<string> lines=new List<string>(),history=new List<string>();
        public IReadOnlyList<string> Lines=>lines;
        public VisualElement Root=>root;
        public PanelSettings Panel=>panel;
        VisualElement root;
        ScrollView output;
        TextField input;
        PanelSettings panel;
        int historyIndex;
        string draft="";
        bool chordHeld,previousCursorVisible;
        float previousTimeScale;
        float caretResetTime;
        CursorLockMode previousCursor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState(){IsOpen=false;consumedFrame=-1;SceneManager.sceneLoaded-=Loaded;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;EnsureInstalled();}
        static void Loaded(Scene scene,LoadSceneMode mode)=>EnsureInstalled();
        static void EnsureInstalled()
        {if(!FindAnyObjectByType<DeveloperConsole>())new GameObject("Developer Console").AddComponent<DeveloperConsole>();}
        void Awake()=>BuildUI();
        void Update()
        {
            var keyboard=Keyboard.current;
            bool chord=keyboard!=null && keyboard.f3Key.isPressed && keyboard.f4Key.isPressed;
            if(chord && !chordHeld){if(IsOpen)Close();else Open();}
            chordHeld=chord;
            if(IsOpen && keyboard!=null && keyboard.escapeKey.wasPressedThisFrame)Close();
            if(IsOpen && input!=null)
                input.EnableInClassList("console-caret-off",Mathf.Repeat(Time.unscaledTime-caretResetTime,1f)>=.5f);
        }
        public void Open()
        {
            if(StationStoryFlow.IsBlocking)return;
            if(IsOpen)return;
            if(root==null)BuildUI();
            if(root==null)return;
            previousTimeScale=Time.timeScale;previousCursor=Cursor.lockState;previousCursorVisible=Cursor.visible;
            IsOpen=true;Time.timeScale=0;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            root.style.display=DisplayStyle.Flex;historyIndex=history.Count;
            ResetCaret();
            input.Focus();input.schedule.Execute(()=>{if(IsOpen)input.Focus();});
        }
        public void Close()
        {
            if(!IsOpen)return;
            IsOpen=false;consumedFrame=Time.frameCount;
            if(root!=null)root.style.display=DisplayStyle.None;
            Time.timeScale=previousTimeScale;Cursor.lockState=previousCursor;Cursor.visible=previousCursorVisible;
        }
        void BuildUI()
        {
            if(root!=null)return;
            var assets=Resources.Load<SettingsMenuAssets>("SettingsMenuAssets");
            var style=Resources.Load<StyleSheet>("DeveloperConsole");
            if(!assets || !style){Debug.LogError("Developer console UI assets are missing.");return;}
            panel=ScriptableObject.CreateInstance<PanelSettings>();panel.hideFlags=HideFlags.HideAndDontSave;
            panel.themeStyleSheet=assets.theme;panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution=new Vector2Int(1280,800);panel.match=1;
            var host=new GameObject("Console UI");host.transform.SetParent(transform,false);
            var document=host.AddComponent<UIDocument>();document.panelSettings=panel;document.sortingOrder=3000;
            root=document.rootVisualElement;root.style.display=DisplayStyle.None;root.style.flexGrow=1;
            root.style.unityFontDefinition=FontDefinition.FromFont(assets.font);root.styleSheets.Add(style);
            var scrim=new VisualElement();scrim.AddToClassList("console-scrim");root.Add(scrim);
            var window=new VisualElement();window.name="console-window";window.AddToClassList("console-window");scrim.Add(window);
            var title=new VisualElement();title.AddToClassList("console-titlebar");window.Add(title);
            title.Add(new Label("Console"));var close=new Button(Close){text="X",name="console-close"};title.Add(close);
            output=new ScrollView(ScrollViewMode.Vertical){name="console-output"};output.AddToClassList("console-output");window.Add(output);
            var row=new VisualElement();row.AddToClassList("console-command-row");window.Add(row);
            row.Add(new Label(">"));input=new TextField{name="console-input",maxLength=256};row.Add(input);
            input.RegisterCallback<KeyDownEvent>(OnInputKey,TrickleDown.TrickleDown);
            input.RegisterCallback<PointerDownEvent>(evt=>ResetCaret(),TrickleDown.TrickleDown);
            input.RegisterCallback<FocusInEvent>(evt=>ResetCaret());
            input.RegisterValueChangedCallback(evt=>ResetCaret());
            row.Add(new Button(Submit){text="Submit",name="console-submit"});
            var footer=new Label("F3 + F4: toggle    ESC: close    UP/DOWN: history    TAB: complete");footer.AddToClassList("console-footer");window.Add(footer);
            Write("PALE HORSE developer console");Write("Type help for commands.");
        }
        void Write(string text)
        {
            lines.Add(text);if(lines.Count>256)lines.RemoveAt(0);
            if(output==null)return;
            var label=new Label(text){enableRichText=false};label.AddToClassList("console-line");output.Add(label);
            if(output.contentContainer.childCount>256)output.contentContainer.RemoveAt(0);
            output.schedule.Execute(()=>{if(label.parent==output.contentContainer)output.ScrollTo(label);});
        }
        void Submit()
        {
            string text=input.value;input.SetValueWithoutNotify("");Execute(text);if(IsOpen)input.Focus();
        }
        void OnInputKey(KeyDownEvent evt)
        {
            ResetCaret();
            if(evt.keyCode==KeyCode.Return || evt.keyCode==KeyCode.KeypadEnter)Submit();
            else if(evt.keyCode==KeyCode.Escape)Close();
            else if(evt.keyCode==KeyCode.UpArrow || evt.keyCode==KeyCode.DownArrow)
            {
                if(historyIndex==history.Count)draft=input.value;
                historyIndex=Mathf.Clamp(historyIndex+(evt.keyCode==KeyCode.UpArrow?-1:1),0,history.Count);
                input.SetValueWithoutNotify(historyIndex==history.Count?draft:history[historyIndex]);
            }
            else if(evt.keyCode==KeyCode.Tab)Complete();
            else return;
            evt.StopImmediatePropagation();evt.PreventDefault();
        }
        void ResetCaret()
        {
            caretResetTime=Time.unscaledTime;
            input?.RemoveFromClassList("console-caret-off");
        }
        void Complete()
        {
            string text=input.value;string[] tokens=text.Split(' ');
            IEnumerable<string> candidates=commands;string prefix=text;
            if(tokens.Length==2 && (tokens[0].Equals("spawn",StringComparison.OrdinalIgnoreCase)||tokens[0].Equals("deliver",StringComparison.OrdinalIgnoreCase)))
            {candidates=tokens[0].Equals("spawn",StringComparison.OrdinalIgnoreCase)?GetParts().Keys.Concat(new[]{"zombie"}):GetParts().Keys;prefix=tokens[1];}
            if(tokens.Length==2 && tokens[0].Equals("weather",StringComparison.OrdinalIgnoreCase))
            {candidates=weatherNames;prefix=tokens[1];}
            var matches=candidates.Where(c=>c.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length==1)
            {
                string completed=(tokens.Length==2?tokens[0]+" ":"")+matches[0]+" ";
                input.SetValueWithoutNotify(completed);
                input.SelectRange(completed.Length,completed.Length);
            }
            else if(matches.Length>1)Write(string.Join("   ",matches));
        }
        static Dictionary<string,GameObject> GetParts()
        {
            var result=new Dictionary<string,GameObject>(StringComparer.OrdinalIgnoreCase);
            var delivery=FindFirstObjectByType<RepairRobotDropper>();
            if(delivery)
                foreach(var prefab in delivery.AllParts)
                {
                    string name=prefab.name.ToLowerInvariant();
                    string id=name.Contains("wheel")?"wheel":name.Contains("battery")?"battery":name.Contains("headlight")?"headlights":name.Contains("window")?"window":name.Contains("body")?"body":name;
                    var variant=prefab.GetComponent<RepairPartVariant>();
                    if(variant)id=variant.robotName.ToLowerInvariant()+"_"+id;
                    result[id]=prefab;
                }
            return result;
        }
        public void Execute(string text)
        {
            if(string.IsNullOrWhiteSpace(text))return;
            text=text.Trim();Write("] "+text);
            if(history.Count==0 || history[history.Count-1]!=text){history.Add(text);if(history.Count>64)history.RemoveAt(0);}
            historyIndex=history.Count;draft="";
            var args=text.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);string command=args[0].ToLowerInvariant();
            if(command=="help" && args.Length==1)
            {
                Write("help                      List all commands");
                Write("balance                   Show current credits");
                Write("credits <amount>          Add a positive number of credits");
                Write("parts                     List repair part IDs");
                Write("spawn <part> [count]      Spawn near the human operator (1-20)");
                Write("spawn zombie              Spawn a zombie near the human operator");
                Write("deliver <part> [count]    Send through Tube_For_Scrap (1-20)");
                Write("weather                   Show current weather and available presets");
                Write("weather <preset>          Set sunny, fog, radiation, meteors, night, cryogenic or ash");
                Write("repair                    Fully repair the robot in the repair bay");
                Write("end                       Skip to and accept seventh story order AURINKO");
                Write("dismantle                 Instantly dismantle final AURINKO; then call the crane");
                Write("pistol                    Add a pistol to the operator's toolbox (8 rounds, R reload)");
                Write("speed [x|reset] [bot|player] Set speed multiplier (0.1-10); default: bot");
                Write("clear                     Clear console output");Write("close                     Close console");return;
            }
            if(command=="clear" && args.Length==1){lines.Clear();output?.Clear();return;}
            if(command=="close" && args.Length==1){Close();return;}
            if(command=="parts" && args.Length==1){Write("Parts: "+string.Join(", ",GetParts().Keys));return;}
            if(command=="pistol")
            {
                if(args.Length!=1){Write("Usage: pistol");return;}
                var player=FindFirstObjectByType<PlayerBody>();
                if(!player){Write("No human operator in this scene.");return;}
                Write(player.GrantPistol()?"Pistol available in toolbox. Hold MMB to select; LMB fire, RMB aim, R reload.":"Pistol assets are not ready.");return;
            }
            if(command=="end")
            {
                if(args.Length!=1){Write("Usage: end");return;}
                var messages=FindFirstObjectByType<RepairMessagesApp>();
                if(!messages){Write("No repair inbox in this scene. Use end in Operator.");return;}
                bool started=messages.DebugStartFinalOrder(out string result);Write(result);
                if(started)Close();return;
            }
            if(command=="dismantle")
            {
                if(args.Length!=1){Write("Usage: dismantle");return;}
                var robot=RepairRobotDropper.Active?RepairRobotDropper.Active.CurrentRobot:null;
                var final=robot?robot.GetComponent<AurinkoDismantling>():null;
                if(!final){Write("No final AURINKO in the bay. Accept the seventh story order first.");return;}
                if(robot.IsBeingCollected){Write("The crane is already collecting this robot.");return;}
                if(final.Complete){Write("AURINKO is already dismantled. Call the crane.");return;}
                if(!final.DebugDismantleAll()){Write("Wait for AURINKO to land and finish preparing.");return;}
                Write("AURINKO dismantled. Call the crane at the red button.");Close();return;
            }
            if(command=="repair")
            {
                if(args.Length!=1){Write("Usage: repair");return;}
                var robot=RepairRobotDropper.Active?RepairRobotDropper.Active.CurrentRobot:null;
                if(!robot){Write("No repair robot in the bay. Accept a repair order first.");return;}
                if(robot.IsBeingCollected){Write("The crane is already collecting this robot.");return;}
                if(!robot.IsSettled){Write("Wait for the repair robot to land.");return;}
                Write(robot.DebugRepairAll()?"Robot fully repaired: all parts seated, bolts and screws complete.":"Robot could not be repaired.");return;
            }
            if(command=="speed")
            {
                string actor=args.Length==3?args[2].ToLowerInvariant():"bot";
                if(args.Length>3 || (actor!="bot" && actor!="player")){Write("Usage: speed <0.1-10|reset> [bot|player]");return;}
                var link=actor=="bot"?FindFirstObjectByType<ScavengerLink>():null;
                var drive=link?link.Drive:null;var player=actor=="player"?FindFirstObjectByType<FirstPersonController>():null;
                if((actor=="bot" && !drive) || (actor=="player" && !player)){Write("Speed target not available: "+actor);return;}
                if(args.Length==1){Write("Bot speed multiplier: x"+drive.DebugSpeedMultiplier.ToString("0.##",CultureInfo.InvariantCulture));return;}
                float multiplier=1;
                if(!args[1].Equals("reset",StringComparison.OrdinalIgnoreCase) &&
                    (!float.TryParse(args[1],NumberStyles.Float,CultureInfo.InvariantCulture,out multiplier) || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier<.1f || multiplier>10))
                {Write("Usage: speed <0.1-10|reset> [bot|player]");return;}
                if(drive)drive.DebugSpeedMultiplier=multiplier;else player.DebugSpeedMultiplier=multiplier;
                Write((actor=="bot"?"SCAV_BOT":"Player")+" speed multiplier: x"+multiplier.ToString("0.##",CultureInfo.InvariantCulture));return;
            }
            if(command=="weather")
            {
                var weather=MoonWeatherSystem.Active;
                if(!weather)weather=FindFirstObjectByType<MoonWeatherSystem>();
                if(!weather){Write("No weather system available in this scene.");return;}
                if(args.Length==1)
                {
                    Write("Weather: "+MoonWeatherSystem.Name(weather.Current)+" ("+Mathf.CeilToInt(weather.Remaining)+"s remaining)");
                    Write("Presets: "+string.Join(", ",weatherNames));
                    Write("Example: weather fog. Close console to resume simulation.");return;
                }
                WeatherKind kind;
                string preset=args.Length==2?args[1].ToLowerInvariant():"";
                switch(preset)
                {
                    case "sunny": kind=WeatherKind.Sunny;break;
                    case "fog": case "dust": case "moondust": kind=WeatherKind.MoonDust;break;
                    case "radiation": kind=WeatherKind.Radiation;break;
                    case "meteors": case "meteorite": case "meteoriterain": kind=WeatherKind.MeteoriteRain;break;
                    case "night": case "deepnight": kind=WeatherKind.DeepNight;break;
                    case "cryogenic": case "cold": case "snow": case "cryo": kind=WeatherKind.CryogenicFront;break;
                    case "ash": case "ashencalm": case "wind": kind=WeatherKind.AshenCalm;break;
                    default: Write("Usage: weather <sunny|fog|radiation|meteors|night|cryogenic|ash>");return;
                }
                weather.SetWeather(kind);
                Write("Weather set: "+MoonWeatherSystem.Name(kind)+". Close console to resume simulation.");
                if(kind==WeatherKind.MeteoriteRain)Write("Meteors spawn outside shelters; impact markers warn at least 5 seconds before impact.");
                return;
            }
            if(command=="balance" || command=="credits")
            {
                var terminal=FindFirstObjectByType<PcMonitorTerminal>();if(!terminal){Write("No PC balance available in this scene.");return;}
                if(args.Length==1){Write("Credits: "+terminal.Credits);return;}
                if(command!="credits" || args.Length!=2 || !int.TryParse(args[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out int amount) || amount<=0)
                {Write("Usage: credits <positive integer>");return;}
                long total=(long)terminal.Credits+amount;
                if(total>int.MaxValue){Write("Credit limit exceeded.");return;}
                terminal.SetCredits((int)total);Write("Added "+amount+" credits. Balance: "+terminal.Credits);return;
            }
            if(command=="spawn" && args.Length>=2 && args[1].Equals("zombie",StringComparison.OrdinalIgnoreCase))
            {
                if(args.Length!=2){Write("Usage: spawn zombie");return;}
                Write(ZombieSpawner.SpawnNearOperator()?"Zombie spawned. Close the console to resume simulation.":"No clear floor near the operator, or Zombie prefab unavailable.");return;
            }
            if(command=="spawn" || command=="deliver")
            {
                if(args.Length<2 || args.Length>3){Write("Usage: "+command+" <part> [count]");return;}
                int count=1;
                if(args.Length==3 && (!int.TryParse(args[2],out count)||count<1||count>20)){Write("Count must be 1-20.");return;}
                var parts=GetParts();
                if(!parts.TryGetValue(args[1],out var prefab))prefab=parts.Values.FirstOrDefault(p=>p.name.Equals(args[1],StringComparison.OrdinalIgnoreCase));
                if(!prefab){Write("Unknown part. Type parts for IDs.");return;}
                int created=0;
                var bay=RepairBay.Active;
                for(int i=0;i<count;i++)
                {
                    if(command=="deliver" ? bay && bay.Deliver(prefab) : DeveloperPartSpawner.Spawn(prefab))created++;
                    else break;
                }
                Write((command=="deliver"?"Queued through pipe: ":"Spawned near operator: ")+created+" / "+count+" "+args[1]);
                if(created<count)Write(command=="deliver"?"Repair pipe unavailable.":"No clear space near the operator.");
                return;
            }
            Write("Unknown command or invalid arguments. Type help.");
        }
        void OnDisable(){if(IsOpen)Close();}
        void OnDestroy(){if(panel)Destroy(panel);}
    }
}
