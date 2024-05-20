using System;
using System.Reflection;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Utilities;
using Celeste.Pico8;
using Monocle;
using MonoMod.ModInterop;

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

    private bool Hooked { get; set; }
    private DecoupledGameTick DecoupledGameTick { get; set; }
    private MotionSmoothingHandler MotionSmoothing { get; set; }
    private UpdateAtDraw UpdateAtDraw { get; set; }

    public override void Load()
    {
        typeof(GravityHelperImports).ModInterop();
        typeof(SpeedrunToolImports).ModInterop();

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
                if (Settings.TasMode)
                {
                    // For now, just draw at 60 as well. Motion smoothing in the Overworld looks awful at the moment.
                    DecoupledGameTick.SetTargetFramerate(60, 60);
                    MotionSmoothing.Enabled = false;
                }
                else
                {
                    DecoupledGameTick.SetTargetFramerate(Settings.FrameRate.ToFps(), Settings.FrameRate.ToFps());
                    MotionSmoothing.Enabled = false;
                }
            }

            Hook();
        }
        else
        {
            MotionSmoothing.Enabled = false;
            DecoupledGameTick.SetTargetFramerate(60, 60);
            Unhook();
        }
    }

    private void Hook()
    {
        if (Hooked) return;

        On.Monocle.Scene.Begin += SceneBeginHook;

        MotionSmoothing.Hook();
        UpdateAtDraw.Hook();
        DecoupledGameTick.Hook();

        Hooked = true;
    }

    private void Unhook()
    {
        if (!Hooked) return;

        On.Monocle.Scene.Begin -= SceneBeginHook;

        MotionSmoothing.Unhook();
        UpdateAtDraw.Unhook();
        DecoupledGameTick.Unhook();

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