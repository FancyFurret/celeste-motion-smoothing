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
	// cleanly.
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

	// [1.5.6+]
	// Returns the scale factor of the current render target, relative to its vanilla size:
	// 1 normally, or the hires scale, usually 6, when a backdrop or effect is being rendered
	// into an upscaled buffer. Mods that draw through a custom shader with their own projection
	// matrix, which bypasses the SpriteBatch transform that Motion Smoothing automatically scales,
	// should divide the viewport dimensions they feed into that projection by this value, so that
	// their quad fills the whole upscaled target instead of a 1/scale corner.
	public static float GetCurrentRenderTargetScale()
	{
		return MotionSmoothingModule.GetCurrentRenderTargetScale();
	}

	// [1.5.6+]
	// Ties a component's rendering to Madeline's smoothed position, so any attachment that anchors
	// itself to her (e.g. a hat, extra jump indicators) stays glued to her under both object smoothing
	// and subpixel rendering. Call this once when the component is created; the tie is dropped
	// automatically when the component is collected or manually with UntieFromPlayer.
	public static void TieToPlayer(Component component)
	{
		Smoothing.Strategies.PushSpriteSmoother.Instance?.TieToPlayer(component);
	}

	// [1.5.6+]
	// Entity overload, for a standalone entity that draws itself relative to Madeline (e.g.
	// extra-jump dots above her head) rather than a component parented to her.
	public static void TieToPlayer(Entity entity)
	{
		Smoothing.Strategies.PushSpriteSmoother.Instance?.TieToPlayer(entity);
	}

	// [1.5.6+]
	// Removes a tie previously created with TieToPlayer.
	public static void UntieFromPlayer(Component component)
	{
		Smoothing.Strategies.PushSpriteSmoother.Instance?.UntieFromPlayer(component);
	}

	public static void UntieFromPlayer(Entity entity)
	{
		Smoothing.Strategies.PushSpriteSmoother.Instance?.UntieFromPlayer(entity);
	}
}