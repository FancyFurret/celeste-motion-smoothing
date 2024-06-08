using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class AtDrawInputHandler
{
    private KeyboardState _oldKeyboardState;
    private MouseState _oldMouseState;
    private readonly GamePadState[] _oldGamePadStates = new GamePadState[4];

    public void UpdateInput()
    {
        _oldKeyboardState = MInput.Keyboard.CurrentState;
        _oldMouseState = MInput.Mouse.CurrentState;
        for (var i = 0; i < _oldGamePadStates.Length; i++)
            _oldGamePadStates[i] = MInput.GamePads[i].CurrentState;

        MInput.Keyboard.CurrentState = Keyboard.GetState();
        MInput.Mouse.CurrentState = Mouse.GetState();
        for (var i = 0; i < _oldGamePadStates.Length; i++)
            MInput.GamePads[i].CurrentState = GamePad.GetState((PlayerIndex)i);
    }

    public void ResetInput()
    {
        MInput.Keyboard.CurrentState = _oldKeyboardState;
        MInput.Mouse.CurrentState = _oldMouseState;
        for (var i = 0; i < _oldGamePadStates.Length; i++)
            MInput.GamePads[i].CurrentState = _oldGamePadStates[i];
    }

    public bool PressedThisUpdate(VirtualButton button)
    {
        if (button.Binding.Pressed(button.GamepadIndex, button.Threshold))
            return true;

        foreach (var node in button.Nodes)
            if (node.Pressed)
                return true;

        return false;
    }
}