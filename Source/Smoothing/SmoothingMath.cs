using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public static class SmoothingMath
{
    private static readonly float SecondsPerUpdate =
        (float)new TimeSpan((long)Math.Round(10_000_000.0 / 60)).TotalSeconds;

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
            SmoothingMode.Extrapolate => Extrapolate(history, elapsedSeconds),
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
        return history[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds;
    }

    public static Vector2 Extrapolate(Vector2[] positionHistory, double elapsedSeconds)
    {
        var speed = (positionHistory[0] - positionHistory[1]) / SecondsPerUpdate;
        return Extrapolate(positionHistory, speed, elapsedSeconds);
    }

    public static Vector2 Extrapolate(Vector2[] positionHistory, Vector2 speed, double elapsedSeconds)
    {
        return positionHistory[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds;
    }
}