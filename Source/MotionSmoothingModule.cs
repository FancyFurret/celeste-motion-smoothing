using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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
using MonoMod.RuntimeDetour;

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

	private bool _wasEnabled = false;

	// Hooks into unmaintained mods that don't have native MotionSmoothing support, disposed on
	// Unload. These live here (rather than inside HiresCameraSmoother) so they apply regardless of
	// the camera-smoothing setting -- see HookUnmaintainedMods.
	private readonly List<Hook> _unmaintainedModHooks = new();

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

    public IFrameUncapStrategy FrameUncapStrategy => Settings.FramerateIncreaseMethod switch
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
    private CrystalSpinnerFillerTracker CrystalSpinnerFillerTracker { get; } = new();
    private UpdateAtDraw UpdateAtDraw { get; } = new();
    private MotionSmoothingInputHandler InputHandler { get; } = new();
    private DebugRenderFix DebugRenderFix { get; } = new();
    private DeltaTimeFix DeltaTimeFix { get; } = new();

    public override void Load()
    {
		typeof(MotionSmoothingExports).ModInterop();

		HookUnmaintainedMods();


		// Normally we'd do this in Initialize, but SpeedrunTool
		// crashes on loading a state if we don't do it here.
		EverestModuleMetadata speedrunTool = new() {
			Name = "SpeedrunTool",
			Version = new Version(3, 22, 0)
		};

		if (Everest.Loader.DependencyLoaded(speedrunTool))
		{
			typeof(SpeedrunToolImports).ModInterop();
			SpeedrunToolImports.RegisterSaveLoadAction?.Invoke(null, SpeedrunToolAfterLoadState, null, null, SpeedrunToolBeforeLoadState, null);
		}

		

        DisableInliningPushSprite();

        UpdateEveryNTicks.Load();
        MotionSmoothing.Load();
        UnlockedCameraSmoother.Load();
        HiresCameraSmoother.Load();
        ActorPushTracker.Load();
        CrystalSpinnerFillerTracker.Load();
        UpdateAtDraw.Load();
        InputHandler.Load();
        DebugRenderFix.Load();
        DeltaTimeFix.Load();

        InputHandler.Enable();

        On.Monocle.Scene.Begin += SceneBeginHook;
        Everest.Events.Level.OnPause += LevelPause;
        Everest.Events.Level.OnUnpause += LevelUnpause;

        DisableMacOSVSync();
    }

    public override void Unload()
    {
        UpdateEveryNTicks.Unload();
        DecoupledGameTick.Unload();
        MotionSmoothing.Unload();
        UnlockedCameraSmoother.Unload();
        HiresCameraSmoother.Unload();
        ActorPushTracker.Unload();
        CrystalSpinnerFillerTracker.Unload();
        UpdateAtDraw.Unload();
        InputHandler.Unload();
        DebugRenderFix.Unload();
        DeltaTimeFix.Unload();

        On.Monocle.Scene.Begin -= SceneBeginHook;
        Everest.Events.Level.OnPause -= LevelPause;
        Everest.Events.Level.OnUnpause -= LevelUnpause;

        foreach (var hook in _unmaintainedModHooks)
            hook.Dispose();
        _unmaintainedModHooks.Clear();

        EnableMacOSVSync();
    }

	public override void Initialize()
	{
        CelesteTasInterop.Load();

		Settings.SillyMode = false;

		ApplySettings();
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
            UpdateEveryNTicks.Disable();
            DecoupledGameTick.Disable();

            // Reset to vanilla 60fps - must be done after disabling strategies
            // so the vanilla Game.Tick() uses the correct target elapsed time
            Engine.Instance.TargetElapsedTime = TimeSpan.FromTicks(166667);

            MotionSmoothing.Disable();
            UnlockedCameraSmoother.Disable();
            HiresCameraSmoother.Disable();
            ActorPushTracker.Disable();
            CrystalSpinnerFillerTracker.Disable();
            UpdateAtDraw.Disable();
            DebugRenderFix.Disable();
            DeltaTimeFix.Disable();
            return;
        }



        // If the game speed is modified, then we have to use dynamic mode
        if (Settings.FramerateIncreaseMethod == UpdateMode.Dynamic || Settings.GameSpeedModified)
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
            CrystalSpinnerFillerTracker.Disable();
            UpdateAtDraw.Disable();
            UnlockedCameraSmoother.Disable();
            HiresCameraSmoother.Disable();
            DebugRenderFix.Disable();
            DeltaTimeFix.Disable();
            return;
        }

        MotionSmoothing.Enable();
        ActorPushTracker.Enable();
        CrystalSpinnerFillerTracker.Enable();
        UpdateAtDraw.Enable();
        DebugRenderFix.Enable();
        DeltaTimeFix.Enable();

        if (Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires)
        {
            UnlockedCameraSmoother.Disable();
            HiresCameraSmoother.Enable();
            
            // We jettison the external large textures: we unconditionally use their large
            // versions, but if we disable rendering Madeline with subpixels, then some of them
            // will be too big for the new gameplay buffer size. There's really no other way to 
            // deal with that than to start over with them.
            HiresCameraSmoother.InitializeLargeTextures();

			HiresCameraSmoother.ZoomScale = Settings.HideStretchedEdges ? 181f / 180f : 1;
			HiresCameraSmoother.ZoomMatrix = Matrix.CreateScale(HiresCameraSmoother.ZoomScale);

			if (Settings.RenderMadelineWithSubpixels && !Settings.SillyMode)
			{
				HiresCameraSmoother.EnableHiresDistort();
			}

			else
			{
				HiresCameraSmoother.DisableHiresDistort();
			}

			if (!Settings.RenderMadelineWithSubpixels && !Settings.SillyMode)
			{
				HiresCameraSmoother.DisableLargeGameplayBuffer();
			}
        }

        else if (Settings.UnlockCameraStrategy == UnlockCameraStrategy.Unlock)
        {
            HiresCameraSmoother.Disable();
            UnlockedCameraSmoother.Enable();

			UnlockedCameraSmoother.ZoomScale = Settings.HideStretchedEdges ? 181f / 180f : 1;
			UnlockedCameraSmoother.ZoomMatrix = Matrix.CreateScale(UnlockedCameraSmoother.ZoomScale);
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



	// Mirrors HiresCameraSmoother.HookUnmaintainedMods (exact-version dependency checks so a hook
	// drops away once the mod ships its own support), but lives on the module itself rather than the
	// camera smoother. That means these hooks apply regardless of the camera-smoothing setting --
	// e.g. tying Hateline's hat to Madeline still matters under plain object smoothing with no
	// hires camera.
	private void HookUnmaintainedMods()
	{
		Version hatelineVersion = new Version(0, 2, 2);

		EverestModuleMetadata hateline = new() {
			Name = "Hateline",
			Version = hatelineVersion
		};

		// Check for exact version so we don't double-tie once Hateline ties the hat itself through
		// the MotionSmoothing interop (0.2.3+).
		if (Everest.Loader.TryGetDependency(hateline, out var hatelineModule))
		{
			if (hatelineModule.Metadata.Version.Equals(hatelineVersion))
			{
				AddHatelineHook();
			}
		}

		// VioletHelper's NRCB and Theo/jelly juggle indicators are standalone entities that draw a
		// marker above Madeline each frame from her position, so under motion smoothing they lag
		// behind her. Tie both to her smoothed position (the same "extra-jump dots" case the
		// PushSpriteSmoother tie path is built for). Gated on a minimum version -- a plain version
		// compare rather than TryGetDependency, since the latter also requires a matching major and
		// this is meant to keep applying as VioletHelper's major version climbs.
		Version violetHelperMinVersion = new Version(0, 1, 11);
		foreach (EverestModule module in Everest.Modules)
		{
			if (module.Metadata?.Name == "VioletHelper" && module.Metadata.Version >= violetHelperMinVersion)
			{
				AddVioletHelperHooks();
				break;
			}
		}
	}



	private static Type _t_HatComponent;

	private delegate void orig_HatelineResetHat(Player self);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddHatelineHook()
	{
		Type t_HatelineModule = Type.GetType("Celeste.Mod.Hateline.HatelineModule, Hateline");
		MethodInfo m_ResetHat = t_HatelineModule?.GetMethod(
			"ResetHat",
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
		);

		_t_HatComponent = Type.GetType("Celeste.Mod.Hateline.HatComponent, Hateline");

		if (m_ResetHat != null && _t_HatComponent != null)
		{
			_unmaintainedModHooks.Add(new Hook(m_ResetHat, HatelineResetHatHook));
		}
	}

	private static void HatelineResetHatHook(orig_HatelineResetHat orig, Player self)
	{
		orig(self);

		// Tie the freshly-(re)added hat to Madeline's smoothed position, mirroring what Hateline
		// 0.2.3+ does itself via MotionSmoothingExports.TieToPlayer. The tie is idempotent and
		// drops automatically when the component is collected, so re-running on every ResetHat is
		// safe.
		foreach (Component component in self.Components)
		{
			if (_t_HatComponent.IsInstanceOfType(component))
			{
				MotionSmoothingExports.TieToPlayer(component);
				break;
			}
		}
	}



	private delegate void orig_IndicatorUpdate(Entity self);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddVioletHelperHooks()
	{
		AddIndicatorTieHook(Type.GetType("Celeste.Mod.VioletHelper.Entities.NRCBIndicatorController, VioletHelper"));
		AddIndicatorTieHook(Type.GetType("Celeste.Mod.VioletHelper.TheoJellyJuggleIndicator, VioletHelper"));
	}

	private void AddIndicatorTieHook(Type indicatorType)
	{
		// The indicators override Update, so GetMethod resolves the override declared on the
		// indicator type -- hooking it fires only for that entity, not every Entity.Update.
		MethodInfo m_update = indicatorType?.GetMethod(
			"Update",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			null,
			Type.EmptyTypes,
			null
		);

		if (m_update != null)
		{
			_unmaintainedModHooks.Add(new Hook(m_update, IndicatorTieUpdateHook));
		}
	}

	private static void IndicatorTieUpdateHook(orig_IndicatorUpdate orig, Entity self)
	{
		orig(self);

		// Idempotent -- the tie is registered once and re-checked cheaply each frame, and drops
		// automatically when the entity is collected. Re-running every Update keeps it correct
		// across room reloads and whether the indicator was placed by a map or by VioletHelper.
		MotionSmoothingExports.TieToPlayer(self);
	}



	private void SpeedrunToolBeforeLoadState(Level level)
    {
		_wasEnabled = Settings.Enabled;
		Settings.Enabled = false;
    }

    private void SpeedrunToolAfterLoadState(Dictionary<Type, Dictionary<string, object>> savedValues, Level level)
    {
		Settings.Enabled = _wasEnabled;
        MotionSmoothing.SmoothAllObjects();
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

	public static Matrix GetLevelZoomMatrix()
	{
		switch (Settings.UnlockCameraStrategy)
		{
			case UnlockCameraStrategy.Hires:
				return HiresCameraSmoother.ZoomMatrix;

			case UnlockCameraStrategy.Unlock:
				return UnlockedCameraSmoother.ZoomMatrix;

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
		if (Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires && GameplayBuffers.Gameplay is VirtualRenderTarget)
		{
			HiresCameraSmoother.InitializeLargeTextures();
		}
	}

	public static float GetCurrentRenderTargetScale()
	{
		return HiresCameraSmoother.GetCurrentRenderTargetScale();
	}
}