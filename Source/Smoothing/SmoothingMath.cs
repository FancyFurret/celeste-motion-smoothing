using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public static class SmoothingMath
{
    public static float SecondsPerUpdate => (float)(1f / MotionSmoothingModule.Settings.GameSpeed);

    public static float Smooth(float[] history, double elapsedSeconds, SmoothingMode mode)
    {
        return mode switch
        {
            SmoothingMode.Interpolate => Interpolate(history, elapsedSeconds),
            SmoothingMode.Extrapolate => Extrapolate(history, elapsedSeconds),
            _ => history[0]
        };
    }

    public static float SmoothAngle(float[] history, double elapsedSeconds, SmoothingMode mode)
    {
        return mode switch
        {
            SmoothingMode.Interpolate => InterpolateAngle(history, elapsedSeconds),
            SmoothingMode.Extrapolate => ExtrapolateAngle(history, elapsedSeconds),
            _ => history[0]
        };
    }

    public static Vector2 Smooth(Vector2[] positionHistory, double elapsedSeconds, SmoothingMode mode)
    {
        return mode switch
        {
            SmoothingMode.Interpolate => Interpolate(positionHistory, elapsedSeconds),
            SmoothingMode.Extrapolate => Extrapolate(positionHistory, elapsedSeconds),
            _ => positionHistory[0]
        };
    }

    public static float Interpolate(float[] history, double elapsedSeconds)
    {
        var t = (float)(elapsedSeconds / SecondsPerUpdate);
        return MathHelper.Lerp(history[1], history[0], t);
    }

    public static float InterpolateAngle(float[] history, double elapsedSeconds)
    {
        var t = (float)(elapsedSeconds / SecondsPerUpdate);
        return Calc.AngleLerp(history[1], history[0], t);
    }

    public static Vector2 Interpolate(Vector2[] positionHistory, double elapsedSeconds)
    {
        var t = (float)(elapsedSeconds / SecondsPerUpdate);
        return Vector2.Lerp(positionHistory[1], positionHistory[0], t);
    }

    public static float Extrapolate(float[] history, double elapsedSeconds)
    {
        var speed = (history[0] - history[1]) / SecondsPerUpdate;
        #pragma warning disable CS0618
        return history[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds;
        #pragma warning restore CS0618
    }

    public static float ExtrapolateAngle(float[] history, double elapsedSeconds)
    {
        var speed = Calc.AngleDiff(history[1], history[0]) / SecondsPerUpdate;
        #pragma warning disable CS0618
        return Calc.WrapAngle(history[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds);
        #pragma warning restore CS0618
    }

    /// <summary>
    /// Threshold for position delta below which we consider the entity stationary.
    /// This prevents jitter from floating-point noise when entities are not moving.
    /// </summary>
    private const float JitterThreshold = 0.01f;

    public static Vector2 Extrapolate(Vector2[] positionHistory, double elapsedSeconds)
    {
        var positionDelta = positionHistory[0] - positionHistory[1];

        // If the position delta is very small, treat the entity as stationary to prevent jitter
        if (positionDelta.LengthSquared() < JitterThreshold * JitterThreshold)
            return positionHistory[0];

        var speed = positionDelta / SecondsPerUpdate;
        return Extrapolate(positionHistory, speed, elapsedSeconds);
    }

    public static Vector2 Extrapolate(Vector2[] positionHistory, Vector2 speed, double elapsedSeconds)
    {
        #pragma warning disable CS0618
        var timeScale = Engine.TimeRate * Engine.TimeRateB;
        #pragma warning restore CS0618

        // When the game is frozen or nearly frozen, skip extrapolation to avoid numerical instability
        if (timeScale < 0.001f)
            return positionHistory[0];

        return positionHistory[0] + speed * timeScale * (float)elapsedSeconds;
    }
}