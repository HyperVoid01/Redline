using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

public class GamepadInfo : MonoBehaviour
{
    private void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null) return;

        // D-pad Up = both motors, Left = low motor only, Right = high motor only
        if (pad.dpad.up.isPressed)
            pad.SetMotorSpeeds(1f, 1f);
        else if (pad.dpad.left.isPressed)
            pad.SetMotorSpeeds(1f, 0f);
        else if (pad.dpad.right.isPressed)
            pad.SetMotorSpeeds(0f, 1f);
        else if (pad.dpad.down.wasPressedThisFrame)
            pad.ResetHaptics();
    }

    private void OnGUI()
    {
        string text = "Gamepads connected: " + Gamepad.all.Count + "\n";

        foreach (Gamepad pad in Gamepad.all)
        {
            InputDeviceDescription d = pad.description;
            text += "\n" + pad.displayName +
                    "\n  C# type: " + pad.GetType().Name +
                    "\n  Layout: " + pad.layout +
                    "\n  Interface: " + d.interfaceName +
                    "\n  Product: " + d.product +
                    "\n  Is current: " + (pad == Gamepad.current) + "\n";
        }

        text += "\nD-pad Up = both motors, Left = low, Right = high, Down = reset";

        GUI.Label(new Rect(10, 10, 700, 400), text);
    }
}