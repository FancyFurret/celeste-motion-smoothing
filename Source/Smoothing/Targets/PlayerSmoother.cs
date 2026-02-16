using System;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public static class PlayerSmoother
{
    /// <summary>
    /// Threshold for position delta below which we consider the player stationary.
    /// This prevents jitter from floating-point noise or stale Speed values.
    /// </summary>
    private const float JitterThreshold = 0.01f;

    public static Vector2 Smooth(Player player, IPositionSmoothingState state, double elapsed, SmoothingMode mode)
    {
        return mode switch
        {
            SmoothingMode.Interpolate => Interpolate(player, state, elapsed),
            SmoothingMode.Extrapolate => Extrapolate(player, state, elapsed),
            _ => state.OriginalRealPosition
        };
    }

    private static Vector2 Interpolate(Player player, IPositionSmoothingState state, double elapsed)
    {
        if (ActorPushTracker.Instance.ApplyPusherOffset(player, elapsed, SmoothingMode.Interpolate, out var pushed))
            return pushed;

        return SmoothingMath.Interpolate(state.RealPositionHistory, elapsed);
    }

    private static Vector2 Extrapolate(Player player, IPositionSmoothingState state, double elapsed)
    {
        // Disable during screen transitions or pause
        if (Engine.Scene is Level { Transitioning: true } or { Paused: true })
            return state.OriginalDrawPosition;

        // If the player is about to dash, reset the states so the player position stops going in the wrong direction
        if (MotionSmoothingHandler.Instance.AtDrawInputHandler.PressedThisUpdate(Input.Dash) ||
            MotionSmoothingHandler.Instance.AtDrawInputHandler.PressedThisUpdate(Input.CrouchDash))
            return state.OriginalDrawPosition;

        if (ActorPushTracker.Instance.ApplyPusherOffset(player, elapsed, SmoothingMode.Extrapolate, out var pushed,
                out var pusherPositionHistory))
        {
            // If we don't have pusher position history, just return the pushed position
            if (pusherPositionHistory == null)
                return pushed;

            // Compute player's velocity relative to the pusher by looking at the change in
            // relative position between frames. This is more numerically stable than computing
            // velocities separately and subtracting, because if the player moved with the pusher,
            // the relative position change will be exactly zero.
            var relativePosCurrent = state.RealPositionHistory[0] - pusherPositionHistory[0];
            var relativePosPrev = state.RealPositionHistory[1] - pusherPositionHistory[1];
            var relativePositionDelta = relativePosCurrent - relativePosPrev;

            // If the relative position delta is very small, treat as stationary relative to pusher
            if (relativePositionDelta.LengthSquared() < JitterThreshold * JitterThreshold)
                return pushed;

            var relativeVelocity = relativePositionDelta / SmoothingMath.SecondsPerUpdate;

            // Check if the player is inverted and flip the Y velocity accordingly
            if (GravityHelperImports.IsPlayerInverted?.Invoke() == true)
                relativeVelocity.Y *= -1;

            #pragma warning disable CS0618
            var timeScale = Engine.TimeRate * Engine.TimeRateB;
            #pragma warning restore CS0618
            return pushed + relativeVelocity * timeScale * (float)elapsed;
        }

        // Use position history to derive velocity, but blend with player.Speed for responsiveness.
        // player.Speed gives us the intended direction immediately, while position history
        // confirms actual movement. We use player.Speed but only if position history shows movement.
        var speed = player.Speed;
        if (GravityHelperImports.IsPlayerInverted?.Invoke() == true)
            speed.Y *= -1;

        Vector2 smoothedPosition = SmoothingMath.Extrapolate(state.RealPositionHistory, speed, elapsed);

        bool isMovingInBothDirections = Math.Abs(player.Speed.X) > float.Epsilon
            && Math.Abs(player.Speed.Y) > float.Epsilon;
        
        if (state.DrawPositionHistory[0].X == state.DrawPositionHistory[1].X && !isMovingInBothDirections)
        {
            smoothedPosition.X = state.OriginalDrawPosition.X;
        }

        if (state.DrawPositionHistory[0].Y == state.DrawPositionHistory[1].Y && !isMovingInBothDirections)
        {
            smoothedPosition.Y = state.OriginalDrawPosition.Y;
        }

        return smoothedPosition;
    }
}