using System;
using System.Linq;
using Celeste.Mod.Entities;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public static class GameUtils
{
    public static readonly TimeSpan UpdateElapsedTime = new((long)Math.Round(10_000_000.0 / 60));
    public static readonly float UpdateElapsedSeconds = (float)UpdateElapsedTime.TotalSeconds;

    public static float CalculateDeltaTime(float rawDeltaTime)
    {
        return rawDeltaTime * Engine.TimeRate * Engine.TimeRateB *
               GetTimeRateComponentMultiplier(Engine.Instance.scene);
    }

    private static float GetTimeRateComponentMultiplier(Scene scene)
    {
        return scene == null
            ? 1f
            : scene.Tracker.GetComponents<TimeRateModifier>().Cast<TimeRateModifier>()
                .Where((Func<TimeRateModifier, bool>)(trm => trm.Enabled))
                .Select((Func<TimeRateModifier, float>)(trm => trm.Multiplier))
                .Aggregate(1f, (Func<float, float, float>)((acc, val) => acc * val));
    }
}