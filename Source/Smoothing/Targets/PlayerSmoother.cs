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
    public static bool IsSmoothingX = true;
    public static bool IsSmoothingY = true;

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
        GetExtrapolatedPositionAndUpdateIsSmoothing(player, state, elapsed);

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



        var smoothedPosition = GetExtrapolatedPositionAndUpdateIsSmoothing(player, state, elapsed);
        
        if (!IsSmoothingX)
        {
            smoothedPosition.X = state.OriginalDrawPosition.X;
        }

        if (!IsSmoothingY)
        {
            smoothedPosition.Y = state.OriginalDrawPosition.Y;
        }

        return smoothedPosition;
    }

    private static Vector2 GetExtrapolatedPositionAndUpdateIsSmoothing(Player player, IPositionSmoothingState state, double elapsed)
    {
        var playerSpeed = player.Speed;

        // Checking this prevents the player from being incorrectly 
        // smoothed while standing still on moving platforms.
        bool isNotStandingStill = playerSpeed.X != 0 || playerSpeed.Y != 0;
        
        var computedSpeed = (state.RealPositionHistory[0] - state.RealPositionHistory[1]) * 60;

        

        Vector2 smoothedPosition = SmoothingMath.Extrapolate(state.RealPositionHistory, computedSpeed, elapsed);

        if (ActorPushTracker.Instance.ApplyPusherOffset(player, elapsed, SmoothingMode.Extrapolate, out var pushed, out var pusherVelocity))
        {
            playerSpeed = pusherVelocity;

            smoothedPosition = pushed;
        }


        
        // We don't use float.Epsilon because there are edge cases where Madeline
        // can have nonzero but extremely small downward speed.
        bool isMovingInBothDirections = Math.Abs(playerSpeed.X) > 0.001 && Math.Abs(playerSpeed.Y) > 0.001;
        
        bool canClimb = player.StateMachine.State == Player.StClimb;

        IsSmoothingX = isNotStandingStill && (
            state.DrawPositionHistory[0].X != state.DrawPositionHistory[1].X
            || isMovingInBothDirections
        );

        IsSmoothingY = isNotStandingStill && (
            state.DrawPositionHistory[0].Y != state.DrawPositionHistory[1].Y
            || isMovingInBothDirections
            || !canClimb
        );

        return smoothedPosition;
    }
}