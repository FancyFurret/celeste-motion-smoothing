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
        if (Engine.Scene is Level { Transitioning: true } or { Paused: true } || Engine.FreezeTimer > 0)
            return state.OriginalDrawPosition;

        // If the player is about to dash, reset the states so the player position stops going in the wrong direction
        if (MotionSmoothingHandler.Instance.AtDrawInputHandler.PressedThisUpdate(Input.Dash) ||
            MotionSmoothingHandler.Instance.AtDrawInputHandler.PressedThisUpdate(Input.CrouchDash))
            return state.OriginalDrawPosition;

        // Check if the player is inverted and flip the speed accordingly
        var speed = player.Speed;
        if (GravityHelperImports.IsPlayerInverted?.Invoke() == true)
            speed.Y *= -1;

        if (ActorPushTracker.Instance.ApplyPusherOffset(player, elapsed, SmoothingMode.Extrapolate, out var pushed))
            return pushed + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsed;

        return SmoothingMath.Extrapolate(state.RealPositionHistory, elapsed);
    }
}