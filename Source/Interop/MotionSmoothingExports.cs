// ReSharper disable UnusedMember.Global

using System;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.ModInterop;

namespace Celeste.Mod.MotionSmoothing.Interop;

[ModExportName("MotionSmoothing")]
public static class MotionSmoothingExports
{
    public static void SetTargetGameSpeed(double speed, bool inLevelOnly)
    {
        MotionSmoothingModule.Settings.GameSpeed = speed;
        MotionSmoothingModule.Settings.GameSpeedInLevelOnly = inLevelOnly;
    }

    public static void RegisterEnabledAction(Action<bool> action)
    {
        MotionSmoothingModule.Instance.EnabledActions.Add(action);
    }

	// [1.3.1+]
	// Returns the fractional camera offset in [0, 1) for camera smoothing.
	public static Vector2 GetFractionalCameraOffset()
    {
        return MotionSmoothingModule.GetCameraOffset();
    }
	
	// [1.3.1+]
	// Returns the camera zoom matrix (typically a 181/180 scale) for camera smoothing.
	public static Matrix GetLevelZoomMatrix()
    {
        return MotionSmoothingModule.GetLevelZoomMatrix();
    }

	// [1.3.2+]
	// When passed a VirtualRenderTarget to one of our internal large buffers,
	// returns the corresponding small large render target. Otherwise, returns
	// the input.
	public static VirtualRenderTarget GetResizableBuffer(VirtualRenderTarget largeRenderTarget)
    {
		return MotionSmoothingModule.GetResizableBuffer(largeRenderTarget);
    }

	// [1.3.2+]
	// Recreates the large texture data. This is necessary if you've called
	// VirtualRenderTarget.Reload() on the Gameplay, Level, TempA, or TempB
	// buffers (e.g. ExCameraDynamics).
	public static void ReloadLargeTextures()
	{
		MotionSmoothingModule.ReloadLargeTextures();
	}

	// [1.5.4+]
	// Disables all object smoothing (position interpolation and push-sprite offsets)
	// for the given entity. Useful for entities whose rendering doesn't interpolate
	// cleanly. Idempotent — safe to call every frame. Pair with ReenableInterpolation
	// to restore smoothing.
	public static void DisableObjectSmoothing(Entity entity)
	{
		if (entity.Get<NoInterpolateComponent>() is null)
			entity.Add(new NoInterpolateComponent());
	}

	// [1.5.4+]
	// Re-enables object smoothing for an entity previously passed to DisableObjectSmoothing.
	public static void ReenableObjectSmoothing(Entity entity)
	{
		entity.Components.RemoveAll<NoInterpolateComponent>();
	}
}