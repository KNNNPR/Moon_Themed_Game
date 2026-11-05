using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonThemedGame.Interactions
{
    public enum GameCommand { Forward, Backward, Left, Right, Interact, Throw, Repair, VisualStyle, RadioPrevious, RadioNext, RadioPlay, RadioOff, RobotPanel, SatelliteOne, SatelliteTwo, SatelliteThree, Menu, RobotCameraReset, RobotHandbrake, RobotRecover }

    public static class GameControls
    {
        static readonly Key[] defaults = { Key.W,Key.S,Key.A,Key.D,Key.E,Key.G,Key.F,Key.F6,Key.LeftArrow,Key.RightArrow,Key.UpArrow,Key.DownArrow,Key.H,Key.Digit1,Key.Digit2,Key.Digit3,Key.Escape,Key.C,Key.Space,Key.R };
        static readonly string[] names = { "Move forward", "Move backward", "Move left", "Move right", "Interact / take", "Throw object", "Repair robot", "Retro / ASCII", "Previous station", "Next station", "Radio on", "Radio off", "Robot tuner", "Satellite 1", "Satellite 2", "Satellite 3", "Open settings", "Reset robot camera", "Robot handbrake", "Right overturned robot" };
        static Key[] keys;
        public static event Action Changed;
        static void Load()
        {
            if (keys != null) return;
            keys = (Key[])defaults.Clone();
            for (int i=0;i<keys.Length;i++)
            {
                int saved = PlayerPrefs.GetInt("controls."+(GameCommand)i, (int)defaults[i]);
                if (Enum.IsDefined(typeof(Key), saved) && saved != (int)Key.None) keys[i]=(Key)saved;
            }
            // New defaults must not steal an existing custom binding from an older save.
            foreach(var command in new[]{GameCommand.RobotCameraReset,GameCommand.RobotHandbrake,GameCommand.RobotRecover})
                if(!PlayerPrefs.HasKey("controls."+command) && Occupied(keys[(int)command],command))
                    foreach(Key candidate in Enum.GetValues(typeof(Key)))
                        if(candidate!=Key.None && candidate!=Key.Escape && !Occupied(candidate,command)) {keys[(int)command]=candidate;break;}
        }
        static bool Occupied(Key key,GameCommand except)
        { for(int i=0;i<keys.Length;i++)if(i!=(int)except && keys[i]==key)return true;return false; }
        public static string Name(GameCommand command) => Loc.T(names[(int)command]);
        public static Key Binding(GameCommand command) { Load(); return keys[(int)command]; }
        public static string Label(GameCommand command)
        {
            var key=Binding(command);
            string value=Keyboard.current != null ? Keyboard.current[key].displayName : key.ToString();
            return string.IsNullOrEmpty(value) ? key.ToString().ToUpperInvariant() : value.ToUpperInvariant();
        }
        public static bool Pressed(GameCommand command) => Keyboard.current != null && Keyboard.current[Binding(command)].wasPressedThisFrame;
        public static bool Held(GameCommand command) => Keyboard.current != null && Keyboard.current[Binding(command)].isPressed;
        public static Vector2 Movement => Vector2.ClampMagnitude(new Vector2((Held(GameCommand.Right)?1:0)-(Held(GameCommand.Left)?1:0), (Held(GameCommand.Forward)?1:0)-(Held(GameCommand.Backward)?1:0)),1);
        public static bool SetBinding(GameCommand command, Key key, out string message)
        {
            Load();
            if (key == Key.None || key == Key.Escape) { message=Loc.T("ESC is reserved for back / cancel."); return false; }
            for(int i=0;i<keys.Length;i++) if (i!=(int)command && keys[i]==key)
            { message=Loc.F("Already assigned: {0}.", Name((GameCommand)i)); return false; }
            keys[(int)command]=key; PlayerPrefs.SetInt("controls."+command,(int)key);
            PlayerPrefs.Save(); Changed?.Invoke(); message=Loc.F("Saved: {0}  [{1}]", Name(command), Label(command)); return true;
        }
        public static void ResetBindings()
        {
            keys=(Key[])defaults.Clone();
            foreach(GameCommand command in Enum.GetValues(typeof(GameCommand))) PlayerPrefs.DeleteKey("controls."+command);
            PlayerPrefs.Save(); Changed?.Invoke();
        }
        public static string FormatPrompt(string prompt) => prompt.Replace("E —",Label(GameCommand.Interact)+" —").Replace("G —",Label(GameCommand.Throw)+" —")
            .Replace("[E]","["+Label(GameCommand.Interact)+"]").Replace("[H]","["+Label(GameCommand.RobotPanel)+"]")
            .Replace("WASD - DRIVE",Label(GameCommand.Forward)+"/"+Label(GameCommand.Left)+"/"+Label(GameCommand.Backward)+"/"+Label(GameCommand.Right)+" - DRIVE")
            .Replace("1/2/3 - SATELLITE",Label(GameCommand.SatelliteOne)+"/"+Label(GameCommand.SatelliteTwo)+"/"+Label(GameCommand.SatelliteThree)+" - SATELLITE")
            .Replace(" H - TUNER"," ["+Label(GameCommand.RobotPanel)+"] - TUNER");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetSession() { keys=null; Changed=null; }
    }
}
