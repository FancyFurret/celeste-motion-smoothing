using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public static class ObjectSmoother
{
    private const float MaxLerpDistance = 50f;
    private static float SecondsPerUpdate => (float)DecoupledGameTick.Instance.TargetUpdateElapsedTime.TotalSeconds;

    public static Vector2 CalculateSmoothedPosition(ISmoothingState state, object obj, Player player,
        double elapsedSeconds)
    {
        if (ShouldCancelSmoothing(state, obj))
            return state.OriginalPosition;

        // Manually fix boosters, can't figure out a better way of doing this
        // Boosters do not set the sprite to invisible, and if a player is entering a booster as it respawns,
        // it does not set the position to zero
        if (obj is Sprite { Entity: Booster booster } &&
            !booster.dashRoutine.Active && booster.respawnTimer <= 0)
            return state.OriginalPosition;

        if (state is EntitySmoothingState entityState && player != null &&
            (obj == player || obj == player.Holding?.Entity))
        {
            return MotionSmoothingModule.Settings.PlayerSmoothing switch
            {
                MotionSmoothingSettings.PlayerSmoothingMode.Interpolate =>
                    Interpolate(state.PositionHistory, elapsedSeconds),
                MotionSmoothingSettings.PlayerSmoothingMode.Extrapolate =>
                    PlayerSmoother.ExtrapolatePosition(player, entityState, elapsedSeconds),
                _ => state.SmoothedPosition
            };
        }

        // Doesn't seem to help much
        // if (state is CameraSmoothingState && MotionSmoothingModule.Settings.PlayerSmoothing ==
        //     MotionSmoothingSettings.PlayerSmoothingMode.Extrapolate)
        // {
        //     state.SmoothedPosition = Extrapolate(state.PositionHistory, elapsedSeconds);
        //     continue;
        // }

        return state.IsAngle
            ? InterpolateAngle(state.PositionHistory, elapsedSeconds)
            : Interpolate(state.PositionHistory, elapsedSeconds);
    }

    private static bool ShouldCancelSmoothing(ISmoothingState state, object obj)
    {
        // If the position is moving to zero, cancel
        if (state.PositionHistory[0] == Vector2.Zero || state.PositionHistory[1] == Vector2.Zero)
            return true;

        // If the entity was invisible but is now visible, cancel
        if (state.WasInvisible && state.GetVisible(obj))
        {
            state.WasInvisible = false;
            return true;
        }

        // If the distance is too large, cancel
        if (Vector2.DistanceSquared(state.PositionHistory[0], state.PositionHistory[1]) >
            MaxLerpDistance * MaxLerpDistance)
            return true;

        return false;
    }

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