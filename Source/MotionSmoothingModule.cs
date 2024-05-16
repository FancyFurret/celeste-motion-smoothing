using System;
using System.Reflection;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Utilities;
using Celeste.Pico8;
using Monocle;

namespace Celeste.Mod.MotionSmoothing;

public class MotionSmoothingModule : EverestModule
{
    public const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static MotionSmoothingModule Instance { get; private set; }

    public override Type SettingsType => typeof(MotionSmoothingSettings);
    public static MotionSmoothingSettings Settings => (MotionSmoothingSettings)Instance._Settings;

    public MotionSmoothingModule()
    {
        Instance = this;
#if DEBUG
        // debug builds use verbose logging
        Logger.SetLogLevel(nameof(MotionSmoothingModule), LogLevel.Verbose);
#else
        // release builds use info logging to reduce spam in log files
        Logger.SetLogLevel(nameof(MotionSmoothingModule), LogLevel.Info);
#endif
    }

    public bool Hooked { get; private set; }
    public DecoupledGameTick DecoupledGameTick { get; private set; }
    public MotionSmoothingHandler MotionSmoothing { get; private set; }
    public UpdateAtDraw UpdateAtDraw { get; private set; }

    public override void Load()
    {
        DecoupledGameTick = new DecoupledGameTick();
        MotionSmoothing = new MotionSmoothingHandler();
        UpdateAtDraw = new UpdateAtDraw();

        MotionSmoothing.Load();
        ApplySmoothing();
    }

    public override void Unload()
    {
        MotionSmoothing.Unload();
        Unhook();
    }

    public void ToggleMod()
    {
        Settings.Enabled = !Settings.Enabled;
        ApplySmoothing();
    }

    public void ApplySmoothing()
    {
        if (MotionSmoothing == null) return;

        if (Settings.Enabled)
        {
            if (
                Engine.Scene is Level ||
                Engine.Scene is LevelLoader ||
                Engine.Scene is LevelExit ||
                Engine.Scene is Emulator)
            {
                // If we're in a level, keep the update rate at 60fps
                DecoupledGameTick.SetTargetFramerate(60, Settings.FrameRate.ToFps());
                MotionSmoothing.Enabled = true;
            }
            else
            {
                // If we're not in a level, just use the target framerate
                DecoupledGameTick.SetTargetFramerate(Settings.FrameRate.ToFps(), Settings.FrameRate.ToFps());
                MotionSmoothing.Enabled = false;
            }

            Hook();
        }
        else
        {
            Unhook();
        }
    }

    private void Hook()
    {
        if (Hooked) return;

        On.Monocle.Scene.Begin += SceneBeginHook;

        DecoupledGameTick.Hook();
        MotionSmoothing.Hook();
        UpdateAtDraw.Hook();

        Hooked = true;
    }

    private void Unhook()
    {
        if (!Hooked) return;

        On.Monocle.Scene.Begin -= SceneBeginHook;

        DecoupledGameTick.Unhook();
        MotionSmoothing.Unhook();
        UpdateAtDraw.Unhook();

        Hooked = false;
    }

    private static void SceneBeginHook(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        orig(self);

        var handler = self.Entities.FindFirst<MotionSmoothingInputHandler>();
        if (handler == null)
        {
            handler = new MotionSmoothingInputHandler();
            handler.Tag |= Tags.Persistent | Tags.Global;
            self.Add(handler);
        }
        else
        {
            handler.Active = true;
        }

        Instance.ApplySmoothing();
    }
}