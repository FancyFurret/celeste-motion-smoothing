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
        private Alarm _enabledMessageAlarm;
        private Alarm _unlockStrategyMessageAlarm;

        public override void Update()
        {
            base.Update();

            if (MotionSmoothingModule.Settings.ButtonToggleSmoothing.Pressed)
            {
                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling motion smoothing");
                MotionSmoothingModule.Settings.Enabled = !MotionSmoothingModule.Settings.Enabled;



                if (_enabledMessageAlarm is Alarm alarm)
                {
                    Engine.Commands.ExecuteCommand("hide_message", new string[] { "motion_smoothing_enabled" });
                    alarm.RemoveSelf();
                }

                Engine.Commands.ExecuteCommand("display_message", new string[] {
                    "motion_smoothing_enabled", // id
                    "0.5", // scale
                    "980", // y
                    "true",
                    MotionSmoothingModule.Settings.Enabled ? "[Motion Smoothing] Enabled" : "[Motion Smoothing] Disabled"
                });

                Entity dummy = new Entity();
                (Engine.Scene as Level)?.Add(dummy);
                _enabledMessageAlarm = Alarm.Set(dummy, 1f, () =>
                {
                    Engine.Commands.ExecuteCommand("hide_message", new string[] { "motion_smoothing_enabled" });
                    Engine.Commands.ExecuteCommand("hide_message", new string[] { "motion_smoothing_unlock_strategy" });
                    _enabledMessageAlarm.RemoveSelf();
                });
            }



            else if (MotionSmoothingModule.Settings.ButtonToggleUnlockStrategy.Pressed)
            {
                if (!MotionSmoothingModule.Settings.Enabled)
                {
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



                if (_unlockStrategyMessageAlarm is Alarm alarm)
                {
                    Engine.Commands.ExecuteCommand("hide_message", new string[] { "motion_smoothing_unlock_strategy" });
                    alarm.RemoveSelf();
                }

				var strategyString = MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires
					? "Fancy"
					: MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Unlock
						? "Fast"
						: "Off";

                Engine.Commands.ExecuteCommand("display_message", new string[] {
                    "motion_smoothing_unlock_strategy", // id
                    "0.5", // scale
                    "1020", // y
                    "true",
                    $"[Motion Smoothing] Smooth Camera: {strategyString}"
                });

                Entity dummy = new Entity();
                (Engine.Scene as Level)?.Add(dummy);
                _unlockStrategyMessageAlarm = Alarm.Set(dummy, 1f, () =>
                {
                    Engine.Commands.ExecuteCommand("hide_message", new string[] { "motion_smoothing_enabled" });
                    Engine.Commands.ExecuteCommand("hide_message", new string[] { "motion_smoothing_unlock_strategy" });
                    _unlockStrategyMessageAlarm.RemoveSelf();
                });
            }
        }
    }
}