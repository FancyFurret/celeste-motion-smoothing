using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public static class PositionSmoother
{
    private const float MaxLerpDistance = 50f;

    public static Vector2 Smooth(IPositionSmoothingState state, object obj, double elapsedSeconds, SmoothingMode mode)
    {
        if (ShouldCancelSmoothing(state, obj))
            return state.OriginalDrawPosition;

        var smoothed = GetSmoothedPosition(state, obj, elapsedSeconds, mode);

        // If we are changing X direction, cancel X smoothing
        if (DirectionChanged(state.RealPositionHistory[2].X, state.RealPositionHistory[1].X,
                state.RealPositionHistory[0].X))
            smoothed.X = state.OriginalDrawPosition.X;

        // If we are changing Y direction, cancel Y smoothing
        if (DirectionChanged(state.RealPositionHistory[2].Y, state.RealPositionHistory[1].Y,
                state.RealPositionHistory[0].Y))
            smoothed.Y = state.OriginalDrawPosition.Y;

        return smoothed;
    }

    private static Vector2 GetSmoothedPosition(IPositionSmoothingState state, object obj, double elapsedSeconds,
        SmoothingMode mode)
    {
        // Manually fix boosters, can't figure out a better way of doing this
        // Boosters do not set the sprite to invisible, and if a player is entering a booster as it respawns,
        // it does not set the position to zero
        if (obj is Sprite { Entity: Booster booster } &&
            !booster.dashRoutine.Active && booster.respawnTimer <= 0)
            return state.OriginalDrawPosition;

        // TODO: Could move these to smoothing state subclasses
        var player = MotionSmoothingHandler.Instance.Player;
        if (state is ActorSmoothingState entityState && player != null)
        {
            if (obj == player)
                return PlayerSmoother.Smooth(player, entityState, elapsedSeconds, mode);

            if (obj == player.Holding?.Entity)
            {
                var playerState = (MotionSmoothingHandler.Instance.GetState(player) as IPositionSmoothingState)!;
                return entityState.GetLastDrawPosition(mode) + playerState.GetSmoothedOffset(mode);
            }
        }

        if (obj is Entity entity)
        {
            var mover = entity.Get<StaticMover>();
            if (mover is { Platform: not null })
            {
                var moverState = MotionSmoothingHandler.Instance.GetState(mover.Platform);
                if (moverState is { Changed: true })
                {
                    var startPos = mode == SmoothingMode.Interpolate
                        ? state.DrawPositionHistory[1]
                        : state.DrawPositionHistory[0];
                    return startPos +
                           ActorPushTracker.Instance.GetSolidOffset(moverState, mover.Platform, elapsedSeconds);
                }
            }
        }

        if (obj is Actor actor)
            if (ActorPushTracker.Instance.ApplyPusherOffset(actor, elapsedSeconds, mode, out var pushed))
                return pushed;

        return SmoothingMath.Smooth(state.RealPositionHistory, elapsedSeconds, mode);
    }

    private static bool ShouldCancelSmoothing(IPositionSmoothingState state, object obj)
    {
        // If the position is moving to zero, cancel
        if (state.DrawPositionHistory[0] == Vector2.Zero || state.DrawPositionHistory[1] == Vector2.Zero)
            return true;

        // If the entity was invisible but is now visible, cancel
        if (state.WasInvisible && state.GetVisible(obj))
        {
            state.WasInvisible = false;
            return true;
        }

        // If the distance is too large, cancel
        var distance = Vector2.DistanceSquared(state.RealPositionHistory[0], state.RealPositionHistory[1]);
        if (distance > MaxLerpDistance * MaxLerpDistance)
            return true;

        // Fixes pause buffering (otherwise the player could be extrapolated, and then snap back to the location they
        // were paused at the next update
        if (MotionSmoothingHandler.Instance.WasPaused)
            return true;

        return false;
    }

    private static bool DirectionChanged(float a, float b, float c)
    {
        return a > b && b < c || a < b && b > c;
    }
}