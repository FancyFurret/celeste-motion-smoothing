using Celeste.Mod.MotionSmoothing.Maps;
using Celeste.Mod.MotionSmoothing.Utilities;
using Monocle;

namespace Celeste.Mod.MotionSmoothing;

public class MotionSmoothingInputHandler : ToggleableFeature<MotionSmoothingInputHandler>
{
    public override void Load()
    {
        base.Load();
        MotionSmoothingModule.DisableInlining(typeof(Scene), "Begin");
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
                // The setter refuses while a map is deciding this, so say why rather than
                // leaving the hotkey looking broken.
                if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.Enabled))
                {
                    MotionSmoothingMessage.Show(
                        "motion_smoothing_enabled",
                        "Motion Smoothing is set by this map",
                        y: 980f
                    );

                    return;
                }

                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling motion smoothing");
                MotionSmoothingModule.Settings.Enabled = !MotionSmoothingModule.Settings.Enabled;

                MotionSmoothingMessage.Show(
                    "motion_smoothing_enabled",
                    MotionSmoothingModule.Settings.Enabled ? "Motion Smoothing Enabled" : "Motion Smoothing Disabled",
                    y: 980f
                );
            }



            else if (MotionSmoothingModule.Settings.ButtonChangeCameraSmoothingMode.Pressed)
            {
                // Covers every way smoothing can be off: the player's own setting, a map deciding
                // it, and SpeedrunTool's state restore. Checked before the lock below because
                // it's the more actionable of the two -- and a map that turns smoothing off can't
                // also be deciding Camera Smoothing, so the two never both apply.
                if (!MotionSmoothingModule.Settings.Enabled)
                {
                    MotionSmoothingMessage.Show(
                        "motion_smoothing_unlock_strategy",
                        "Enable Motion Smoothing to change Camera Smoothing",
                        y: 1020f
                    );

                    return;
                }

                if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.CameraSmoothingMode))
                {
                    MotionSmoothingMessage.Show(
                        "motion_smoothing_unlock_strategy",
                        "Camera Smoothing is set by this map",
                        y: 1020f
                    );

                    return;
                }

                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling unlock strategy");

                if (MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires)
                {
                    MotionSmoothingModule.Settings.UnlockCameraStrategy = UnlockCameraStrategy.Unlock;
                }

                else if (MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Unlock)
                {
                    MotionSmoothingModule.Settings.UnlockCameraStrategy = UnlockCameraStrategy.Off;
                }

                else
                {
                    MotionSmoothingModule.Settings.UnlockCameraStrategy = UnlockCameraStrategy.Hires;
                }

				var strategyString = MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires
					? "Fancy"
					: MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Unlock
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