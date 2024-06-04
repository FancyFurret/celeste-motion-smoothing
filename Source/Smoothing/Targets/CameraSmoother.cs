using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class CameraSmoother
{
    const float ScaleMultiplier = 181f / 180f;
    
    private static bool _hooked;

    public static void EnableUnlock()
    {
        if (_hooked)
            return;

        IL.Celeste.Level.Render += LevelRenderHook;
        IL.Celeste.HiresRenderer.BeginRender += HiresRendererBeginRenderHook;

        _hooked = true;
    }

    public static void DisableUnlock()
    {
        if (!_hooked)
            return;

        IL.Celeste.Level.Render -= LevelRenderHook;
        IL.Celeste.HiresRenderer.BeginRender -= HiresRendererBeginRenderHook;

        _hooked = false;
    }

    public static Vector2 Smooth(Camera camera, IPositionSmoothingState state, double elapsedSeconds,
        SmoothingMode mode)
    {
        // In theory, we could calculate the Player.CameraTarget using the smoothed player position instead
        // of interpolating the camera position, but in testing it didn't look much smoother and was more prone
        // to issues. So for now, just interpolate the camera position.
        return SmoothingMath.Smooth(state.RealPositionHistory, elapsedSeconds, SmoothingMode.Interpolate);
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
            cursor.EmitCall(typeof(CameraSmoother).GetMethod(nameof(GetCameraMatrix))!);
            cursor.EmitCall(typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
        }

        // Slightly scale up the level to hide the empty pixels on the right/bottom
        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<SpriteBatch>(nameof(SpriteBatch.Begin))))
        {
            cursor.EmitLdloc(8); // scale
            cursor.EmitLdcR4(ScaleMultiplier);
            cursor.EmitMul();
            cursor.EmitStloc(8);
        }
    }

    private static void HiresRendererBeginRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Offset and scale the matrix for the HUD
        if (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchLdloc0()))
        {
            cursor.EmitCall(typeof(CameraSmoother).GetMethod(nameof(GetCameraMatrix))!);
            cursor.Index++;
            cursor.EmitCall(typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
            cursor.EmitLdcR4(ScaleMultiplier);
            cursor.EmitCall(typeof(Matrix).GetMethod(nameof(Matrix.CreateScale), new[] { typeof(float) })!);
            cursor.EmitCall(typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
        }
    }
}