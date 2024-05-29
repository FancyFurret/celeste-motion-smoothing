using System;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class CameraSmoother
{
    private static bool _hooked;
    
    public static void EnableUnlock()
    {
        if (_hooked)
            return;
        
        IL.Celeste.Level.Render += LevelRenderHook;
        _hooked = true;
    }

    public static void DisableUnlock()
    {
        if (!_hooked)
            return;
        
        IL.Celeste.Level.Render -= LevelRenderHook;
        _hooked = false;
    }

    public static Vector2 Smooth(Camera camera, IPositionSmoothingState state, double elapsedSeconds,
        SmoothingMode mode)
    {
        return SmoothingMath.Smooth(state.RealPositionHistory, elapsedSeconds, SmoothingMode.Interpolate);
        
        // TODO: Revisit this
        var player = MotionSmoothingHandler.Instance.Player;

        // If the camera is targeting the player, have it target the smoothed player position
        // If not, just interpolate/extrapolate like normal
        if (player is { Dead: false } && (player.InControl || player.ForceCameraUpdate) &&
            Engine.Scene is Level { Transitioning: false })
            return TargetPlayer(player, camera, elapsedSeconds);

        return SmoothingMath.Smooth(state.RealPositionHistory, elapsedSeconds, mode);
    }

    private static Vector2 TargetPlayer(Player player, Camera camera, double elapsedSeconds)
    {
        const int stateReflectionFall = 18;
        const int stateTempleFall = 20;
        const int stateCassetteFly = 21;

        var cameraPos = camera.Position;

        var playerState = (MotionSmoothingHandler.Instance.GetState(player) as IPositionSmoothingState)!;
        var original = player.Position;

        player.Position = playerState.SmoothedRealPosition;
        var target = player.CameraTarget;
        player.Position = original;

        if (player.StateMachine.State == stateCassetteFly && player.cassetteFlyLerp < 1.0)
            return target;
        if (player.StateMachine.State == stateReflectionFall)
            return target;

        var num = player.StateMachine.State == stateTempleFall ? 8f : 1f;
        return cameraPos + (target - cameraPos) * (1f - (float)Math.Pow(0.009999999776482582 / num, elapsedSeconds));
    }

    public static Matrix GetCameraMatrix()
    {
        if (Engine.Scene is Level level)
        {
            var cameraState = (MotionSmoothingHandler.Instance.GetState(level.Camera) as IPositionSmoothingState)!;
            var pixelOffset = cameraState.SmoothedRealPosition - cameraState.SmoothedRealPosition.Floor();
            var offset = pixelOffset * 6;
            return Matrix.CreateTranslation(-offset.X, -offset.Y, 0);
        }

        return Matrix.Identity;
    }

    private static void LevelRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Offset the camera *after* it is resized larger
        if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchLdsfld(typeof(Engine).GetField(nameof(Engine.ScreenMatrix))!)))
        {
            cursor.Emit(OpCodes.Call, typeof(CameraSmoother).GetMethod(nameof(GetCameraMatrix))!);
            cursor.Emit(OpCodes.Call,
                typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
        }

        // Slightly scale up the level to hide the empty pixels on the right/bottom
        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<SpriteBatch>(nameof(SpriteBatch.Begin))))
        {
            const float scaleMultiplier = 181f / 180f;

            cursor.Emit(OpCodes.Ldloc, 8); // scale
            cursor.Emit(OpCodes.Ldc_R4, scaleMultiplier);
            cursor.Emit(OpCodes.Mul);
            cursor.Emit(OpCodes.Stloc, 8);
        }
    }
}