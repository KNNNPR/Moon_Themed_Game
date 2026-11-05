using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class AppleAndHintsValidation
    {
        const string Output="Logs/AppleAndHints";
        public static async void Run()
        {
            Directory.CreateDirectory(Output);var report=new StringBuilder();bool passed=false;
            bool oldOptions=EditorSettings.enterPlayModeOptionsEnabled;var oldPlayOptions=EditorSettings.enterPlayModeOptions;
            var language=typeof(Loc).GetField("current",BindingFlags.NonPublic|BindingFlags.Static);object oldLanguage=language.GetValue(null);
            GameObject stage=null;Camera originalCamera=null;
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Apple.prefab");
                Require(prefab && prefab.GetComponent<EdibleProp>() && prefab.GetComponent<HeldProp>(),"Apple prefab does not contain its edible behavior.");
                Require(!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Radio.prefab").GetComponent<EdibleProp>(),"Radio became edible.");
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();for(int i=0;i<120 && !EditorApplication.isPlaying;i++)await Task.Delay(100);
                Require(EditorApplication.isPlaying,"Play mode did not start.");await Task.Delay(500);
                language.SetValue(null,GameLanguage.English);
                Require(Object.FindObjectsByType<EdibleProp>(FindObjectsSortMode.None).Length>0,"Authored Apple prefab instance did not inherit edible behavior.");
                originalCamera=Camera.main;originalCamera.tag="Untagged";originalCamera.enabled=false;
                var controller=Object.FindFirstObjectByType<FirstPersonController>();controller.enabled=false;
                stage=new GameObject("Apple validation fixtures");
                var host=new GameObject("Validation camera");host.transform.parent=stage.transform;
                var camera=host.AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(1000,1000,1000);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.045f,0.055f,0.052f);
                camera.cullingMask=1<<28;camera.nearClipPlane=0.03f;camera.fieldOfView=65;
                var light=host.AddComponent<Light>();light.type=LightType.Point;light.intensity=4;light.range=10;
                Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
                var apple=Object.Instantiate(prefab,stage.transform);apple.transform.position=camera.transform.position+Vector3.forward*1.4f;apple.layer=28;
                var prop=apple.GetComponent<HeldProp>();var edible=apple.GetComponent<EdibleProp>();
                Require(!edible.TryEat(),"Apple can be eaten without picking it up.");
                prop.Interact();Require(prop.IsHeld && HeldProp.Holding,"Apple pickup failed.");
                Require(edible.TryEat() && !edible.TryEat(),$"Eat action is missing or can start twice: held={prop.IsHeld}, active={edible.isActiveAndEnabled}, camera={prop.HoldingCamera}, blocked={GameSettingsMenu.BlockGameplayInput}.");
                var menu=Object.FindFirstObjectByType<GameSettingsMenu>();menu.Open();
                var pausedScale=apple.transform.localScale;await Task.Delay(250);
                Require(apple && edible.IsEating && apple.transform.localScale==pausedScale,"Pause consumed or animated the apple.");
                menu.Close();await Task.Delay(800);
                Require(!apple && !HeldProp.Holding,"Eating did not destroy the apple or release the hand.");
                apple=Object.Instantiate(prefab,stage.transform);apple.transform.position=camera.transform.position+Vector3.forward;apple.layer=28;
                prop=apple.GetComponent<HeldProp>();edible=apple.GetComponent<EdibleProp>();var originalScale=apple.transform.localScale;
                Cursor.lockState=CursorLockMode.Locked;prop.Interact();Require(edible.TryEat(),"Second apple cannot be eaten.");await Task.Delay(100);
                prop.Drop();await Task.Delay(100);
                Require(apple && !edible.IsEating && Vector3.Distance(originalScale,apple.transform.localScale)<0.001f && !HeldProp.Holding,"Throwing during eating did not cancel cleanly.");
                Require(!prop.GetComponent<Rigidbody>().useGravity && !prop.GetComponent<Rigidbody>().isKinematic,"Apple throw lost its space physics.");
                Object.DestroyImmediate(apple);
                var radio=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Radio.prefab"),stage.transform);
                radio.transform.position=camera.transform.position+Vector3.forward;radio.layer=28;
                var radioProp=radio.GetComponent<HeldProp>();radioProp.Interact();Require(radioProp.IsHeld,"Hand remained blocked after eating.");radioProp.Drop(false);
                report.AppendLine("Apple prefab and scene instance: pickup, single consumption, pause, canceled bite/throw, restored scale and free hand passed. Radio remains a normal spatial prop.");

                var keyField=typeof(GameControls).GetField("keys",BindingFlags.NonPublic|BindingFlags.Static);
                GameControls.Binding(GameCommand.Interact);var originalKeys=keyField.GetValue(null);
                try
                {
                    var custom=(Key[])((Key[])originalKeys).Clone();custom[(int)GameCommand.Interact]=Key.J;custom[(int)GameCommand.Throw]=Key.K;keyField.SetValue(null,custom);
                    Require(GameControls.FormatHint("E — Open")=="[J] Open" && GameControls.FormatHint("G — Drop")=="[K] Drop","Hint did not follow rebindings.");
                    Require(GameControls.FormatHint("[E] Play")=="[E] Play","Already formatted keys were rebound twice.");
                }
                finally{keyField.SetValue(null,originalKeys);}
                foreach(var item in Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None))
                    foreach(var lang in new[]{GameLanguage.English,GameLanguage.Russian})
                    {
                        language.SetValue(null,lang);string prompt=GameControls.FormatHint(Loc.T(item.Prompt));
                        Require(!prompt.Contains('—') && !prompt.Contains('–'),"Long dash remained in a hover prompt: "+item.name);
                    }
                report.AppendLine("English/Russian hover prompts and custom bindings use bracketed controls without long dashes.");

                passed=true;
            }
            catch(Exception exception){report.AppendLine(exception.ToString());Debug.LogException(exception);}
            finally
            {
                Time.timeScale=1;FirstPersonController.LookFrozen=false;
                if(stage)Object.DestroyImmediate(stage);
                if(originalCamera){originalCamera.tag="MainCamera";originalCamera.enabled=true;}
                if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);}
                language.SetValue(null,oldLanguage);
                EditorSettings.enterPlayModeOptionsEnabled=oldOptions;EditorSettings.enterPlayModeOptions=oldPlayOptions;
                File.WriteAllText(Output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);Debug.Log("[AppleAndHints] "+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
