using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Rendering;
using MoonThemedGame.Scavenger;
using MoonThemedGame.Weather;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    // Native rendering and gameplay checks in a disposable project copy.
    public static class WeatherAndUplinkValidation
    {
        const string Output="Logs/WeatherAndUplink";
        public static async void Run()
        {
            Directory.CreateDirectory(Output);var report=new StringBuilder();bool passed=false;
            bool oldOptions=EditorSettings.enterPlayModeOptionsEnabled,oldAsync=ShaderUtil.allowAsyncCompilation;
            var oldPlayOptions=EditorSettings.enterPlayModeOptions;int oldQuality=QualitySettings.GetQualityLevel();
            float oldTimeScale=Time.timeScale;GameObject fixtures=null;Material cardMaterial=null;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for(int i=0;i<120 && !EditorApplication.isPlaying;i++)await Task.Delay(100);
                Require(EditorApplication.isPlaying,"Play mode did not start.");await Task.Delay(600);
                Time.timeScale=1;ShaderUtil.allowAsyncCompilation=false;
                var weather=MoonWeatherSystem.Active;var link=Object.FindFirstObjectByType<ScavengerLink>();
                Require(weather && weather.assets && link && link.Drive,"Scene weather/rover setup failed.");
                var network=link.Network;
                typeof(SatelliteNetwork).GetField("rivalTimer",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(network,100000f);
                var operatorCamera=Camera.main;
                Require(weather.ViewCamera==operatorCamera,"Operator camera not used for weather.");
                link.Connect();
                for(int i=0;i<80 && link.Phase!=LinkPhase.Live;i++)await Task.Delay(100);
                Require(link.Phase==LinkPhase.Live && weather.ViewCamera==link.robotCamera,"Weather did not follow the rover camera.");
                link.Drive.enabled=false;
                var land=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r=>r.name=="LAND").bounds;
                var far=new Vector3(land.min.x+40,land.max.y+200,land.min.z+40);
                link.Drive.PlaceAt(far+Vector3.up*2,Quaternion.identity);
                link.Drive.Body.isKinematic=true;
                link.robotCamera.GetComponent<ScavengerOrbitCamera>().ResetView(true);
                fixtures=new GameObject("Weather and uplink fixtures");
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.parent=fixtures.transform;
                floor.transform.position=far-Vector3.up*0.5f;floor.transform.localScale=new Vector3(160,1,160);floor.layer=29;
                Physics.SyncTransforms();
                int best=Enumerable.Range(0,network.satellites.Count).OrderByDescending(i=>network.satellites[i].Strength(network.Clock)).First();
                network.Select(best);
                network.Evaluate(far+Vector3.up*4,null,out float farQuality,out float farCover);
                network.Evaluate(far+new Vector3(800,4,800),null,out float distantQuality,out float distantCover);
                Require(farQuality>=0.85f && farCover==0 && distantCover==0 && Mathf.Abs(farQuality-distantQuality)<0.001f,"Open-sky uplink depends on position or remains weak.");
                foreach(var satellite in network.satellites)
                    for(int i=0;i<1800;i++)
                    {
                        float time=i*0.1f;
                        Require(!satellite.InRange(time) || satellite.Strength(time)>=0.8499f,"Weak in-range orbit signal.");
                    }
                var side=GameObject.CreatePrimitive(PrimitiveType.Cube);side.transform.parent=fixtures.transform;
                side.transform.position=far+new Vector3(5,20,0);side.transform.localScale=new Vector3(1,40,80);side.layer=29;
                Physics.SyncTransforms();Require(network.Cover(far+Vector3.up*4,null)==0,"Vertical cliff counted as overhead cover.");
                var roof=GameObject.CreatePrimitive(PrimitiveType.Cube);roof.transform.parent=fixtures.transform;
                roof.transform.position=far+Vector3.up*12;roof.transform.localScale=new Vector3(60,1,60);roof.layer=29;
                Physics.SyncTransforms();network.Evaluate(far+Vector3.up*4,null,out float roofQuality,out float roofCover);
                Require(roofCover>0.9f && roofQuality<farQuality && roofQuality>0.4f,"Roof penalty is missing or excessive.");
                Object.DestroyImmediate(side);Object.DestroyImmediate(roof);Physics.SyncTransforms();
                int unavailable=Enumerable.Range(0,network.satellites.Count).First(i=>!network.satellites[i].InRange(network.Clock));
                network.Select(unavailable);Require(network.Evaluate(far,null,out _,out _)==UplinkStatus.OutOfRange,"Out-of-range selection no longer matters.");network.Select(best);
                report.AppendLine($"Open-sky quality {farQuality:P0} at opposite map positions; orbit floor 85%; cliff ignored; roof quality {roofQuality:P0}; manual satellite switching preserved.");

                weather.SetWeather(WeatherKind.MoonDust);Require(weather.Density>0.06f && weather.Intensity>0.99f,"Manual weather selection is still invisible during fade-in.");
                await Task.Delay(650);
                var particles=weather.GetComponentInChildren<ParticleSystem>();
                var live=new ParticleSystem.Particle[1200];int count=particles.GetParticles(live);
                double particleDistance=count>0?live.Take(count).Average(p=>Vector3.Distance(p.position,link.robotCamera.transform.position)):0;
                Require(count>5 && particleDistance<60,$"Dust particles remained at the base: count={count}, distance={particleDistance}, camera={link.robotCamera.transform.position}, emitter={particles.transform.position}.");
                report.AppendLine($"Dust: {count} particles, mean distance {particleDistance:F1}m from active rover camera.");
                var monitor=Object.FindFirstObjectByType<WeatherMonitorDisplay>();Require(monitor && monitor.Root!=null,"Forecast monitor is missing.");
                var cameraHost=new GameObject("Weather render camera");cameraHost.transform.parent=fixtures.transform;
                var camera=cameraHost.AddComponent<Camera>();camera.CopyFrom(operatorCamera);camera.enabled=false;
                camera.cullingMask=1<<30;camera.aspect=16f/9;camera.farClipPlane=250;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
                camera.transform.SetPositionAndRotation(far+Vector3.up*8,Quaternion.identity);
                var card=GameObject.CreatePrimitive(PrimitiveType.Quad);card.transform.parent=fixtures.transform;card.layer=30;
                card.transform.position=camera.transform.position+Vector3.forward*100;card.transform.localScale=Vector3.one*200;
                cardMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));cardMaterial.SetColor("_BaseColor",new Color(0.35f,0.35f,0.35f));card.GetComponent<Renderer>().sharedMaterial=cardMaterial;
                foreach(int quality in new[]{1,0})foreach(var style in new[]{VisualStyle.Retro,VisualStyle.ColorAscii})
                {
                    QualitySettings.SetQualityLevel(quality,true);VisualStyleSwitcher.SetStyle(style);
                    weather.SetWeather(WeatherKind.Sunny);var clear=Capture(camera,$"{quality}-{style}-sunny");
                    weather.SetWeather(WeatherKind.MoonDust);var dust=Capture(camera,$"{quality}-{style}-dust");
                    weather.SetWeather(WeatherKind.Radiation);var radiation=Capture(camera,$"{quality}-{style}-radiation");
                    // ASCII occupies only a small share of pixels; compare both absolute and relative change.
                    float dustDifference=(dust-clear).magnitude;
                    Require(dustDifference>(style==VisualStyle.ColorAscii?0.003f:0.015f) && dustDifference>clear.magnitude*0.1f,
                        "Dust is hidden by permanent haze: "+quality+" "+style+" "+clear+" "+dust);
                    float greenThreshold=style==VisualStyle.ColorAscii?0.003f:0.012f;
                    Require(radiation.y>clear.y+greenThreshold && radiation.y>clear.y*1.15f,
                        "Radiation tint is hidden by permanent haze: "+quality+" "+style+" "+clear+" "+radiation);
                    report.AppendLine($"Renderer {quality} {style}: clear/dust/radiation RGB {clear}/{dust}/{radiation}.");
                }
                var probe=new GameObject("Radiation energy probe").AddComponent<ScavengerRobotState>();probe.transform.parent=fixtures.transform;
                probe.transform.position=far;probe.Active=true;weather.SetWeather(WeatherKind.Radiation);
                float charge=probe.Charge,start=Time.time;await Task.Delay(600);
                Require(Mathf.Abs(charge-probe.Charge-(Time.time-start)*probe.drainPerSecond*weather.toxicEnergyMultiplier)<0.3f,"Exterior radiation drain failed.");
                Require(weather.EnergyMultiplier(weather.shelterCenter)==1,"Radiation affects the base interior.");
                probe.gameObject.SetActive(false);
                weather.SetWeather(WeatherKind.Sunny,0.2f,false);await Task.Delay(400);
                Require(weather.Current==WeatherKind.MoonDust && weather.Intensity<0.1f,"Automatic schedule/fade-in did not advance.");
                monitor.Refresh();Require(weather.Forecast(1).startsIn>weather.Forecast(0).startsIn,"Forecast ETAs are not cumulative.");
                float remaining=weather.Remaining;Time.timeScale=0;await Task.Delay(250);Require(weather.Remaining==remaining,"Paused weather clock advanced.");Time.timeScale=1;
                report.AppendLine("Live dust follows rover; radiation drains energy outside only; automatic schedule, forecast and pause passed.");

                weather.meteorInterval=new Vector2(5.5f,5.5f);weather.SetWeather(WeatherKind.MeteoriteRain);
                Require(weather.TryFindStrike(out var point,out var normal) && Vector3.Distance(point,far)<90,"Meteor targets did not follow the rover to the map edge.");
                var strike=weather.ScheduleStrike(point,normal);strike.enabled=false;
                Require(strike && strike.Remaining>=5,"Meteor warning is shorter than 5 seconds.");
                strike.Advance(4.9f);Require(!strike.HasFallen,"Meteor hit before warning expired.");strike.Advance(0.11f);Require(strike.HasFallen,"Meteor impact did not resolve.");
                int impacts=weather.Impacts;float rainStart=Time.time;
                for(int i=0;i<180 && weather.Impacts==impacts;i++)await Task.Delay(100);
                Require(weather.Impacts>impacts,"Automatic meteor rain produced no impacts at the map edge.");
                report.AppendLine($"Meteors: map-edge target, five-second warning and automatic impact in {Time.time-rainStart:F1}s passed.");
                foreach(var message in ShaderUtil.GetShaderMessages(Shader.Find("Moon Game/Exterior Weather Atmosphere")))
                    Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
                link.Disconnect();Require(weather.ViewCamera==operatorCamera,"Weather did not return to operator camera.");
                passed=true;
            }
            catch(Exception exception){report.AppendLine(exception.ToString());Debug.LogException(exception);}
            finally
            {
                Time.timeScale=oldTimeScale;
                if(fixtures)Object.DestroyImmediate(fixtures);if(cardMaterial)Object.DestroyImmediate(cardMaterial);
                if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);}
                EditorSettings.enterPlayModeOptionsEnabled=oldOptions;EditorSettings.enterPlayModeOptions=oldPlayOptions;
                QualitySettings.SetQualityLevel(oldQuality,true);ShaderUtil.allowAsyncCompilation=oldAsync;
                File.WriteAllText(Output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);Debug.Log("[WeatherAndUplink] "+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }
        static Vector3 Capture(Camera camera,string name)
        {
            var target=RenderTexture.GetTemporary(320,180,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;var original=camera.targetTexture;var texture=new Texture2D(320,180,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,320,180),0,0);texture.Apply();
                File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
                var pixels=texture.GetPixels();var sum=Vector3.zero;
                foreach(var pixel in pixels)sum+=new Vector3(pixel.r,pixel.g,pixel.b);
                return sum/pixels.Length;
            }
            finally{camera.targetTexture=original;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(texture);}
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
