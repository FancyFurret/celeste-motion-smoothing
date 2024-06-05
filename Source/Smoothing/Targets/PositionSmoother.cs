﻿using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public static class PositionSmoother
{
    private const float MaxLerpDistance = 50f;

    public static Vector2 Smooth(IPositionSmoothingState state, object obj, double elapsedSeconds, SmoothingMode mode)
    {
        if (mode == SmoothingMode.None || ShouldCancelSmoothing(state, obj))
            return state.OriginalDrawPosition;

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
                return PlayerSmoother.Instance.Smooth(player, entityState, elapsedSeconds, mode);
            
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
                    return startPos + ActorPushTracker.Instance.GetSolidOffset(moverState, mover.Platform, elapsedSeconds);
                }
            }
        }

        if (obj is Actor actor)
            if (ActorPushTracker.Instance.ApplyPusherOffset(actor, elapsedSeconds, mode, out var pushed))
                return pushed;

        if (obj is Camera)
        {
            // In theory, we could calculate the Player.CameraTarget using the smoothed player position instead
            // of interpolating the camera position, but in testing it didn't look much smoother and was more prone
            // to issues. So for now, just interpolate the camera position.
            mode = SmoothingMode.Interpolate;
        }

        return SmoothingMath.Smooth(state.RealPositionHistory, elapsedSeconds, mode);
    }

    private static bool ShouldCancelSmoothing(IPositionSmoothingState state, object obj)
    {
        // If the position is moving to zero, cancel
        if (state.DrawPositionHistory[0] == Vector2.Zero || state.DrawPositionHistory[1] == Vector2.Zero)
            return true;
        
        // If the position isn't changing, cancel
        if (state.DrawPositionHistory[0] == state.DrawPositionHistory[1])
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

        return false;
    }
}