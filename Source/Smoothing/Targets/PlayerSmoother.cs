using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public static class PlayerSmoother
{
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
                out var pusherVelocity))
        {
            // Compute player's velocity relative to the pusher by subtracting the pusher's velocity
            // from the player's total velocity (derived from position history).
            // This ensures consistency with how the pusher's offset is calculated and avoids any
            // discrepancies that could arise from using player.Speed directly.
            var playerVelocity = (state.RealPositionHistory[0] - state.RealPositionHistory[1]) / SmoothingMath.SecondsPerUpdate;
            var relativeVelocity = playerVelocity - pusherVelocity;

            // Check if the player is inverted and flip the Y velocity accordingly
            if (GravityHelperImports.IsPlayerInverted?.Invoke() == true)
                relativeVelocity.Y *= -1;

            #pragma warning disable CS0618
            return pushed + relativeVelocity * Engine.TimeRate * Engine.TimeRateB * (float)elapsed;
            #pragma warning restore CS0618
        }

        // Check if the player is inverted and flip the speed accordingly
        var speed = player.Speed;
        if (GravityHelperImports.IsPlayerInverted?.Invoke() == true)
            speed.Y *= -1;

        return SmoothingMath.Extrapolate(state.RealPositionHistory, speed, elapsed);
    }
}