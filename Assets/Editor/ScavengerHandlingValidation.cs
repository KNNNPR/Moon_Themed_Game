using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class ScavengerHandlingValidation
    {
        const string Output="Logs/ScavengerHandling";
        static readonly Vector3 Stage=new Vector3(5000,0,5000);
        static ScavengerDrive drive;
        static Vector3 rest;
        static Quaternion restRotation;

        // Focused startup regression check for an unwanted generated floor.
        public static async void RunStartArea()
        {
            Directory.CreateDirectory(Output);
            bool passed=false, oldOptions=EditorSettings.enterPlayModeOptionsEnabled;
            var oldPlayOptions=EditorSettings.enterPlayModeOptions;
            var report=new StringBuilder();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                Require(!ScavengerStartLocation.Find(),"Expected the authored scene without a generated start area.");
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for(int i=0;i<120 && !EditorApplication.isPlaying;i++)await Task.Delay(100);
                await Task.Delay(700);
                var link=Object.FindFirstObjectByType<ScavengerLink>();
                Require(link && link.Drive && link.robotCamera,"SCAV_BOT setup failed.");
                var location=ScavengerStartLocation.Find();
                Require(location && ScavengerStartLocation.Marker(location),"Spawn marker missing.");
                AssertNoStartGeometry(location);
                report.AppendLine("Startup creates only a Robot Start marker: no generated mesh, renderer or collider.");
                link.Connect();
                for(int i=0;i<80 && link.Phase!=LinkPhase.Live;i++)await Task.Delay(100);
                Require(link.Phase==LinkPhase.Live && link.Drive.controllable,"Robot connection failed.");
                AssertNoStartGeometry(location);
                Require(link.Drive.Body.position.y>ScavengerStartLocation.Marker(location).position.y-link.Drive.Length*0.3f,"Removing the visual floor made the robot fall through its start point.");
                link.Disconnect(); await Task.Delay(100);
                Require(!link.Connected,"Disconnect failed.");
                AssertNoStartGeometry(location);
                report.AppendLine("Connect/disconnect does not recreate the floor; spawn support and vehicle controls remain valid.");
                passed=true;
            }
            catch(Exception exception) {report.AppendLine(exception.ToString());Debug.LogException(exception);}
            finally
            {
                if(EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                    for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);
                }
                EditorSettings.enterPlayModeOptionsEnabled=oldOptions;EditorSettings.enterPlayModeOptions=oldPlayOptions;
                File.WriteAllText(Output+"/start-area-validation.txt",(passed?"PASS\n":"FAIL\n")+report);
                Debug.Log("[ScavengerStartArea] "+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }

        static void AssertNoStartGeometry(Transform location)
        {
            Require(location.GetComponentsInChildren<Renderer>(true).Length==0 &&
                location.GetComponentsInChildren<MeshFilter>(true).Length==0 &&
                location.GetComponentsInChildren<Collider>(true).Length==0,"Unexpected generated start-area surface.");
        }

        // Intended for an isolated batch copy. Uses the real bot mesh, suspension and PhysX.
        public static async void Run()
        {
            Directory.CreateDirectory(Output);
            var report=new StringBuilder();
            bool passed=false;
            var oldSimulation=Physics.simulationMode;
            float oldFixed=Time.fixedDeltaTime;
            bool oldOptions=EditorSettings.enterPlayModeOptionsEnabled;
            var oldPlayOptions=EditorSettings.enterPlayModeOptions;
            GameObject testBot=null, ground=null, cameraHost=null, obstacle=null;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                var source=GameObject.Find(ScavengerLink.RobotName);
                Require(source,"SCAV_BOT missing.");
                ground=GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name="Handling test floor"; ground.transform.position=Stage-Vector3.up*0.5f;
                ground.transform.localScale=new Vector3(500,1,500);
                testBot=Object.Instantiate(source); testBot.name="Rally dynamics test";
                testBot.transform.localScale*=0.6f;
                var bounds=Bounds(testBot);
                testBot.transform.position+=Stage-new Vector3(bounds.center.x,bounds.min.y-0.15f,bounds.center.z);
                bounds=Bounds(testBot);
                var forward=ScavengerLink.DetectForward(testBot.transform,bounds.center);
                drive=testBot.AddComponent<ScavengerDrive>(); drive.Setup(forward);
                drive.controllable=true;
                drive.Body.interpolation=RigidbodyInterpolation.None;
                Physics.simulationMode=SimulationMode.Script;
                Time.fixedDeltaTime=0.02f;
                Simulate(Vector2.zero,2);
                rest=drive.Body.position; restRotation=drive.Body.rotation;
                Require(Vector3.Dot(testBot.transform.up,Vector3.up)>0.9f,"Bot did not settle upright.");
                report.AppendLine($"Length {drive.Length:F2}m; top speed {drive.MaximumForwardSpeed:F2}m/s; actual sphere-cast suspension and Rigidbody.");

                Simulate(Vector2.up,0.45f);
                float early=drive.ForwardSpeed/drive.MaximumForwardSpeed;
                Require(early>0.15f && early<0.55f,"Rally acceleration should build progressively: "+early);
                Simulate(Vector2.up,1.35f);
                float top=drive.ForwardSpeed;
                Require(top>drive.MaximumForwardSpeed*0.88f,"Cannot reach cruise speed.");
                Require(Vector3.Dot(drive.transform.up,Vector3.up)>0.8f,"Acceleration overturned the car.");
                Simulate(Vector2.zero,0.3f);
                float coasting=drive.ForwardSpeed;
                Require(coasting>top*0.25f && coasting<top*0.95f,"Release should coast without stopping or keeping full speed.");
                Simulate(Vector2.down,1.1f);
                Require(drive.ForwardSpeed < -drive.MaximumForwardSpeed*0.15f,"Braking did not transition into reverse.");
                report.AppendLine($"At 0.45s: {early:P0} of maximum speed. Coasting after 0.3s: {coasting/top:P0}. Brake/reverse passed.");

                Reset(); float parked=Heading();
                Simulate(Vector2.right,0.5f);
                Require(Mathf.Abs(Mathf.DeltaAngle(parked,Heading()))<2,"Stationary steering still pivots the chassis.");
                Reset(); Simulate(Vector2.up,0.6f); float start=Heading();
                Simulate(Vector2.one,0.4f); float forwardTurn=Mathf.DeltaAngle(start,Heading());
                Require(forwardTurn>12,"Forward steering remains too slow: "+forwardTurn);
                Require(Vector3.Dot(drive.transform.up,Vector3.up)>0.75f,"Turning overturned the car.");
                Reset(); Simulate(Vector2.down,0.6f); start=Heading();
                Simulate(new Vector2(1,-1),0.4f); float reverseTurn=Mathf.DeltaAngle(start,Heading());
                Require(reverseTurn < -8,"Reverse steering has the wrong direction: "+reverseTurn);
                report.AppendLine($"0.4s right steering: forward {forwardTurn:F1}deg, reverse {reverseTurn:F1}deg. No stationary skid-spin.");

                Reset();Simulate(Vector2.up,1.6f);
                float normalStart=Heading();
                Simulate(Vector2.one,0.5f);float normalSlip=Mathf.Abs(drive.SlipAngle);
                float normalYaw=Mathf.Abs(Mathf.DeltaAngle(normalStart,Heading()));
                Reset();Simulate(Vector2.up,1.6f);
                float entrySpeed=Vector3.ProjectOnPlane(drive.Body.linearVelocity,Vector3.up).magnitude;
                float driftStart=Heading();
                Simulate(Vector2.one,0.5f,0.02f,true);float driftSlip=Mathf.Abs(drive.SlipAngle);
                float driftYaw=Mathf.Abs(Mathf.DeltaAngle(driftStart,Heading()));
                float retained=Vector3.ProjectOnPlane(drive.Body.linearVelocity,Vector3.up).magnitude/entrySpeed;
                Require(driftSlip>normalSlip+12 && driftSlip>30,"Handbrake did not produce a visible slide: "+normalSlip+" / "+driftSlip);
                Require(driftYaw>normalYaw+12,"Handbrake did not tighten the turn: "+normalYaw+" / "+driftYaw);
                Require(retained>0.6f,"Handbrake killed the momentum: "+retained);
                Require(!drive.IsOverturned,"Initiating a drift overturned the car.");
                Simulate(new Vector2(-1,1),0.2f);Simulate(Vector2.up,0.8f);
                Require(drive.DriftAmount<0.01f && Mathf.Abs(drive.SlipAngle)<driftSlip*0.8f+4,"Grip did not recover after releasing handbrake.");
                report.AppendLine($"0.5s turn: normal yaw/slip {normalYaw:F1}/{normalSlip:F1}deg; handbrake {driftYaw:F1}/{driftSlip:F1}deg, retained speed {retained:P0}; countersteer/grip recovery passed.");
                Reset();Simulate(Vector2.up,1.6f);
                float accumulated=0,lastHeading=Heading();
                for(int i=0;i<65;i++)
                {
                    Simulate(Vector2.one,0.02f,0.02f,true);
                    float headingNow=Heading();accumulated+=Mathf.DeltaAngle(lastHeading,headingNow);lastHeading=headingNow;
                }
                Require(accumulated>130,"Holding handbrake cancelled the U-turn: "+accumulated);
                Require(!drive.IsOverturned,"Sustained handbrake turn overturned the car.");
                report.AppendLine($"Sustained 1.3s handbrake turn: {accumulated:F1}deg without steering reversal or rollover.");

                Reset();float time90=0;
                while(drive.ForwardSpeed<drive.MaximumForwardSpeed*0.9f && time90<3)
                {Simulate(Vector2.up,0.02f);time90+=0.02f;}
                Require(time90>0.65f && time90<1.35f,"Unexpected racing acceleration time: "+time90);
                report.AppendLine($"0-90% speed: {time90:F2}s.");

                Reset(); Simulate(Vector2.up,1); drive.controllable=false;
                Simulate(Vector2.one,0.7f);
                Require(Mathf.Abs(drive.ForwardSpeed)<drive.Length*0.05f,"Disabled drive still responds to throttle.");
                drive.controllable=true;
                Reset(); Simulate(Vector2.up,0.7f); float speed50=drive.ForwardSpeed;
                Reset(); Simulate(Vector2.up,0.7f,0.01f); float speed100=drive.ForwardSpeed;
                Require(Mathf.Abs(speed50-speed100)<drive.MaximumForwardSpeed*0.12f,"Physics-step-dependent speed.");
                report.AppendLine($"50/100Hz speed after 0.7s: {speed50:F2}/{speed100:F2}m/s; disabled controls brake safely.");

                Reset();
                obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name="Low kerb";
                obstacle.transform.position=Stage+drive.Heading*drive.Length*2+Vector3.up*drive.Length*0.035f;
                obstacle.transform.rotation=Quaternion.LookRotation(drive.Heading);
                obstacle.transform.localScale=new Vector3(drive.Length*3,drive.Length*0.07f,drive.Length*0.25f);
                Physics.SyncTransforms();
                var before=drive.Body.position;
                Simulate(Vector2.up,2);
                Require(Vector3.Distance(before,drive.Body.position)>drive.Length*3,"The car stuck on a low kerb.");
                Require(Vector3.Dot(drive.transform.up,Vector3.up)>0.6f,"Low kerb overturned the car.");
                Object.DestroyImmediate(obstacle); obstacle=null;

                // Starting at bridge height must not leave a virtual floor beside/beyond the deck.
                ground.transform.position-=Vector3.up*drive.Length*3;
                obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name="Raised bridge deck";
                obstacle.transform.rotation=Quaternion.LookRotation(drive.Heading);
                obstacle.transform.localScale=new Vector3(drive.Length*3,0.4f,drive.Length*4);
                obstacle.transform.position=Stage+drive.Heading*drive.Length-Vector3.up*0.2f;
                Physics.SyncTransforms();Reset();Simulate(Vector2.zero,0.5f);
                Require(Mathf.Abs(drive.Body.position.y-rest.y)<drive.Length*0.15f,"Suspension cannot stand on a real bridge deck.");
                drive.PlaceAt(rest+Vector3.Cross(Vector3.up,drive.Heading)*drive.Length*3,restRotation);
                Physics.SyncTransforms();Simulate(Vector2.zero,1);
                float besideDrop=rest.y-drive.Body.position.y;
                Require(besideDrop>drive.Length*0.4f,"Invisible support beside the bridge: "+besideDrop);
                Reset();Simulate(Vector2.up,5);
                float edgeDrop=rest.y-drive.Body.position.y;
                Require(edgeDrop>drive.Length*2,"Driving off a bridge still rides an invisible plane: "+edgeDrop);
                Require(drive.Body.position.y>ground.transform.position.y-1,"Bot fell through real terrain below the bridge.");
                report.AppendLine($"Real ground only: bridge supports suspension; beside-deck drop {besideDrop:F2}m in 1s; driving off deck falls {edgeDrop:F2}m onto lower terrain.");
                Object.DestroyImmediate(obstacle);obstacle=null;
                ground.transform.position=Stage-Vector3.up*0.5f;Physics.SyncTransforms();

                Reset();
                drive.PlaceAt(rest+Vector3.up*20,restRotation);
                Simulate(Vector2.one,0.5f);
                Require(new Vector2(drive.Body.linearVelocity.x,drive.Body.linearVelocity.z).magnitude<drive.Length*0.05f,"Drive generates thrust in mid-air.");
                report.AppendLine("Low kerb crossed upright; no airborne steering/thrust.");

                Reset();
                Require(!drive.CanRecover && !drive.RecoverUpright(),"Recovery was allowed while upright.");
                var rolled=Quaternion.AngleAxis(180,drive.Heading)*restRotation;
                drive.PlaceAt(rest+Vector3.up*drive.Length*0.4f,rolled);
                Simulate(Vector2.zero,4);
                Require(drive.IsOverturned,"Bot automatically righted itself.");
                Require(drive.CanRecover,"Stopped overturned bot cannot be righted.");
                Require(drive.RecoverUpright(),"Manual recovery failed.");Simulate(Vector2.zero,0.8f);
                Require(!drive.IsOverturned && !drive.CanRecover && !drive.RecoverUpright(),"Recovery did not return the bot to its wheels or remained active.");
                drive.PlaceAt(rest+Vector3.up*drive.Length*3,rolled);Simulate(Vector2.zero,0.7f);
                Require(drive.IsOverturned && !drive.CanRecover && !drive.RecoverUpright(),"Recovery was allowed in mid-air.");
                report.AppendLine("Overturned for 4s without auto-flip; manual recovery passed; upright and airborne recovery rejected.");

                Reset();
                cameraHost=new GameObject("Orbit input test"); cameraHost.AddComponent<Camera>().enabled=false;
                var orbit=cameraHost.AddComponent<ScavengerOrbitCamera>();
                bounds=Bounds(testBot);
                orbit.Initialize(testBot.transform,drive,testBot.transform.InverseTransformPoint(bounds.center),drive.Length*0.5f);
                orbit.Advance(0.02f,Vector2.zero,false,true);
                float heading=Heading();
                orbit.Advance(0.02f,new Vector2(75,-18),false,true);
                CameraSteps(orbit,0.4f);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading,orbit.Yaw))>60,"Mouse orbit is not independent.");
                Require(Mathf.Abs(Mathf.DeltaAngle(heading,Heading()))<1,"Camera changed the vehicle heading.");
                float heldYaw=orbit.Yaw; CameraSteps(orbit,2);
                Require(Mathf.Abs(Mathf.DeltaAngle(heldYaw,orbit.Yaw))<1,"Parked view recenters unexpectedly.");
                drive.Body.linearVelocity=drive.Heading*drive.Length;
                CameraSteps(orbit,2.2f);
                Require(Mathf.Abs(Mathf.DeltaAngle(Heading(),orbit.Yaw))<3,"Moving camera did not return behind the car.");
                drive.Body.linearVelocity=Vector3.zero;
                orbit.Advance(0.02f,new Vector2(110,-1000),false,true); CameraSteps(orbit,0.4f);
                Require(orbit.Pitch<=orbit.pitchLimits.y+0.1f,"Camera pitch escaped its limit.");
                orbit.Advance(0.02f,Vector2.zero,true,true); CameraSteps(orbit,0.6f);
                Require(Mathf.Abs(orbit.Pitch-orbit.defaultPitch)<1 && Mathf.Abs(Mathf.DeltaAngle(Heading(),orbit.Yaw))<2,"Camera reset failed.");
                heldYaw=orbit.Yaw;
                orbit.Advance(0.1f,new Vector2(90,0),false,false);
                orbit.Advance(0.1f,new Vector2(90,0),false,true);
                Require(Mathf.Abs(Mathf.DeltaAngle(heldYaw,orbit.Yaw))<1,"Tuner/pause leaked a mouse delta into the camera.");
                float at30=CameraGesture(orbit,30),at120=CameraGesture(orbit,120);
                Require(Mathf.Abs(Mathf.DeltaAngle(at30,at120))<2,"Orbit input is frame-rate dependent.");
                report.AppendLine("Orbit: independent mouse/stick degree input, parked hold, moving recenter, reset/pitch limits and UI input guard passed; 30/120fps matched.");
                orbit.ResetView(true);drive.Body.linearVelocity=Quaternion.AngleAxis(30,Vector3.up)*drive.Heading*drive.Length;
                CameraSteps(orbit,2);
                Require(Mathf.Abs(Mathf.DeltaAngle(Heading(),orbit.Yaw)-12)<3,"Camera did not account for the direction of drift.");
                drive.Body.linearVelocity=Vector3.zero;orbit.ResetView(true);
                report.AppendLine("Drift camera follows a blend of chassis heading and travel direction.");

                orbit.ResetView(true);float parkedFov=cameraHost.GetComponent<Camera>().fieldOfView,parkedDistance=orbit.Distance;
                drive.Body.linearVelocity=drive.Heading*drive.MaximumForwardSpeed;CameraSteps(orbit,1.2f);
                Require(cameraHost.GetComponent<Camera>().fieldOfView>parkedFov+8 && orbit.Distance>parkedDistance*1.08f,"Speed camera has no FOV/distance response.");
                Require(orbit.Pitch<orbit.defaultPitch-2,"Speed camera did not lower its chase angle.");
                drive.Body.angularVelocity=Vector3.up*2;CameraSteps(orbit,0.4f);
                float bank=Mathf.Abs(Mathf.DeltaAngle(0,cameraHost.transform.eulerAngles.z));
                Require(bank>0.1f && bank<=orbit.maximumCornerBank+0.1f,"Cornering camera bank is missing or excessive.");
                drive.Body.linearVelocity=drive.Body.angularVelocity=Vector3.zero;CameraSteps(orbit,2);
                Require(Mathf.Abs(cameraHost.GetComponent<Camera>().fieldOfView-parkedFov)<0.1f,"Speed FOV did not settle at rest.");
                report.AppendLine("Speed presentation: smooth FOV expansion, camera pullback/lower angle, bounded corner bank, and clean return at rest passed.");

                orbit.ResetView(true); orbit.Advance(0.02f,Vector2.zero,false,true);
                float full=orbit.Distance;
                var pivot=bounds.center+Vector3.up*drive.Length*0.125f;
                var away=(cameraHost.transform.position-pivot).normalized;
                obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube); obstacle.name="Camera wall";
                obstacle.transform.position=pivot+away*full*0.55f;
                obstacle.transform.localScale=Vector3.one*drive.Length*0.5f;
                Physics.SyncTransforms(); CameraSteps(orbit,0.15f);
                Require(orbit.Distance<full*0.8f && !obstacle.GetComponent<Collider>().bounds.Contains(cameraHost.transform.position),"Camera clipped into wall.");
                Object.DestroyImmediate(obstacle); obstacle=null; CameraSteps(orbit,0.8f);
                Require(orbit.Distance>full*0.95f,"Camera did not extend after wall.");
                report.AppendLine("Camera obstruction pull-in and smooth restore passed.");

                Object.DestroyImmediate(testBot); testBot=null; drive=null;
                Object.DestroyImmediate(ground); ground=null;
                Object.DestroyImmediate(cameraHost); cameraHost=null;
                Physics.simulationMode=oldSimulation; Time.fixedDeltaTime=oldFixed;
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for(int i=0;i<120 && !EditorApplication.isPlaying;i++)await Task.Delay(100);
                await Task.Delay(600);
                var link=Object.FindFirstObjectByType<ScavengerLink>();
                Require(link && link.Drive && link.robotCamera && link.robotCamera.GetComponent<ScavengerOrbitCamera>(),"Live bot did not install drive/camera.");
                link.Connect();
                for(int i=0;i<80 && link.Phase!=LinkPhase.Live;i++)await Task.Delay(100);
                Require(link.Phase==LinkPhase.Live,"Connection did not enter live feed.");
                Require(link.Drive.controllable,"Live drive unexpectedly disabled.");
                Require(!link.CanRecoverRobot && !link.RecoverRobot(),"Live upright recovery action should be hidden/disabled.");
                var tuner=link.GetComponent<RfPatchPanel>(); tuner.Toggle(); await Task.Delay(100);
                Require(tuner.IsOpen && !link.Drive.controllable,"Tuner did not disable driving.");
                tuner.Close(); await Task.Delay(100);
                Require(link.Drive.controllable,"Drive did not resume after tuner.");
                link.Disconnect(); await Task.Delay(100);
                Require(!link.Connected && !link.robotCamera.enabled && !link.Drive.controllable,"Disconnect did not release camera/control.");
                report.AppendLine("Live Operator scene: camera auto-install, connect, tuner input suppression, resume and disconnect passed.");
                passed=true;
            }
            catch(Exception exception) { report.AppendLine(exception.ToString()); Debug.LogException(exception); }
            finally
            {
                if(testBot)Object.DestroyImmediate(testBot); if(ground)Object.DestroyImmediate(ground);
                if(cameraHost)Object.DestroyImmediate(cameraHost); if(obstacle)Object.DestroyImmediate(obstacle);
                Physics.simulationMode=oldSimulation; Time.fixedDeltaTime=oldFixed;
                if(EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                    for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);
                }
                EditorSettings.enterPlayModeOptionsEnabled=oldOptions; EditorSettings.enterPlayModeOptions=oldPlayOptions;
                File.WriteAllText(Output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);
                Debug.Log("[ScavengerHandling] "+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }
        static void Reset()
        {
            drive.controllable=true; drive.PlaceAt(rest,restRotation); Physics.SyncTransforms();
            Simulate(Vector2.zero,0.5f);
        }
        static void Simulate(Vector2 input,float seconds,float dt=0.02f,bool handbrake=false)
        {
            Time.fixedDeltaTime=dt;
            for(int i=0;i<Mathf.RoundToInt(seconds/dt);i++) { drive.SimulateStep(input,dt,handbrake); Physics.Simulate(dt); }
        }
        static float Heading()=>Quaternion.LookRotation(drive.Heading).eulerAngles.y;
        static Bounds Bounds(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>(); var b=renderers[0].bounds;
            foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }
        static void CameraSteps(ScavengerOrbitCamera orbit,float seconds)
        { for(int i=0;i<Mathf.RoundToInt(seconds/0.02f);i++)orbit.Advance(0.02f,Vector2.zero,false,true); }
        static float CameraGesture(ScavengerOrbitCamera orbit,int fps)
        {
            orbit.ResetView(true);orbit.Advance(1f/fps,Vector2.zero,false,true);
            for(int i=0;i<fps;i++)orbit.Advance(1f/fps,new Vector2(70f/fps,0),false,true);
            CameraSteps(orbit,0.3f);return orbit.Yaw;
        }
        static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    }
}
