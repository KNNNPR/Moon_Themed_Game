using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Computer;
using MoonThemedGame.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class SettingsStartupValidation
    {
        const string Output="Logs/SettingsStartup";
        static readonly BindingFlags Private=BindingFlags.Static|BindingFlags.NonPublic;
        public static async void Run()
        {
            Directory.CreateDirectory(Output);var report=new StringBuilder();bool passed=false;
            bool oldOptions=EditorSettings.enterPlayModeOptionsEnabled;var oldPlayOptions=EditorSettings.enterPlayModeOptions;
            var background=InputSystem.settings.backgroundBehavior;Keyboard keyboard=null;
            var editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;var inputMode=InputSystem.settings.updateMode;
            bool hadLanguage=PlayerPrefs.HasKey("language");int language=PlayerPrefs.GetInt("language",0);
            try
            {
                // Force the real static initializer: a duplicate key must fail this check.
                Require(Loc.T("SETTINGS")!=null,"Localization did not initialize.");
                var table=(System.Collections.IDictionary)typeof(Loc).GetField("Ru",Private).GetValue(null);
                Require(table.Count>300 && table.Contains("Suspicious Apple"),"Wrong or stale localization table.");
                report.AppendLine("Localization initializes with "+table.Count+" distinct keys, including Suspicious Apple.");
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                for(int session=0;session<2;session++)
                {
                    EditorApplication.EnterPlaymode();
                    for(int i=0;i<150 && !EditorApplication.isPlaying;i++)await Task.Delay(100);
                    Require(EditorApplication.isPlaying,"Play mode did not start.");await Task.Delay(600);
                    var menu=Object.FindFirstObjectByType<GameSettingsMenu>();
                    Require(menu && menu.Root!=null && menu.Root.Q<Button>("resume")!=null,"Settings did not finish building.");
                    Require(menu.Root.Q<Button>("bind-Crouch")!=null,"Controls list was truncated.");
                    Require(!GameSettingsMenu.IsOpen && !GameSettingsMenu.BlockGameplayInput && Time.timeScale==1,"Game starts paused or blocked.");
                    Require(menu.Root.style.display==DisplayStyle.None,"Settings are visible at startup.");
                    var player=Object.FindFirstObjectByType<FirstPersonController>();Require(player && player.enabled,"Player starts disabled.");
                    InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
                    if(keyboard==null){keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.EnableDevice(keyboard);keyboard.MakeCurrent();}
                    await Escape(keyboard);
                    Require(GameSettingsMenu.IsOpen && Time.timeScale==0 && !player.enabled,"ESC does not open pause.");
                    Require(menu.Root.style.display==DisplayStyle.Flex,"Open menu stays hidden.");
                    await Escape(keyboard);
                    Require(!GameSettingsMenu.IsOpen && Time.timeScale==1 && player.enabled && menu.Root.style.display==DisplayStyle.None,"ESC does not resume the game.");
                    await Escape(keyboard);
                    Submit(menu.Root.Q<Button>("resume"));await Task.Delay(100);
                    Require(!GameSettingsMenu.IsOpen && Time.timeScale==1 && player.enabled,"Resume button does not close pause.");
                    menu.Open();menu.BeginRebind(GameCommand.Crouch);await Escape(keyboard);
                    Require(GameSettingsMenu.IsOpen && !menu.IsCapturing,"ESC does not cancel only the key assignment.");
                    await Escape(keyboard);Require(GameSettingsMenu.IsOpen,"ESC from Controls closed the whole menu.");
                    await Escape(keyboard);Require(!GameSettingsMenu.IsOpen && Time.timeScale==1,"Controls cannot return to gameplay.");
                    menu.Open();Submit(menu.Root.Q<Button>("language"));await Task.Delay(100);
                    Require(menu.Root.Q<Button>("resume").text==Loc.T("RESUME"),"Language button broke the menu.");
                    menu.Close();
                    var pc=Object.FindFirstObjectByType<PcMonitorTerminal>();Require(pc,"PC monitor missing.");
                    pc.Focus(true);Require(pc.IsFocused,"PC focus failed.");await Escape(keyboard);
                    for(int i=0;i<25 && pc.IsFocused;i++)await Task.Delay(100);
                    Require(!pc.IsFocused,"The PC camera did not finish its exit transition.");
                    Require(!GameSettingsMenu.IsOpen && Time.timeScale==1,"Leaving the computer also opened pause.");
                    report.AppendLine("Play session "+(session+1)+": hidden and unpaused startup; real ESC opens/closes; Resume button, rebinding cancel, Controls back, EN/RU and PC Escape priority pass.");
                    if(keyboard!=null && keyboard.added){InputSystem.RemoveDevice(keyboard);keyboard=null;}
                    EditorApplication.ExitPlaymode();
                    for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);
                }
                passed=true;
            }
            catch(Exception e){report.AppendLine(e.ToString());Debug.LogException(e);}
            finally
            {
                if(keyboard!=null && keyboard.added)InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior=background;
                InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;InputSystem.settings.updateMode=inputMode;
                var menu=Object.FindFirstObjectByType<GameSettingsMenu>();if(menu && GameSettingsMenu.IsOpen)menu.Close();
                if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
                for(int i=0;i<100 && EditorApplication.isPlaying;i++)await Task.Delay(100);
                EditorSettings.enterPlayModeOptionsEnabled=oldOptions;EditorSettings.enterPlayModeOptions=oldPlayOptions;
                if(hadLanguage)PlayerPrefs.SetInt("language",language);else PlayerPrefs.DeleteKey("language");PlayerPrefs.Save();
                File.WriteAllText(Output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }
        static async Task Escape(Keyboard keyboard)
        {
            // Hidden batch editors do not route queued hardware events to the Game view.
            // Feed a manual Input System update, then run the same ordered consumers.
            await Task.Delay(100);
            InputSystem.Update();keyboard.MakeCurrent();
            InputState.Change(keyboard,new KeyboardState());InputSystem.Update();
            InputState.Change(keyboard,new KeyboardState(Key.Escape));
            Require(keyboard.escapeKey.wasPressedThisFrame,"ESC test input did not reach the Input System.");
            var menu=Object.FindFirstObjectByType<GameSettingsMenu>();
            typeof(GameSettingsMenu).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null);
            foreach(var pc in Object.FindObjectsByType<PcMonitorTerminal>(FindObjectsSortMode.None))
                if(pc.IsFocused)typeof(PcMonitorTerminal).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pc,null);
            InputSystem.Update();InputState.Change(keyboard,new KeyboardState());InputSystem.Update();await Task.Delay(150);
        }
        static void Submit(Button button)
        {
            Require(button!=null,"Button missing.");
            using var submit=NavigationSubmitEvent.GetPooled();submit.target=button;button.SendEvent(submit);
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
