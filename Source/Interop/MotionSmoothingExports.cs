// ReSharper disable UnusedMember.Global

using System;
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
}