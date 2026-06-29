using Celeste.Mod.MotionSmoothing.Utilities;
using Monocle;

namespace Celeste.Mod.MotionSmoothing;

public class MotionSmoothingInputHandler : ToggleableFeature<MotionSmoothingInputHandler>
{
    public override void Load()
    {
        base.Load();
        On.Monocle.Scene.Begin += SceneBeginHook;
    }

    public override void Unload()
    {
        base.Unload();
        On.Monocle.Scene.Begin -= SceneBeginHook;
    }

    private static void SceneBeginHook(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        orig(self);

        var handler = self.Entities.FindFirst<MotionSmoothingInputHandlerEntity>();
        if (handler == null)
        {
            handler = new MotionSmoothingInputHandlerEntity();
            handler.Tag |= Tags.Persistent | Tags.Global;
            self.Add(handler);
        }
        else
        {
            handler.Active = true;
        }
    }

    private class MotionSmoothingInputHandlerEntity : Entity
    {
        public override void Update()
        {
            base.Update();

            if (MotionSmoothingModule.Settings.ButtonToggleMotionSmoothingEnabled.Pressed)
            {
                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling motion smoothing");
                MotionSmoothingModule.Instance.CurrentEnabled = !MotionSmoothingModule.Instance.CurrentEnabled;

                MotionSmoothingMessage.Show(
                    "motion_smoothing_enabled",
                    MotionSmoothingModule.Instance.CurrentEnabled ? "Motion Smoothing Enabled" : "Motion Smoothing Disabled",
                    y: 980f
                );
            }



            else if (MotionSmoothingModule.Settings.ButtonChangeCameraSmoothingMode.Pressed)
            {
                if (!MotionSmoothingModule.Instance.CurrentEnabled)
                {
                    return;
                }

                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling unlock strategy");

                if (MotionSmoothingModule.Instance.CurrentUnlockCameraStrategy == UnlockCameraStrategy.Hires)
                {
                    MotionSmoothingModule.Instance.CurrentUnlockCameraStrategy = UnlockCameraStrategy.Unlock;
                }

                else if (MotionSmoothingModule.Instance.CurrentUnlockCameraStrategy == UnlockCameraStrategy.Unlock)
                {
                    MotionSmoothingModule.Instance.CurrentUnlockCameraStrategy = UnlockCameraStrategy.Off;
                }

                else
                {
                    MotionSmoothingModule.Instance.CurrentUnlockCameraStrategy = UnlockCameraStrategy.Hires;
                }

				var strategyString = MotionSmoothingModule.Instance.CurrentUnlockCameraStrategy == UnlockCameraStrategy.Hires
					? "Fancy"
					: MotionSmoothingModule.Instance.CurrentUnlockCameraStrategy == UnlockCameraStrategy.Unlock
						? "Fast"
						: "Off";

                MotionSmoothingMessage.Show(
                    "motion_smoothing_unlock_strategy",
                    $"Camera Smoothing: {strategyString}",
                    y: 1020f
                );
            }
        }
    }
}