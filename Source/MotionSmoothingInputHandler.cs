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
            if (MotionSmoothingModule.Settings.ButtonToggleSmoothing.Pressed)
            {
                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling motion smoothing");
                MotionSmoothingModule.Settings.Enabled = !MotionSmoothingModule.Settings.Enabled;
            }
            if (MotionSmoothingModule.Settings.ButtonToggleUnlockedCamera.Pressed)
            {
                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling unlocked camera");
                MotionSmoothingModule.Settings.UnlockCamera = !MotionSmoothingModule.Settings.UnlockCamera;
            }
        }
    }
}