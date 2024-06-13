using System;
using System.Collections.Generic;
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

    public List<Action<bool>> EnabledActions { get; } = new();

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
    private DeltaTimeFix DeltaTimeFix { get; } = new();

    public override void Load()
    {
        typeof(MotionSmoothingExports).ModInterop();
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
        DeltaTimeFix.Load();

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
        DeltaTimeFix.Unload();

        On.Monocle.Scene.Begin -= SceneBeginHook;
    }

    public void ApplySettings()
    {
        if (MotionSmoothing == null) return;

        if (!Settings.Enabled)
        {
            UpdateEveryNTicks.Disable();
            DecoupledGameTick.Disable();

            MotionSmoothing.Disable();
            UnlockedCameraSmoother.Disable();
            ActorPushTracker.Disable();
            UpdateAtDraw.Disable();
            DebugRenderFix.Disable();
            DeltaTimeFix.Disable();
            return;
        }

        var inLevel = Engine.Scene is Level || Engine.Scene is LevelLoader ||
                      Engine.Scene is LevelExit || Engine.Scene is Emulator;

        // If the game speed is modified, then we have to use dynamic mode
        if (Settings.UpdateMode == UpdateMode.Dynamic || Settings.GameSpeedModified)
        {
            UpdateEveryNTicks.Disable();
            DecoupledGameTick.Enable();
        }
        else
        {
            DecoupledGameTick.Disable();
            UpdateEveryNTicks.Enable();
        }

        if (!inLevel)
        {
            // For TAS, just draw at 60 as well. Motion smoothing in the Overworld looks awful at the moment.
            // If we're not in a level, just use the target framerate
            var drawFps = Settings.TasMode ? 60 : Settings.FrameRate;
            var updateFps = (double)Settings.FrameRate;
            if (Settings.TasMode) updateFps = 60;
            else if (Settings.GameSpeedModified && !Settings.GameSpeedInLevelOnly) updateFps = Settings.GameSpeed;
            
            FrameUncapStrategy.SetTargetFramerate(updateFps, drawFps);
            MotionSmoothing.Disable();
            ActorPushTracker.Disable();
            UpdateAtDraw.Disable();
            UnlockedCameraSmoother.Disable();

            if (DecoupledGameTick.Enabled && (Settings.TasMode || !Settings.GameSpeedInLevelOnly))
                DecoupledGameTick.SetTargetDeltaTime(60);

            return;
        }

        // If we're in a level, keep the update rate at 60fps
        FrameUncapStrategy.SetTargetFramerate(Settings.GameSpeed, Settings.FrameRate);
        MotionSmoothing.Enable();
        ActorPushTracker.Enable();
        UpdateAtDraw.Enable();
        DebugRenderFix.Enable();
        DeltaTimeFix.Enable();

        if (DecoupledGameTick.Enabled)
            DecoupledGameTick.SetTargetDeltaTime(60);

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