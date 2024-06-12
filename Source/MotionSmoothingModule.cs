using System;
using System.Reflection;
using Celeste.Mod.MotionSmoothing.FrameUncap;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
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

    public IFrameUncapStrategy FrameUncapStrategy => Settings.UpdateMode switch
    {
        UpdateMode.Interval => UpdateEveryNTicks,
        UpdateMode.Dynamic => DecoupledGameTick,
        _ => throw new ArgumentOutOfRangeException()
    };
    private UpdateEveryNTicks UpdateEveryNTicks { get; } = new();
    private DecoupledGameTick DecoupledGameTick { get; } = new();

    private MotionSmoothingHandler MotionSmoothing { get; } = new();
    private UnlockedCameraSmoother UnlockedCameraSmoother { get; } = new();
    private ActorPushTracker ActorPushTracker { get; } = new();
    private UpdateAtDraw UpdateAtDraw { get; } = new();
    private MotionSmoothingInputHandler InputHandler { get; } = new();
    private DebugRenderFix DebugRenderFix { get; } = new();

    public override void Load()
    {
        typeof(GravityHelperImports).ModInterop();
        typeof(SpeedrunToolImports).ModInterop();
        CelesteTasInterop.Load();

        UpdateEveryNTicks.Load();
        MotionSmoothing.Load();
        UnlockedCameraSmoother.Load();
        ActorPushTracker.Load();
        UpdateAtDraw.Load();
        InputHandler.Load();
        DebugRenderFix.Load();

        InputHandler.Enable();

        On.Monocle.Scene.Begin += SceneBeginHook;

        ApplySettings();
    }

    public override void Unload()
    {
        UpdateEveryNTicks.Unload();
        MotionSmoothing.Unload();
        UnlockedCameraSmoother.Unload();
        ActorPushTracker.Unload();
        UpdateAtDraw.Unload();
        InputHandler.Unload();
        DebugRenderFix.Unload();

        On.Monocle.Scene.Begin -= SceneBeginHook;
    }

    public void ToggleMod()
    {
        Settings.Enabled = !Settings.Enabled;
        ApplySettings();
    }

    public void ApplySettings()
    {
        if (MotionSmoothing == null) return;

        if (!Settings.Enabled)
        {
            UpdateEveryNTicks.SetTargetFramerate(60, 60);
            UpdateEveryNTicks.Disable();
            DecoupledGameTick.Disable();

            MotionSmoothing.Disable();
            UnlockedCameraSmoother.Disable();
            ActorPushTracker.Disable();
            UpdateAtDraw.Disable();
            DebugRenderFix.Disable();
            return;
        }

        if (Settings.UpdateMode == UpdateMode.Dynamic)
        {
            UpdateEveryNTicks.Disable();
            DecoupledGameTick.Enable();
        }
        else
        {
            DecoupledGameTick.Disable();
            UpdateEveryNTicks.Enable();
        }

        var inLevel = Engine.Scene is Level || Engine.Scene is LevelLoader ||
                      Engine.Scene is LevelExit || Engine.Scene is Emulator;
        if (!inLevel)
        {
            // For TAS, just draw at 60 as well. Motion smoothing in the Overworld looks awful at the moment.
            // If we're not in a level, just use the target framerate
            var fps = Settings.TasMode ? 60 : Settings.FrameRate;
            FrameUncapStrategy.SetTargetFramerate(fps, fps);
            MotionSmoothing.Disable();
            ActorPushTracker.Disable();
            UpdateAtDraw.Disable();
            UnlockedCameraSmoother.Disable();
            return;
        }

        // If we're in a level, keep the update rate at 60fps
        FrameUncapStrategy.SetTargetFramerate(60, Settings.FrameRate);
        MotionSmoothing.Enable();
        ActorPushTracker.Enable();
        UpdateAtDraw.Enable();
        DebugRenderFix.Enable();

        if (Settings.UnlockCamera)
            UnlockedCameraSmoother.Enable();
        else
            UnlockedCameraSmoother.Disable();
    }

    private static void SceneBeginHook(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        orig(self);
        Instance.ApplySettings();
    }
}