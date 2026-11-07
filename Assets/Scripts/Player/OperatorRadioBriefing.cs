using System.Collections;
using System.Collections.Generic;
using MoonThemedGame.Computer;
using MoonThemedGame.Repair;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor=UnityEngine.Cursor;

namespace MoonThemedGame.Interactions
{
    [DefaultExecutionOrder(11000),DisallowMultipleComponent]
    public sealed class OperatorRadioBriefing : MonoBehaviour
    {
        public const string SeenKey="palehorse.operator.radio-intro.seen.v1";
        public OperatorTutorialContent content;
        public Transform radio;
        [Min(.1f)] public float turnDuration=1.4f;
        public static OperatorRadioBriefing Active {get;private set;}
        public static bool IsPlaying=>Active&&(Active.waiting||Active.playing);
        public static bool HasSeen=>PlayerPrefs.GetInt(SeenKey,0)==1;
        public static bool ShouldPlay=>!HasSeen&&StationRun.Mode==StationGameMode.Story;
        public bool Speaking=>playing;
        public string CurrentSubtitle=>subtitle!=null?subtitle.text:"";
        FirstPersonController player;
        FirstPersonCameraEffects effects;
        PlayerBody body;
        Camera view;
        bool waiting,playing,restoreMovement,restoreEffects,restoreBody,ownsContent;
        float elapsed;
        Quaternion initialRotation,localRotation;
        Vector3 localPosition;
        readonly Dictionary<Renderer,bool> renderers=new Dictionary<Renderer,bool>();
        PanelSettings panel;
        GameObject overlay;
        Label subtitle,skip;
        VisualElement card;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){Active=null;SceneManager.sceneLoaded-=Loaded;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;Ensure(SceneManager.GetActiveScene());}
        static void Loaded(Scene scene,LoadSceneMode mode)=>Ensure(scene);
        static void Ensure(Scene scene)
        {
            if(scene.name=="Operator"&&ShouldPlay&&!FindFirstObjectByType<OperatorRadioBriefing>())
                new GameObject("Operator / First morning radio briefing").AddComponent<OperatorRadioBriefing>();
        }
        IEnumerator Start()
        {
            if(!ShouldPlay)yield break;
            Active=this;waiting=true;
            yield return null;
            if(!OperatorWakeUp.IsPlaying)
            {
                var wake=FindFirstObjectByType<OperatorWakeUp>();
                if(!wake)wake=new GameObject("Operator / Wake Up From Bed").AddComponent<OperatorWakeUp>();
                wake.Begin();
            }
            while(OperatorWakeUp.IsPlaying)yield return null;
            if(ShouldPlay)Begin();else{waiting=false;if(Active==this)Active=null;}
        }
        public bool Begin()
        {
            if(playing||!Application.isPlaying)return false;
            player=FindFirstObjectByType<FirstPersonController>();view=player?player.GetComponentInChildren<Camera>():null;
            if(!player||!view){waiting=false;if(Active==this)Active=null;return false;}
            if(!content)content=Resources.Load<OperatorTutorialContent>("OperatorTutorialContent");
            if(!content){content=ScriptableObject.CreateInstance<OperatorTutorialContent>();ownsContent=true;}
            if(!radio)
                foreach(var prop in FindObjectsByType<HeldProp>(FindObjectsSortMode.None))
                    if(prop.gameObject.scene==gameObject.scene&&prop.IsRadio){radio=prop.transform;break;}
            Active=this;waiting=false;playing=true;elapsed=0;
            initialRotation=view.transform.rotation;localPosition=view.transform.localPosition;localRotation=view.transform.localRotation;
            restoreMovement=player.enabled;player.enabled=false;
            effects=view.GetComponentInParent<FirstPersonCameraEffects>();restoreEffects=effects&&effects.enabled;if(effects)effects.enabled=false;
            body=player.GetComponent<PlayerBody>();restoreBody=body&&body.enabled;if(body)body.enabled=false;
            HideBody();BuildOverlay();Cursor.lockState=CursorLockMode.None;Cursor.visible=false;RefreshSubtitle();return true;
        }
        Vector3 RadioPoint
        {
            get
            {
                if(radio&&radio.TryGetComponent<Renderer>(out var renderer))return renderer.bounds.center;
                return radio?radio.position:view.transform.position+initialRotation*Vector3.forward;
            }
        }
        void HideBody()
        {
            foreach(var renderer in player.GetComponentsInChildren<Renderer>(true))
            {if(!renderers.ContainsKey(renderer))renderers.Add(renderer,renderer.enabled);renderer.enabled=false;}
        }
        void BuildOverlay()
        {
            var assets=Resources.Load<SettingsMenuAssets>("SettingsMenuAssets");
            panel=ScriptableObject.CreateInstance<PanelSettings>();panel.hideFlags=HideFlags.DontSave;
            if(assets)panel.themeStyleSheet=assets.theme;
            panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1280,720);panel.match=1;
            overlay=new GameObject("Radio briefing / subtitles");overlay.transform.SetParent(transform,false);
            var document=overlay.AddComponent<UIDocument>();document.panelSettings=panel;document.sortingOrder=2050;
            var root=document.rootVisualElement;root.pickingMode=PickingMode.Ignore;root.style.position=Position.Absolute;
            root.style.left=root.style.right=root.style.top=root.style.bottom=0;
            card=new VisualElement{pickingMode=PickingMode.Ignore};root.Add(card);card.style.position=Position.Absolute;
            card.style.left=Length.Percent(12);card.style.right=Length.Percent(12);card.style.bottom=58;
            card.style.paddingLeft=card.style.paddingRight=14;card.style.paddingTop=card.style.paddingBottom=9;
            card.style.backgroundColor=new Color(0,0,0,.66f);
            subtitle=new Label{pickingMode=PickingMode.Ignore,name="radio-subtitle"};card.Add(subtitle);
            subtitle.style.fontSize=22;subtitle.style.color=new Color(.96f,.95f,.88f);subtitle.style.whiteSpace=WhiteSpace.Normal;
            subtitle.style.unityTextAlign=TextAnchor.MiddleCenter;
            skip=new Label{pickingMode=PickingMode.Ignore,name="radio-skip"};root.Add(skip);skip.style.position=Position.Absolute;
            skip.style.left=0;skip.style.right=0;skip.style.bottom=24;skip.style.fontSize=13;
            skip.style.unityTextAlign=TextAnchor.MiddleCenter;skip.style.color=new Color(.7f,.72f,.65f);
            if(assets&&assets.font){subtitle.style.unityFontDefinition=FontDefinition.FromFont(assets.font);skip.style.unityFontDefinition=FontDefinition.FromFont(assets.font);}
        }
        void Update()
        {
            if(!playing||DeveloperConsole.IsOpen)return;
            var keys=Keyboard.current;
            if(keys!=null&&(keys.spaceKey.wasPressedThisFrame||keys.escapeKey.wasPressedThisFrame)){Finish();return;}
            elapsed+=Time.deltaTime;RefreshSubtitle();
            if(elapsed>=TotalDuration)Finish();
        }
        float TotalDuration
        {
            get{float duration=turnDuration+.5f;if(content&&content.radioLines!=null)foreach(var line in content.radioLines)if(line!=null)duration+=Mathf.Max(1,line.seconds);return duration;}
        }
        void RefreshSubtitle()
        {
            if(subtitle==null)return;
            skip.text=Loc.Russian?"[ПРОБЕЛ / ESC] Пропустить":"[SPACE / ESC] Skip";
            float t=elapsed-turnDuration;subtitle.text="";card.style.opacity=0;
            if(t<0||!content||content.radioLines==null)return;
            foreach(var line in content.radioLines)
            {
                if(line==null)continue;float duration=Mathf.Max(1,line.seconds);
                if(t<duration){subtitle.text=line.Text;card.style.opacity=Mathf.Clamp01(Mathf.Min(t/.2f,(duration-t)/.3f));return;}
                t-=duration;
            }
        }
        void LateUpdate()
        {
            if(!playing||!view)return;
            HideBody();var direction=RadioPoint-view.transform.position;
            if(direction.sqrMagnitude>.001f)
                view.transform.rotation=Quaternion.Slerp(initialRotation,Quaternion.LookRotation(direction,Vector3.up),Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/turnDuration)));
        }
        public void Finish()=>Stop(true);
        void Stop(bool markSeen)
        {
            waiting=false;
            if(playing)
            {
                playing=false;
                var rotation=view?view.transform.rotation:initialRotation;
                if(view){view.transform.localPosition=localPosition;view.transform.localRotation=localRotation;}
                if(player)
                {
                    var angles=rotation.eulerAngles;player.SetViewPose(player.transform.position,angles.y,Mathf.DeltaAngle(0,angles.x));
                    foreach(var entry in renderers)if(entry.Key)entry.Key.enabled=entry.Value;
                    if(body)body.enabled=restoreBody;if(effects)effects.enabled=restoreEffects;player.enabled=restoreMovement;
                }
                if(markSeen){PlayerPrefs.SetInt(SeenKey,1);PlayerPrefs.Save();}
                if(!DeveloperConsole.IsOpen){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
            }
            renderers.Clear();if(overlay){overlay.SetActive(false);Destroy(overlay);}if(panel)Destroy(panel);
            overlay=null;panel=null;card=null;subtitle=null;skip=null;if(Active==this)Active=null;
        }
        [ContextMenu("Replay radio intro (Play Mode)")]
        public void Replay(){if(Application.isPlaying&&!IsPlaying)Begin();}
        void OnDisable()=>Stop(false);
        void OnDestroy(){if(Active==this)Active=null;if(ownsContent&&content)Destroy(content);}
    }
}
