using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public static class MotionSmoothingMath
{
    private static float SecondsPerUpdate => (float)DecoupledGameTick.Instance.TargetUpdateElapsedTime.TotalSeconds;
    
    public static Vector2 Interpolate(Vector2[] positionHistory, double elapsedSeconds)
    {
        var t = (float)(elapsedSeconds / SecondsPerUpdate);
        return new Vector2
        {
            X = MathHelper.Lerp(positionHistory[1].X, positionHistory[0].X, t),
            Y = MathHelper.Lerp(positionHistory[1].Y, positionHistory[0].Y, t)
        };
    }

    public static Vector2 InterpolateAngle(Vector2[] positionHistory, double elapsedSeconds)
    {
        var t = (float)(elapsedSeconds / SecondsPerUpdate);
        return new Vector2
        {
            X = Calc.AngleLerp(positionHistory[1].X, positionHistory[0].X, t),
            Y = Calc.AngleLerp(positionHistory[1].Y, positionHistory[0].Y, t)
        };
    }

    public static Vector2 Extrapolate(Vector2[] positionHistory, double elapsedSeconds)
    {
        var speed = (positionHistory[0] - positionHistory[1]) / SecondsPerUpdate;
        return positionHistory[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds;
    }

    public static Vector2 Extrapolate(Vector2[] positionHistory, Vector2 speed, double elapsedSeconds)
    {
        return positionHistory[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds;
    }
}