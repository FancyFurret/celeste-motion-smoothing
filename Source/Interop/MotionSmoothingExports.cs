// ReSharper disable UnusedMember.Global

using System;
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
}