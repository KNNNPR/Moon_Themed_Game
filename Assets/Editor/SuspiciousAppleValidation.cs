using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Interactions;
using MoonThemedGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class SuspiciousAppleValidation
    {
        const string Output="Logs/SuspiciousApples";
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static async void Run()
        {
            Directory.CreateDirectory(Output);var report=new StringBuilder();bool passed=false;
            bool oldOptions=EditorSettings.enterPlayModeOptionsEnabled;var oldPlayOptions=EditorSettings.enterPlayModeOptions;
            int quality=QualitySettings.GetQualityLevel();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");AppleStationSetup.Install();AppleStationSetup.Install();
                var station=Object.FindFirstObjectByType<AppleStation>();Require(station,"Apple Station was not installed.");
                Require(Object.FindObjectsByType<AppleStation>(FindObjectsSortMode.None).Length==1,"Duplicate station.");
                Require(station.assets.collisionMesh.isReadable,"Collision mesh cannot be cooked in a build.");
                Require(station.GetComponents<Collider>().Count(c=>c.enabled && !c.isTrigger)==1,"Tray is sealed by a duplicate collider.");
                Require(station.assets.applePrefab.GetComponent<EdibleProp>().intoxicationDose==1,"Wrong suspicious prefab dose.");
                var normal=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Apple.prefab");
                Require(normal.GetComponent<EdibleProp>().intoxicationDose==0,"Normal apple became intoxicating.");
                foreach(var path in VisualStyleSetup.RendererPaths)
                    Require(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path).rendererFeatures.OfType<OperatorIntoxicationFeature>().Count()==1,"Duplicate or missing intoxication pass.");
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for(int i=0;i<150 && !EditorApplication.isPlaying;i++)await Task.Delay(100);
                Require(EditorApplication.isPlaying,"Play mode did not start.");await Task.Delay(400);
                station=Object.FindFirstObjectByType<AppleStation>();Physics.SyncTransforms();
                Require(station.TrayIsClear,"Authored drop position is blocked.");
                Require(station.TryDispense(out var apple),"Station did not dispense.");
                Require(apple.displayName=="Suspicious Apple" && apple.GetComponent<EdibleProp>().intoxicationDose==1,"Wrong dispensed fruit.");
                Require(!station.TryDispense(out _),"Repeated E bypasses the cooldown.");
                Vector3 released=apple.GetComponent<Renderer>().bounds.center;
                await Task.Delay(1400);
                var settled=apple.GetComponent<Renderer>().bounds;
                Require(settled.center.y<released.y-0.04f,"Apple did not drop.");
                var below=new Ray(settled.center+Vector3.up*.02f,Vector3.down);
                Require(station.GetComponent<MeshCollider>().Raycast(below,out var trayHit,0.6f),"Apple missed the station tray.");
                Require(apple.GetComponent<Rigidbody>().linearVelocity.magnitude<0.3f,"Apple did not settle in the tray.");
                Require(!apple.GetComponent<Rigidbody>().useGravity,"Space gravity behavior changed.");
                report.AppendLine("Station: one dispenser; readable mesh collider; clear spawn; cooldown; fruit falls and settles on the physical tray.");
                report.AppendLine($"Tray capture: start={released:F3}; final={settled.center:F3}; floor={trayHit.point:F3}.");

                var camera=Camera.main;Require(camera,"No operator camera.");
                var player=camera.GetComponentInParent<FirstPersonController>();Require(player,"Main camera is not the human view.");
                player.transform.position=new Vector3(1000,1000,1000);
                Cursor.lockState=CursorLockMode.None;
                apple.Interact();Require(apple.IsHeld,"Cannot pick up the dispensed apple.");
                var edible=apple.GetComponent<EdibleProp>();Require(edible.TryEat(),"Cannot start eating.");
                await Task.Delay(100);apple.Drop(false);await Task.Delay(100);
                Require(!edible.IsConsumed && !player.GetComponent<OperatorIntoxication>(),"A cancelled bite applied intoxication.");
                apple.Interact();Require(edible.TryEat(),"Cannot eat after cancelling.");await Task.Delay(800);
                var state=player.GetComponent<OperatorIntoxication>();Require(state && state.Dose==1 && !HeldProp.Holding,"Successful eating did not add exactly one dose or release the hand.");
                var ordinary=Object.Instantiate(normal).GetComponent<HeldProp>();ordinary.Interact();
                Require(ordinary.GetComponent<EdibleProp>().TryEat(),"Normal apple is no longer edible.");await Task.Delay(800);
                Require(state.Dose==1,"Normal apple added intoxication.");
                Time.timeScale=0;float phase=state.Phase;await Task.Delay(150);
                Require(state.Phase==phase,"Pause advances intoxication.");
                state.Advance(1);float single=state.Strength;
                state.AddDose(2);state.Advance(1);float triple=state.Strength;
                Require(triple>single && single>0,"Consumption does not stack.");
                state.AddDose(100);state.Advance(1);Require(state.Dose==state.maximumDose && state.Strength==1,"Dose cap failed.");
                var eyePosition=camera.transform.localPosition;var eyeRotation=camera.transform.localRotation;
                state.Advance(7);state.Advance(0.5f);Require(state.Strength>0.9f && state.Strength<1,"Recovery snapped instead of fading.");
                for(int i=0;i<9000;i++)state.Advance(1f/60);
                Require(state.Dose==0 && state.Strength==0,"Intoxication did not completely recover.");
                Require(camera.transform.localPosition==eyePosition && camera.transform.localRotation==eyeRotation,"Effect changed look or crouch transforms.");
                report.AppendLine($"Eating: cancelled bite adds nothing; normal apple stays sober; doses stack {single:F3} -> {triple:F3} -> 1; cap, pause, smooth recovery to zero; hand clears correctly.");

                ShaderUtil.allowAsyncCompilation=false;
                camera.enabled=false;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.transform.SetPositionAndRotation(new Vector3(1000,1000,1000),Quaternion.identity);
                var card=GameObject.CreatePrimitive(PrimitiveType.Quad);Object.Destroy(card.GetComponent<Collider>());card.layer=30;
                card.transform.SetPositionAndRotation(camera.transform.position+Vector3.forward*3,Quaternion.identity);card.transform.localScale=new Vector3(7,4,1);
                var texture=new Texture2D(128,128,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
                var colors=new[]{new Color(.7f,.16f,.12f),new Color(.12f,.55f,.35f),new Color(.15f,.3f,.8f),Color.white};
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)texture.SetPixel(x,y,((x+y)%13<2)?Color.black:colors[(x/8+y/8)%4]);
                texture.Apply();var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetTexture("_BaseMap",texture);card.GetComponent<Renderer>().sharedMaterial=material;
                var other=new GameObject("Non-operator view").AddComponent<Camera>();other.transform.SetParent(player.transform);other.CopyFrom(camera);other.enabled=false;
                other.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
                var styles=Resources.Load<VisualStyleSettings>("VisualStyleSettings");
                foreach(int level in new[]{1,0})
                {
                    QualitySettings.SetQualityLevel(level,true);await Task.Delay(350);
                    foreach(var path in VisualStyleSetup.RendererPaths)
                        foreach(var feature in AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path).rendererFeatures)
                            if(feature.name.Contains("Weather"))feature.SetActive(false);
                    foreach(var style in new[]{VisualStyle.Retro,VisualStyle.ColorAscii})
                    {
                        styles.SetStyle(style);string prefix=(level==1?"pc-":"mobile-")+style;
                        for(int i=0;i<9000;i++)state.Advance(1f/60);
                        var sober=Capture(camera,prefix+"-sober");
                        var otherSober=Capture(other,prefix+"-other-sober");
                        state.AddDose(1);state.Advance(1);SetPhase(state,2);var one=Capture(camera,prefix+"-one");
                        state.AddDose(2);state.Advance(1);SetPhase(state,2);var three=Capture(camera,prefix+"-three");
                        Require(Difference(sober,one)>2 && Difference(sober,three)>Difference(sober,one),prefix+" effect missing or not increasing.");
                        Require(!state.Affects(other),"Effect leaks to another camera under the player.");
                        var unaffected=Capture(other,prefix+"-other-view");Require(Difference(otherSober,unaffected)==0,prefix+" affected a different view.");
                        for(int i=0;i<9000;i++)state.Advance(1f/60);
                        var recovered=Capture(camera,prefix+"-recovered");Require(Difference(sober,recovered)==0,prefix+" left a residual effect.");
                        report.AppendLine($"GPU {prefix}: one dose delta={Difference(sober,one):F2}; three={Difference(sober,three):F2}; other camera and recovered image match sober.");
                    }
                }
                var shader=Shader.Find("Moon Game/Operator Intoxication");Require(shader && !ShaderUtil.ShaderHasError(shader),"Intoxication shader failed compilation.");
                report.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceName}; PC deferred + Mobile forward, Retro + ASCII.");passed=true;
            }
            catch(Exception e){report.AppendLine(e.ToString());Debug.LogException(e);}
            finally
            {
                Time.timeScale=1;QualitySettings.SetQualityLevel(quality,true);
                if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
                for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);
                EditorSettings.enterPlayModeOptionsEnabled=oldOptions;EditorSettings.enterPlayModeOptions=oldPlayOptions;
                File.WriteAllText(Output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }
        static void SetPhase(OperatorIntoxication state,float phase)=>typeof(OperatorIntoxication).GetField("<Phase>k__BackingField",Private).SetValue(state,phase);
        static Color32[] Capture(Camera camera,string name)
        {
            var rt=RenderTexture.GetTemporary(640,360,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
            var texture=new Texture2D(640,360,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.aspect=640f/360;camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,640,360),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());return texture.GetPixels32();
            }
            finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(texture);}
        }
        static double Difference(Color32[] a,Color32[] b)=>a.Select((p,i)=>(Math.Abs(p.r-b[i].r)+Math.Abs(p.g-b[i].g)+Math.Abs(p.b-b[i].b))/3.0).Average();
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
