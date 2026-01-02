using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Celeste.Mod.MotionSmoothing.FrameUncap;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Celeste.Mod.MotionSmoothing.Utilities;
using Celeste.Pico8;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    public bool InLevel => Engine.Scene is Level || Engine.Scene is LevelLoader ||
                           Engine.Scene is LevelExit || Engine.Scene is Emulator;

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
    private HiresCameraSmoother HiresCameraSmoother { get; } = new();
    private ActorPushTracker ActorPushTracker { get; } = new();
    private UpdateAtDraw UpdateAtDraw { get; } = new();
    private MotionSmoothingInputHandler InputHandler { get; } = new();
    private DebugRenderFix DebugRenderFix { get; } = new();
    private DeltaTimeFix DeltaTimeFix { get; } = new();

    public override void Load()
    {
        DisableInliningPushSprite();

        typeof(MotionSmoothingExports).ModInterop();
        typeof(GravityHelperImports).ModInterop();
        typeof(SpeedrunToolImports).ModInterop();
        CelesteTasInterop.Load();

        UpdateEveryNTicks.Load();
        MotionSmoothing.Load();
        UnlockedCameraSmoother.Load();
        HiresCameraSmoother.Load();
        ActorPushTracker.Load();
        UpdateAtDraw.Load();
        InputHandler.Load();
        DebugRenderFix.Load();
        DeltaTimeFix.Load();

        InputHandler.Enable();

        On.Monocle.Scene.Begin += SceneBeginHook;
        Everest.Events.Level.OnPause += LevelPause;
        Everest.Events.Level.OnUnpause += LevelUnpause;

        DisableMacOSVSync();

        ApplySettings();
    }

    public override void Unload()
    {
        UpdateEveryNTicks.Unload();
        MotionSmoothing.Unload();
        UnlockedCameraSmoother.Unload();
        HiresCameraSmoother.Unload();
        ActorPushTracker.Unload();
        UpdateAtDraw.Unload();
        InputHandler.Unload();
        DebugRenderFix.Unload();
        DeltaTimeFix.Unload();

        On.Monocle.Scene.Begin -= SceneBeginHook;
        Everest.Events.Level.OnPause -= LevelPause;
        Everest.Events.Level.OnUnpause -= LevelUnpause;

        EnableMacOSVSync();
    }

    public override void LoadContent(bool firstLoad)
    {
        base.LoadContent(firstLoad);
        if (firstLoad) Smoothing.Targets.HiresRenderer.Load();
    }

    public void ApplySettings()
    {
        if (MotionSmoothing == null) return;

        if (!Settings.Enabled)
        {
            ApplyFramerate();

            UpdateEveryNTicks.Disable();
            DecoupledGameTick.Disable();

            MotionSmoothing.Disable();
            UnlockedCameraSmoother.Disable();
            HiresCameraSmoother.Disable();
            ActorPushTracker.Disable();
            UpdateAtDraw.Disable();
            DebugRenderFix.Disable();
            DeltaTimeFix.Disable();
            return;
        }



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
        
        ApplyFramerate();

        if (!InLevel)
        {
            MotionSmoothing.Disable();
            ActorPushTracker.Disable();
            UpdateAtDraw.Disable();
            UnlockedCameraSmoother.Disable();
            HiresCameraSmoother.Disable();
            return;
        }

        MotionSmoothing.Enable();
        ActorPushTracker.Enable();
        UpdateAtDraw.Enable();
        DebugRenderFix.Enable();
        DeltaTimeFix.Enable();

        if (Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires)
        {
            UnlockedCameraSmoother.Disable();
            HiresCameraSmoother.Enable();

			if (Settings.RenderMadelineWithSubpixels)
			{
				HiresCameraSmoother.EnableHiresDistort();
			}

			else
			{
				HiresCameraSmoother.DisableHiresDistort();
				Smoothing.Targets.HiresRenderer.DisableLargeGameplayBuffer();
			}
        }

        else if (Settings.UnlockCameraStrategy == UnlockCameraStrategy.Unlock)
        {
            HiresCameraSmoother.Disable();
            UnlockedCameraSmoother.Enable();
        }
        
        else
        {
            UnlockedCameraSmoother.Disable();
            HiresCameraSmoother.Disable();
        }
    }

    private void ApplyFramerate()
    {
        int framerate = Settings.Enabled ? Settings.FrameRate : 60;

        var updateFps = 60.0;
        
        if (!InLevel)
        {
            // For TAS, just draw at 60 as well. Motion smoothing in the Overworld looks awful at the moment.
            // If we're not in a level, just use the target framerate
            var drawFps = Settings.TasMode ? 60 : framerate;
            updateFps = framerate;
            if (Settings.TasMode) updateFps = 60;
            else if (Settings.GameSpeedModified && !Settings.GameSpeedInLevelOnly) updateFps = Settings.GameSpeed;

            FrameUncapStrategy.SetTargetFramerate(updateFps, drawFps);
            if (DecoupledGameTick.Enabled && (Settings.TasMode || !Settings.GameSpeedInLevelOnly))
                DecoupledGameTick.SetTargetDeltaTime(60);
            return;
        }
        
        // If we're in a level, keep the update rate at 60fps
        var level = (Engine.Scene as Level)!;
        if (Settings.GameSpeedModified && !(level.Paused && Settings.GameSpeedInLevelOnly))
            updateFps = Settings.GameSpeed;
        
        FrameUncapStrategy.SetTargetFramerate(updateFps, framerate);
        if (DecoupledGameTick.Enabled)
            DecoupledGameTick.SetTargetDeltaTime(60);
    }

    private static void SceneBeginHook(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        orig(self);
        Instance.ApplySettings();
    }

    private static void LevelPause(Level level, int startIndex, bool minimal, bool quickReset)
    {
        Instance.ApplyFramerate();
    }
    
    private static void LevelUnpause(Level level)
    {
        Instance.ApplyFramerate();
    }


    // A fix for Madeline's hair being glitchy;
    // from Wartori's Mountain Tweaks, with permission. Thank you!
    private static void DisableInliningPushSprite()
    {
        Type t_SpriteBatch = typeof(SpriteBatch);

        MethodInfo m_PushSprite = t_SpriteBatch.GetMethod("PushSprite", BindingFlags.Instance | BindingFlags.NonPublic);
        if (m_PushSprite == null)
        {
            Logger.Log(LogLevel.Error, nameof(MotionSmoothingModule), $"Could not find method PushSprite in {nameof(SpriteBatch)}!");
            return;
        }

        MonoMod.Core.Platforms.PlatformTriple.Current.TryDisableInlining(m_PushSprite);
    }

    private static void DisableMacOSVSync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        Engine.Graphics.SynchronizeWithVerticalRetrace = false;
        Engine.Graphics.ApplyChanges();

        On.Monocle.Commands.Vsync += VsyncHook;
        On.Celeste.MenuOptions.SetVSync += SetVSyncHook;
    }

    private static void VsyncHook(On.Monocle.Commands.orig_Vsync orig, bool enabled)
    {
        if (!Settings.Enabled)
        {
            orig(enabled);
            return;
        }

        orig(false);
    }

    private static void SetVSyncHook(On.Celeste.MenuOptions.orig_SetVSync orig, bool on)
    {
        if (!Settings.Enabled)
        {
            orig(on);
            return;
        }

        orig(false);
    }

    private static void EnableMacOSVSync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        Engine.Graphics.SynchronizeWithVerticalRetrace = global::Celeste.Settings.Instance.VSync;
        Engine.Graphics.ApplyChanges();

        On.Monocle.Commands.Vsync -= VsyncHook;
        On.Celeste.MenuOptions.SetVSync -= SetVSyncHook;
    }



	public static Vector2 GetCameraOffset()
    {
        switch (Settings.UnlockCameraStrategy)
        {
            case UnlockCameraStrategy.Hires:
                return HiresCameraSmoother.GetCameraOffset();

            case UnlockCameraStrategy.Unlock:
                return UnlockedCameraSmoother.GetCameraOffset();

            case UnlockCameraStrategy.Off:
                return Vector2.Zero;
        }

		return Vector2.Zero;
    }

	private static Matrix ZoomedMatrix = Matrix.CreateScale(181f / 180f);

	public static Matrix GetLevelZoomMatrix()
	{
		switch (Settings.UnlockCameraStrategy)
		{
			case UnlockCameraStrategy.Hires:
				return ZoomedMatrix;

			case UnlockCameraStrategy.Unlock:
				return ZoomedMatrix;

			case UnlockCameraStrategy.Off:
				return Matrix.Identity;
		}

		return Matrix.Identity;
	}

	public static VirtualRenderTarget GetResizableBuffer(VirtualRenderTarget largeRenderTarget)
	{
		if (Smoothing.Targets.HiresRenderer.Instance is not { } renderer)
		{
			return largeRenderTarget;
		}

		var target = largeRenderTarget.Target;

        if (target == renderer.LargeGameplayBuffer.Target)
		{
			return Smoothing.Targets.HiresRenderer.OriginalGameplayBuffer;
		}

		if (target == renderer.LargeLevelBuffer.Target)
		{
			return Smoothing.Targets.HiresRenderer.OriginalLevelBuffer;
		}

		if (target == renderer.LargeTempABuffer.Target)
		{
			return Smoothing.Targets.HiresRenderer.OriginalTempABuffer;
		}

		if (target == renderer.LargeTempBBuffer.Target)
		{
			return Smoothing.Targets.HiresRenderer.OriginalTempBBuffer;
		}

		return largeRenderTarget;
	}

	public static void ReloadLargeTextures()
	{
		if (Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires)
		{
			HiresCameraSmoother.InitializeLargeTextures();
		}
	}
}