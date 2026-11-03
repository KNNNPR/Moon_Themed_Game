using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // WASD to drive, E to interact, Escape to drop the link back to the PC.
    // Polled directly, same as PcMonitorTerminal, so no action asset is needed.
    public static class ScavengerInput
    {
        public static Vector2 Move
        {
            get
            {
                var move = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
                var keys = Keyboard.current;
                if (keys == null) return move;
                if (keys.wKey.isPressed) move.y += 1;
                if (keys.sKey.isPressed) move.y -= 1;
                if (keys.dKey.isPressed) move.x += 1;
                if (keys.aKey.isPressed) move.x -= 1;
#elif ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKey(KeyCode.W)) move.y += 1;
                if (Input.GetKey(KeyCode.S)) move.y -= 1;
                if (Input.GetKey(KeyCode.D)) move.x += 1;
                if (Input.GetKey(KeyCode.A)) move.x -= 1;
#endif
                return move;
            }
        }

        public static bool Interact => Pressed(KeyId.E);
        public static bool Exit => Pressed(KeyId.Escape);
        public static bool Click
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButtonDown(0);
#else
                return false;
#endif
            }
        }

        public enum KeyId { E, Escape }

        public static bool Pressed(KeyId key)
        {
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return false;
            return key switch
            {
                KeyId.E => keys.eKey.wasPressedThisFrame,
                KeyId.Escape => keys.escapeKey.wasPressedThisFrame,
                _ => false
            };
#elif ENABLE_LEGACY_INPUT_MANAGER
            return key switch
            {
                KeyId.E => Input.GetKeyDown(KeyCode.E),
                KeyId.Escape => Input.GetKeyDown(KeyCode.Escape),
                _ => false
            };
#else
            return false;
#endif
        }
    }
}
