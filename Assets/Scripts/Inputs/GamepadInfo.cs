using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

/// <summary>
/// Controller debugging aid: lists connected gamepads and lets the D-pad test the rumble motors.
/// It only runs in the editor and in development builds. In a release build it does nothing, so
/// pressing the D-pad on a menu can't rumble the controller and no debug text is drawn.
/// </summary>
public class GamepadInfo : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private string text = "";
    private int lastCount = -1;
    private Gamepad lastCurrent;

    private void Update()
    {
        Gamepad pad = Gamepad.current;

        // The text only changes when pads connect, disconnect or the current pad changes,
        // so build it then instead of every OnGUI call.
        if (Gamepad.all.Count != lastCount || pad != lastCurrent)
        {
            lastCount = Gamepad.all.Count;
            lastCurrent = pad;
            text = BuildText();
        }

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
        GUI.Label(new Rect(10, 10, 700, 400), text);
    }

    private static string BuildText()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.Append("Gamepads connected: ").Append(Gamepad.all.Count).Append('\n');

        foreach (Gamepad pad in Gamepad.all)
        {
            InputDeviceDescription d = pad.description;
            builder.Append('\n').Append(pad.displayName)
                   .Append("\n  C# type: ").Append(pad.GetType().Name)
                   .Append("\n  Layout: ").Append(pad.layout)
                   .Append("\n  Interface: ").Append(d.interfaceName)
                   .Append("\n  Product: ").Append(d.product)
                   .Append("\n  Is current: ").Append(pad == Gamepad.current).Append('\n');
        }

        builder.Append("\nD-pad Up = both motors, Left = low, Right = high, Down = reset");
        return builder.ToString();
    }
#endif
}