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



            // Cycles the same three values it always has -- they're Rendering Mode's now that the
            // master toggle and the choice of renderer are one setting. The "enable smoothing
            // first" guard this used to carry is gone with them: Off is one of the three.
            else if (MotionSmoothingModule.Settings.ButtonChangeCameraSmoothingMode.Pressed)
            {
                // The setter refuses while a map is deciding either half of the mode, so say why
                // rather than leaving the hotkey looking broken.
                if (MotionSmoothingSettings.RenderingModeLocked)
                {
                    MotionSmoothingMessage.Show(
                        "motion_smoothing_unlock_strategy",
                        "Rendering Mode is set by this map",
                        y: 1020f
                    );

                    return;
                }

                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Cycling rendering mode");

                if (MotionSmoothingModule.Settings.RenderingMode == RenderingMode.Fancy)
                {
                    MotionSmoothingModule.Settings.RenderingMode = RenderingMode.Fast;
                }

                else if (MotionSmoothingModule.Settings.RenderingMode == RenderingMode.Fast)
                {
                    MotionSmoothingModule.Settings.RenderingMode = RenderingMode.Off;
                }

                else
                {
                    MotionSmoothingModule.Settings.RenderingMode = RenderingMode.Fancy;
                }

				var modeString = MotionSmoothingModule.Settings.RenderingMode == RenderingMode.Fancy
					? "Fancy"
					: MotionSmoothingModule.Settings.RenderingMode == RenderingMode.Fast
						? "Fast"
						: "Off";

                MotionSmoothingMessage.Show(
                    "motion_smoothing_unlock_strategy",
                    $"Rendering Mode: {modeString}",
                    y: 1020f
                );
            }
        }
    }
}