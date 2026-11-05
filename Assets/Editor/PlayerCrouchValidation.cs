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
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class PlayerCrouchValidation
    {
        const string Output="Logs/PlayerCrouch";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly MethodInfo stance=typeof(FirstPersonController).GetMethod("UpdateCrouch",Private);
        static readonly Vector3 Stage=new Vector3(5000,0,5000);
        public static async void Run()
        {
            Directory.CreateDirectory(Output);var report=new StringBuilder();bool passed=false;
            bool oldOptions=EditorSettings.enterPlayModeOptionsEnabled;var oldPlayOptions=EditorSettings.enterPlayModeOptions;
            GameObject actor=null,floor=null,roof=null,obstacle=null;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                var source=Object.FindFirstObjectByType<FirstPersonController>();Require(source,"No human controller.");
                actor=Object.Instantiate(source.gameObject);actor.name="Crouch physics probe";
                actor.transform.localScale=source.transform.lossyScale;
                foreach(var camera in actor.GetComponentsInChildren<Camera>())camera.enabled=false;
                var player=actor.GetComponent<FirstPersonController>();player.enabled=false;
                typeof(FirstPersonController).GetMethod("Awake",Private).Invoke(player,null);
                var capsule=actor.GetComponent<CharacterController>();
                float initialHeight=capsule.height,initialStep=capsule.stepOffset;var initialCenter=capsule.center;
                var eye=player.CameraRoot.localPosition;
                floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=Stage-Vector3.up*0.5f;floor.transform.localScale=new Vector3(100,1,100);
                capsule.enabled=false;actor.transform.position+=Stage-Feet(capsule)+Vector3.up*0.05f;capsule.enabled=true;
                Physics.SyncTransforms();var initialFeet=Feet(capsule);float initialEye=player.CameraRoot.position.y;
                Tick(player,true,0.03f);Require(player.CrouchAmount>0 && player.CrouchAmount<1,"Crouch snapped instead of blending.");
                Tick(player,true,0.3f);float worldCrouch=capsule.height*Mathf.Abs(actor.transform.lossyScale.y);
                float worldStanding=initialHeight*Mathf.Abs(actor.transform.lossyScale.y);
                Require(player.IsCrouching && capsule.height<initialHeight*0.7f,"Capsule did not shrink.");
                Require(Vector3.Distance(Feet(capsule),initialFeet)<0.001f,"Crouch moved the feet.");
                Require(Mathf.Abs(initialEye-player.CameraRoot.position.y-(worldStanding-worldCrouch))<0.001f,"Camera did not follow capsule height.");
                roof=GameObject.CreatePrimitive(PrimitiveType.Cube);roof.transform.position=Stage+new Vector3(0,Mathf.Lerp(worldCrouch,worldStanding,0.3f)+0.1f,3);
                roof.transform.localScale=new Vector3(8,0.2f,4);Physics.SyncTransforms();
                capsule.Move(new Vector3(0,-0.05f,3));Physics.SyncTransforms();
                Require(actor.transform.position.z>Stage.z+2.5f,"Crouched player cannot enter the low passage.");
                Tick(player,false,0.4f);Require(player.CrouchAmount>0.99f,"Player stood up through a low ceiling.");
                Require(capsule.bounds.max.y<roof.GetComponent<Collider>().bounds.min.y+0.03f,"Capsule penetrated the roof.");
                capsule.Move(new Vector3(0,-0.05f,8));Physics.SyncTransforms();Tick(player,false,0.3f);
                Require(!player.IsCrouching && Mathf.Abs(capsule.height-initialHeight)<0.00001f && capsule.center==initialCenter && player.CameraRoot.localPosition==eye,"Standing pose was not restored after leaving cover.");
                Require(Mathf.Abs(capsule.stepOffset-initialStep)<0.00001f,$"Standing step offset changed: {initialStep} -> {capsule.stepOffset}, amount={player.CrouchAmount}, height={capsule.height}.");
                Object.DestroyImmediate(roof);roof=null;

                Tick(player,true,0.3f);
                obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position=actor.transform.position+Vector3.up*(worldStanding*0.2f);
                obstacle.transform.localScale=Vector3.one*0.2f;var ignored=obstacle.GetComponent<Collider>();
                Physics.IgnoreCollision(capsule,ignored,true);Physics.SyncTransforms();Tick(player,false,0.3f);
                Require(!player.IsCrouching,"Held/ignored prop blocked standing.");
                Physics.IgnoreCollision(capsule,ignored,false);ignored.isTrigger=true;
                Tick(player,true,0.3f);Physics.SyncTransforms();Tick(player,false,0.3f);
                Require(!player.IsCrouching,"Trigger blocked standing.");Object.DestroyImmediate(obstacle);obstacle=null;
                Tick(player,true,0.1f,1f/30);float at30=capsule.height;Tick(player,false,0.3f);
                Tick(player,true,0.1f,1f/120);float at120=capsule.height;
                Require(Mathf.Abs(at30-at120)<initialHeight*0.001f,"Crouch depends on frame rate.");
                report.AppendLine($"Authored scale {actor.transform.lossyScale}: capsule {worldStanding:F2}m -> {worldCrouch:F2}m, smooth eye drop, feet preserved.");
                report.AppendLine("Low passage traversed; blocked stand rejected; original height/center/camera/step restored after exit. Ignored props/triggers do not block standing. 30/120Hz matched.");
                Object.DestroyImmediate(actor);actor=null;Object.DestroyImmediate(floor);floor=null;

                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();for(int i=0;i<120 && !EditorApplication.isPlaying;i++)await Task.Delay(100);await Task.Delay(600);
                Require(EditorApplication.isPlaying,"Play did not start.");player=Object.FindFirstObjectByType<FirstPersonController>();
                capsule=player.GetComponent<CharacterController>();Tick(player,true,0.3f);
                var menu=Object.FindFirstObjectByType<GameSettingsMenu>();menu.Open();float pausedHeight=capsule.height;
                await Task.Delay(250);Require(Mathf.Abs(capsule.height-pausedHeight)<0.00001f,"Pause changed stance.");
                menu.Close();await Task.Delay(100);
                player.enabled=false;float screenHeight=capsule.height;var screenEye=player.CameraRoot.localPosition;
                await Task.Delay(200);Require(capsule.height==screenHeight && player.CameraRoot.localPosition==screenEye,"Screen/control handoff reset stance.");player.enabled=true;
                Require(menu.Root.Q<UnityEngine.UIElements.Button>("bind-Crouch")!=null,"Crouch cannot be rebound in settings.");
                Require((int)GameCommand.RobotRecover==19 && (int)GameCommand.Crouch==20,"Existing saved action IDs changed.");
                report.AppendLine("Live player: pause and screen-style disable/enable preserve stance; settings include crouch; saved control IDs preserved.");
                passed=true;
            }
            catch(Exception exception){report.AppendLine(exception.ToString());Debug.LogException(exception);}
            finally
            {
                if(actor)Object.DestroyImmediate(actor);if(floor)Object.DestroyImmediate(floor);if(roof)Object.DestroyImmediate(roof);if(obstacle)Object.DestroyImmediate(obstacle);
                Time.timeScale=1;
                if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);}
                EditorSettings.enterPlayModeOptionsEnabled=oldOptions;EditorSettings.enterPlayModeOptions=oldPlayOptions;
                File.WriteAllText(Output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);Debug.Log("[PlayerCrouch] "+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }
        static Vector3 Feet(CharacterController capsule)=>capsule.transform.TransformPoint(capsule.center)-capsule.transform.up*capsule.height*Mathf.Abs(capsule.transform.lossyScale.y)*0.5f;
        static void Tick(FirstPersonController player,bool requested,float duration,float dt=0.02f)
        {for(int i=0;i<Mathf.RoundToInt(duration/dt);i++)stance.Invoke(player,new object[]{requested,dt});}
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
