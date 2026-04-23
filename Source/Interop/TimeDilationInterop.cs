using Monocle;

namespace Celeste.Mod.MotionSmoothing.Interop;

public static class TimeDilationInterop
{
    // Clamp for EffectiveFactor so SecondsPerUpdate cannot blow up if the dilation factor
    // approaches zero (e.g. unexpected edge case at extreme player speeds).
    private const float MinFactor = 0.01f;

    // Set by TimeDilation via MotionSmoothingExports setters.
    internal static bool PushedTrueSlowdownEnabled;
    internal static float PushedDilationFactor = 1f;

    // Flips to true the first time TimeDilation pushes a value.
    public static bool Available { get; private set; }

    internal static void MarkAvailable() => Available = true;

    /// <summary>
    /// True when the user has the TrueSlowdown setting enabled. Cheap to call per frame.
    /// </summary>
    public static bool TrueSlowdownEnabled => Available && PushedTrueSlowdownEnabled;

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
    /// Last DilationFactor pushed by TimeDilation, or 1f if TimeDilation has never pushed.
    /// </summary>
    public static float CurrentDilationFactor => Available ? PushedDilationFactor : 1f;

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
}
