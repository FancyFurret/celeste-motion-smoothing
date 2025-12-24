// ReSharper disable UnusedMember.Global

using System;
using Microsoft.Xna.Framework;
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

	// Returns the fractional camera offset in [0, 1) for camera smoothing.
	// Available in 1.3.1+.
	public static Vector2 GetFractionalCameraOffset()
    {
        return MotionSmoothingModule.GetCameraOffset();
    }
	
	// Returns the camera zoom matrix (typically a 181/180 scale) for camera smoothing.
	// Available in 1.3.1+.
	public static Matrix GetLevelZoomMatrix()
    {
        return MotionSmoothingModule.GetLevelZoomMatrix();
    }
}