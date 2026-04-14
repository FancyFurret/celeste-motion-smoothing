using System;
using System.Reflection;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Interop;

public static class TimeDilationInterop
{
    // Clamp for EffectiveFactor so SecondsPerUpdate cannot blow up if the dilation factor
    // approaches zero (e.g. unexpected edge case at extreme player speeds).
    private const float MinFactor = 0.01f;

    public static bool Available { get; private set; }

    private static Func<bool> _getTrueSlowdown;
    private static Type _componentType;
    private static MethodInfo _dilationFactorGetter;

    /// <summary>
    /// True when the user has the TrueSlowdown setting enabled. Cheap to call per frame.
    /// </summary>
    public static bool TrueSlowdownEnabled => Available && (_getTrueSlowdown?.Invoke() ?? false);

    /// <summary>
    /// Mirrors the guard in TimeDilation's TrueSlowDown.GetTimeMultiplier: only active in an
    /// unpaused Level with a player in control and the debug console closed.
    /// </summary>
    public static bool IsActive
    {
        get
        {
            if (!TrueSlowdownEnabled) return false;
            if (Engine.Scene is not Level { Paused: false } level) return false;
            var player = level.Tracker.GetEntity<Player>();
            if (player == null || !player.InControl) return false;
            if (Engine.Commands != null && Engine.Commands.Open) return false;
            return true;
        }
    }

    /// <summary>
    /// Current DilationFactor of the player's TimeDilationComponent, or 1f if not available.
    /// </summary>
    public static float CurrentDilationFactor
    {
        get
        {
            if (!Available || _componentType == null || _dilationFactorGetter == null) return 1f;
            if (Engine.Scene is not Level level) return 1f;
            var player = level.Tracker.GetEntity<Player>();
            if (player == null) return 1f;
            foreach (var component in player.Components)
            {
                if (_componentType.IsInstanceOfType(component))
                    return (float)_dilationFactorGetter.Invoke(component, null);
            }
            return 1f;
        }
    }

    /// <summary>
    /// Returns 1f when slowdown is inactive (fast path); otherwise the current dilation factor
    /// clamped to [MinFactor, +inf).
    /// </summary>
    public static float EffectiveFactor
    {
        get
        {
            if (!IsActive) return 1f;
            var factor = CurrentDilationFactor;
            return factor < MinFactor ? MinFactor : factor;
        }
    }

    public static void Load()
    {
        var metadata = new EverestModuleMetadata
        {
            Name = "TimeDilation",
            Version = new Version(1, 0, 0)
        };

        if (!Everest.Loader.TryGetDependency(metadata, out var module)) return;

        var assembly = module.GetType().Assembly;

        var moduleType = assembly.GetType("Celeste.Mod.TimeDilation.TimeDilationModule", false);
        var settingsType = assembly.GetType("Celeste.Mod.TimeDilation.TimeDilationModuleSettings", false);
        var componentType = assembly.GetType("Celeste.Mod.TimeDilation.TimeDilationComponent", false);
        if (moduleType == null || settingsType == null || componentType == null) return;

        var settingsProp = moduleType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
        var trueSlowdownProp = settingsType.GetProperty("TrueSlowdown");
        var dilationFactorProp = componentType.GetProperty("DilationFactor");
        if (settingsProp == null || trueSlowdownProp == null || dilationFactorProp == null) return;

        var settingsGetter = settingsProp.GetGetMethod();
        var trueSlowdownGetter = trueSlowdownProp.GetGetMethod();
        var dilationFactorGetter = dilationFactorProp.GetGetMethod();
        if (settingsGetter == null || trueSlowdownGetter == null || dilationFactorGetter == null) return;

        _getTrueSlowdown = () =>
        {
            var settings = settingsGetter.Invoke(null, null);
            return settings != null && (bool)trueSlowdownGetter.Invoke(settings, null);
        };

        _componentType = componentType;
        _dilationFactorGetter = dilationFactorGetter;
        Available = true;
    }
}
