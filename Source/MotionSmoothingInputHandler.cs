using Monocle;

namespace Celeste.Mod.MotionSmoothing;

public class MotionSmoothingInputHandler : Entity
{
    public override void Update()
    {
        base.Update();

        if (MotionSmoothingModule.Settings.ButtonToggleSmoothing.Pressed)
        {
            Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling motion smoothing");
            MotionSmoothingModule.Instance.ToggleMod();
        }
    }
}