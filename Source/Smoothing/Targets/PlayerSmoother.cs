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
    public static bool IsSmoothingX = false;
    public static bool IsSmoothingY = false;

    public static bool AllowSubpixelRenderingX = false;
    public static bool AllowSubpixelRenderingY = false;

    private static bool _ignoreSubpixelMotionX = false;
    private static int _xDeltaSignChanges = 0;
    private static int _prevXDeltaSign = 0;

    private static bool _ignoreSubpixelMotionY = false;
    private static int _yDeltaSignChanges = 0;
    private static int _prevYDeltaSign = 0;

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
        if (Engine.Scene is not Level || Engine.Scene is Level { Transitioning: true } or { Paused: true })
            return state.OriginalDrawPosition;

        var smoothedPosition = GetExtrapolatedPositionAndUpdateIsSmoothing(player, state, elapsed);

        // If the player is about to dash, reset the states so the player position stops going in the wrong direction
        if (
            player.CanDash &&
            (
                MotionSmoothingHandler.Instance.AtDrawInputHandler.PressedThisUpdate(Input.Dash)
                || MotionSmoothingHandler.Instance.AtDrawInputHandler.PressedThisUpdate(Input.CrouchDash)
            )
        ) {
            return state.OriginalDrawPosition;
        }
        
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
        // We initialize it in this bizarre way so MoveBlocks can
        // override individual directions later.
        bool isNotStandingStillX = Math.Abs(playerSpeed.X) > 0.001 || Math.Abs(playerSpeed.Y) > 0.001;
        bool isNotStandingStillY = isNotStandingStillX;
        
        var computedSpeed = (state.RealPositionHistory[0] - state.RealPositionHistory[1]) * 60;

        

        Vector2 smoothedPosition = SmoothingMath.Extrapolate(state.RealPositionHistory, computedSpeed, elapsed);

        bool pusherOffsetApplied = ActorPushTracker.Instance.ApplyPusherOffset(player, elapsed, SmoothingMode.Extrapolate, out var pushed, out var pusherVelocity);
        if (pusherOffsetApplied)
        {
            playerSpeed = pusherVelocity;
            smoothedPosition = pushed;
        }



        // Detect subpixel X oscillation
        if (state.DrawPositionHistory[0].X != state.DrawPositionHistory[1].X)
        {
            _ignoreSubpixelMotionX = false;
            _xDeltaSignChanges = 0;
            _prevXDeltaSign = 0;
        }
        else if (!_ignoreSubpixelMotionX)
        {
            int sign = Math.Sign(state.RealPositionHistory[0].X - state.RealPositionHistory[1].X);
            if (sign != 0)
            {
                if (_prevXDeltaSign != 0 && sign != _prevXDeltaSign)
                    _xDeltaSignChanges++;
                _prevXDeltaSign = sign;
            }
            if (_xDeltaSignChanges >= 2)
                _ignoreSubpixelMotionX = true;
        }

        // Detect subpixel Y oscillation
        if (state.DrawPositionHistory[0].Y != state.DrawPositionHistory[1].Y)
        {
            _ignoreSubpixelMotionY = false;
            _yDeltaSignChanges = 0;
            _prevYDeltaSign = 0;
        }
        else if (!_ignoreSubpixelMotionY)
        {
            int sign = Math.Sign(state.RealPositionHistory[0].Y - state.RealPositionHistory[1].Y);
            if (sign != 0)
            {
                if (_prevYDeltaSign != 0 && sign != _prevYDeltaSign)
                    _yDeltaSignChanges++;
                _prevYDeltaSign = sign;
            }
            if (_yDeltaSignChanges >= 2)
                _ignoreSubpixelMotionY = true;
        }

        UpdateIsSmoothing(pusherOffsetApplied, pusherVelocity, playerSpeed, isNotStandingStillX, isNotStandingStillY, state, player);

        UpdateAllowSubpixelRendering(pusherOffsetApplied, pusherVelocity, playerSpeed, isNotStandingStillX, isNotStandingStillY, state, player);

        return smoothedPosition;
    }

    private static void UpdateIsSmoothing(
        bool pusherOffsetApplied,
        Vector2 pusherVelocity,
        Vector2 playerSpeed,
        bool isNotStandingStillX,
        bool isNotStandingStillY,
        IPositionSmoothingState state,
        Player player
    ) {
        // A player standing still on a moving platform should still be smoothed,
        // but only in the direction the platform is actually moving.
        bool ridingMovingSolid = pusherOffsetApplied && ActorPushTracker.Instance.IsPlayerRidingSolid;
        bool ridingMovingJumpThru = pusherOffsetApplied && ActorPushTracker.Instance.IsPlayerRidingJumpThru;
        bool ridingMovingEntity = ridingMovingSolid || ridingMovingJumpThru;

        if (ridingMovingEntity && Math.Abs(pusherVelocity.X) > 0.001)
            isNotStandingStillX = true;
        if (ridingMovingEntity && Math.Abs(pusherVelocity.Y) > 0.001)
            isNotStandingStillY = true;

        // We don't use float.Epsilon because there are edge cases where Madeline
        // can have nonzero but extremely small downward speed.
        bool isMovingInBothDirections = Math.Abs(playerSpeed.X) > 0.001 && Math.Abs(playerSpeed.Y) > 0.001;

        bool canClimb = player.StateMachine.State == Player.StClimb;

        IsSmoothingX = isNotStandingStillX && !_ignoreSubpixelMotionX && (
            state.DrawPositionHistory[0].X != state.DrawPositionHistory[1].X
            || isMovingInBothDirections
            || (ridingMovingEntity && Math.Abs(pusherVelocity.X) > 0.001)
        );

        IsSmoothingY = isNotStandingStillY && !_ignoreSubpixelMotionY && (
            state.DrawPositionHistory[0].Y != state.DrawPositionHistory[1].Y
            || isMovingInBothDirections
            || !canClimb
        );
    }
    
    // The logic for when we should use subpixel rendering is identical to position extrapolation,
    // except we don't just allow riding any moving solids (like moon blocks), but only specifically
    // steerable move blocks.
    private static void UpdateAllowSubpixelRendering(bool pusherOffsetApplied, Vector2 pusherVelocity, Vector2 playerSpeed, bool isNotStandingStillX, bool isNotStandingStillY, IPositionSmoothingState state, Player player)
    {
        bool ridingSteerableMoveBlock = pusherOffsetApplied && ActorPushTracker.Instance.IsPlayerRidingSteerableMoveBlock;
        if (ridingSteerableMoveBlock && pusherVelocity.X != 0)
            isNotStandingStillX = true;
        if (ridingSteerableMoveBlock && pusherVelocity.Y != 0)
            isNotStandingStillY = true;

        bool isMovingInBothDirections = Math.Abs(playerSpeed.X) > 0.001 && Math.Abs(playerSpeed.Y) > 0.001;
        
        bool canClimb = player.StateMachine.State == Player.StClimb;

        AllowSubpixelRenderingX = isNotStandingStillX && !_ignoreSubpixelMotionX && (
            state.DrawPositionHistory[0].X != state.DrawPositionHistory[1].X
            || isMovingInBothDirections
        );

        AllowSubpixelRenderingY = isNotStandingStillY && !_ignoreSubpixelMotionY && (
            state.DrawPositionHistory[0].Y != state.DrawPositionHistory[1].Y
            || isMovingInBothDirections
            || !canClimb
        );
    }
}