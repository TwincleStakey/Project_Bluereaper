// 거점 interact key in one place (11-20 Kido: F talks to NPCs - change InteractKey only).
using UnityEngine.InputSystem;

namespace Hub
{
    public static class HubInput
    {
        public const Key InteractKey = Key.F;
        public static string InteractLabel => InteractKey.ToString();

        public static bool InteractPressed()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && kb[InteractKey].wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame);
        }
    }
}
