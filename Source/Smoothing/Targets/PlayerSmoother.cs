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



        var playerSpeed = player.Speed;
        
        var computedSpeed = (state.RealPositionHistory[0] - state.RealPositionHistory[1]) * 60;

        if (GravityHelperImports.IsPlayerInverted?.Invoke() == true)
            computedSpeed.Y *= -1;

        

        Vector2 smoothedPosition = SmoothingMath.Extrapolate(state.RealPositionHistory, computedSpeed, elapsed);

        if (ActorPushTracker.Instance.ApplyPusherOffset(player, elapsed, SmoothingMode.Extrapolate, out var pushed, out var pusherVelocity))
        {
            playerSpeed = pusherVelocity;

            smoothedPosition = pushed;
        }



        bool isMovingInBothDirections = Math.Abs(playerSpeed.X) > float.Epsilon
            && Math.Abs(playerSpeed.Y) > float.Epsilon;
        
        bool canClimb = player.StateMachine.State == Player.StClimb;
        
        if (state.DrawPositionHistory[0].X == state.DrawPositionHistory[1].X && !isMovingInBothDirections)
        {
            smoothedPosition.X = state.OriginalDrawPosition.X;
        }

        if (state.DrawPositionHistory[0].Y == state.DrawPositionHistory[1].Y && !isMovingInBothDirections && canClimb)
        {
            smoothedPosition.Y = state.OriginalDrawPosition.Y;
        }

        return smoothedPosition;
    }
}