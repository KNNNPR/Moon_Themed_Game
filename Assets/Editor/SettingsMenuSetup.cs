using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MoonThemedGame.Interactions;
using MoonThemedGame.Computer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class SettingsMenuSetup
    {
        const string AssetPath="Assets/Resources/SettingsMenuAssets.asset";
        const string Output="Logs/SettingsMenuCaptures";
        static RenderTexture viewTexture;
        [MenuItem("Tools/Moon Game/Settings/Install Menu Assets")]
        public static void Apply()
        {
            var assets=AssetDatabase.LoadAssetAtPath<SettingsMenuAssets>(AssetPath);
            if(!assets) { assets=ScriptableObject.CreateInstance<SettingsMenuAssets>(); AssetDatabase.CreateAsset(assets,AssetPath); }
            assets.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PTMono/PTMono-Regular.ttf");
            assets.theme=AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            assets.style=AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/SettingsMenu/SettingsMenu.uss");
            if(!assets.font || !assets.theme || !assets.style) throw new InvalidOperationException("Settings assets are missing.");
            EditorUtility.SetDirty(assets); AssetDatabase.SaveAssets();
        }

        public static async void ApplyAndValidate()
        {
            Directory.CreateDirectory(Output);
            bool passed=false;
            bool previousOptions=EditorSettings.enterPlayModeOptionsEnabled;
            var options=EditorSettings.enterPlayModeOptions;
            var report=new System.Text.StringBuilder();
            var oldBindings=new Dictionary<GameCommand,Key>();
            Keyboard keyboard=null;
            var background=InputSystem.settings.backgroundBehavior;
            try
            {
                Apply(); EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for(int i=0;i<150 && !EditorApplication.isPlaying;i++) await Task.Delay(100);
                Require(EditorApplication.isPlaying,"Play mode did not start.");
                await Task.Delay(500);
                foreach(GameCommand command in Enum.GetValues(typeof(GameCommand))) oldBindings[command]=GameControls.Binding(command);
                GameControls.ResetBindings();
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                keyboard=InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
                var menu=Object.FindFirstObjectByType<GameSettingsMenu>();
                Require(menu && menu.Root!=null,"Menu missing.");
                Require(menu.Root.Q("apple-clicker")==null,"Removed mini-game is still present.");
                viewTexture=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32); viewTexture.Create();
                menu.Panel.targetTexture=viewTexture; menu.Panel.clearColor=true; menu.Panel.colorClearValue=Color.black;
                var terminal=Object.FindFirstObjectByType<PcMonitorTerminal>(); terminal.Focus(true);
                Keys(keyboard,Key.Escape);
                menu.HandleBack();
                Require(!GameSettingsMenu.IsOpen,"Escape from PC also opened settings.");
                Keys(keyboard); terminal.ExitFocus(true);
                menu.Open();
                Require(Time.timeScale==0 && UnityEngine.Cursor.lockState==CursorLockMode.None,"Pause did not unlock UI.");
                await Task.Delay(300);
                await Capture(menu,"menu.png");
                var volume=menu.Root.Q<Slider>("master-volume");
                float previousVolume=volume.value;
                volume.value=0.43f;
                Require(Mathf.Approximately(AudioListener.volume,0.43f),"Master volume did not update.");
                volume.value=previousVolume;
                menu.ShowControls();
                await Task.Delay(100);
                Require(menu.Root.Q("settings-controls").style.display==DisplayStyle.Flex,"Controls did not open.");
                await Capture(menu,"controls.png");
                menu.HandleBack();
                Require(GameSettingsMenu.IsOpen && menu.Root.Q("settings-home").style.display==DisplayStyle.Flex,"Escape from controls did not return home.");
                await Task.Delay(100);
                menu.BeginRebind(GameCommand.Interact);
                Require(!menu.AcceptRebind(Key.W) && menu.IsCapturing,"Conflicting key was accepted.");
                Require(menu.AcceptRebind(Key.R) && GameControls.Binding(GameCommand.Interact)==Key.R,"Rebind did not apply.");
                Keys(keyboard,Key.R);
                Require(GameControls.Held(GameCommand.Interact),"Keyboard state still uses the old binding.");
                Keys(keyboard);
                Require(GameControls.FormatPrompt("E — Take apple").StartsWith("R"),"Prompts did not follow rebinding.");
                typeof(GameControls).GetField("keys",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);
                Require(GameControls.Binding(GameCommand.Interact)==Key.R,"Saved key did not reload.");
                Require(GameControls.SetBinding(GameCommand.Forward,Key.I,out _),"Move rebind failed.");
                Keys(keyboard,Key.I);
                Require(GameControls.Movement.y==1,"Movement ignores rebind.");
                Keys(keyboard);
                menu.BeginRebind(GameCommand.Throw);
                await Task.Delay(100);
                await Capture(menu,"rebind.png");
                Keys(keyboard,Key.Escape);
                menu.HandleBack();
                Require(GameSettingsMenu.IsOpen && !menu.IsCapturing,"Escape did not cancel only the binding capture.");
                Require(menu.Root.Q("settings-controls").style.display==DisplayStyle.Flex,"Cancelling capture left controls.");
                Keys(keyboard);
                menu.HandleBack();
                Require(Time.timeScale==0 && GameSettingsMenu.IsOpen,"Returning home resumed the game.");
                menu.RequestQuit();
                Require(menu.Root.Q("quit-confirm").style.display==DisplayStyle.Flex,"Quit button did not open confirmation.");
                await Task.Delay(100);
                await Capture(menu,"quit-confirm.png");
                Keys(keyboard,Key.Escape);
                menu.HandleBack();
                Require(GameSettingsMenu.IsOpen && menu.Root.Q("quit-confirm").style.display==DisplayStyle.None,"Escape from quit confirmation closed the entire menu.");
                Keys(keyboard);
                menu.Close(); Require(!GameSettingsMenu.IsOpen && Time.timeScale>0 && GameSettingsMenu.BlockGameplayInput,"Resume frame guard failed.");
                report.AppendLine("PASS: minimal terminal menu without mini-game; master volume; controls navigation; saved/reloaded bindings; conflicts rejected; actual keyboard movement/interact; dynamic prompts; ESC capture/controls/quit/PC priority; pause and resume guard. Quit button is wired to Editor stop / Application.Quit (not executed during test).");
                passed=true;
            }
            catch(Exception exception) { report.AppendLine(exception.ToString()); Debug.LogException(exception); }
            finally
            {
                if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior=background;
                if(oldBindings.Count>0)
                {
                    foreach(var pair in oldBindings) PlayerPrefs.SetInt("controls."+pair.Key,(int)pair.Value);
                    typeof(GameControls).GetField("keys",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);
                    PlayerPrefs.Save();
                }
                var menu=Object.FindFirstObjectByType<GameSettingsMenu>(); if(menu && GameSettingsMenu.IsOpen) menu.Close();
                if(EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                    for(int i=0;i<100 && EditorApplication.isPlaying;i++) await Task.Delay(100);
                }
                AssetDatabase.SaveAssets();
                if(viewTexture) { viewTexture.Release(); Object.DestroyImmediate(viewTexture); viewTexture=null; }
                EditorSettings.enterPlayModeOptionsEnabled=previousOptions; EditorSettings.enterPlayModeOptions=options;
                File.WriteAllText(Output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);
                Debug.Log("[SettingsMenu] "+report);
                if(Application.isBatchMode) EditorApplication.Exit(passed?0:1);
            }
        }
        static void Keys(Keyboard keyboard, params Key[] keys)
        {
            typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null)
                .Invoke(null,new object[]{InputUpdateType.Editor});
            keyboard.MakeCurrent();
            InputState.Change(keyboard,new KeyboardState(),InputUpdateType.Editor);
            InputState.Change(keyboard,new KeyboardState(keys),InputUpdateType.Editor);
        }
        static async Task Capture(GameSettingsMenu menu, string name)
        {
            // Rebuild the off-screen panel's retained draw data before reading a static screen.
            menu.Root.style.display=DisplayStyle.None;
            await Task.Delay(100);
            menu.Root.style.display=DisplayStyle.Flex;
            await Task.Delay(250);
            var previous=RenderTexture.active; var pixels=new Texture2D(viewTexture.width,viewTexture.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=viewTexture; pixels.ReadPixels(new Rect(0,0,viewTexture.width,viewTexture.height),0,0); pixels.Apply(); File.WriteAllBytes(Output+"/"+name,pixels.EncodeToPNG());
            }
            finally { RenderTexture.active=previous; Object.DestroyImmediate(pixels); }
        }
        static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
