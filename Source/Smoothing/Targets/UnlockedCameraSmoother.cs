using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class UnlockedCameraSmoother : ToggleableFeature<UnlockedCameraSmoother>
{
    private const float ZoomScaleMultiplier = 181f / 180f;
    private const int HiresPixelSize = 1080 / 180;
    private const int BorderOffset = HiresPixelSize / 2;

    private static bool _disableFloorFunctions = false;

    protected override void Hook()
    {
        base.Hook();

        IL.Celeste.Level.Render += LevelRenderHook;
        IL.Celeste.HiresRenderer.BeginRender += HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render += TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render += LookoutHudRenderHook;
        On.Monocle.Camera.CameraToScreen += CameraToScreenHook;

        On.Celeste.HudRenderer.RenderContent += HudRenderer_RenderContent;

        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Floor), new[] { typeof(Vector2) })!, FloorHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Ceiling), new[] { typeof(Vector2) })!, CeilingHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Round), new[] { typeof(Vector2) })!, RoundHook));
    }

    protected override void Unhook()
    {
        base.Unhook();

        IL.Celeste.Level.Render -= LevelRenderHook;
        IL.Celeste.HiresRenderer.BeginRender -= HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render -= TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render -= LookoutHudRenderHook;
        On.Monocle.Camera.CameraToScreen -= CameraToScreenHook;

        On.Celeste.HudRenderer.RenderContent -= HudRenderer_RenderContent;
    }

    private static Vector2 GetCameraOffset()
    {
        if (CelesteTasInterop.CenterCamera)
            return Vector2.Zero;

        if (Engine.Scene is Level level)
        {
            var cameraState = (MotionSmoothingHandler.Instance.GetState(level.Camera) as IPositionSmoothingState)!;
            var pixelOffset = cameraState.SmoothedRealPosition.Floor() - cameraState.SmoothedRealPosition;
            return SaveData.Instance.Assists.MirrorMode ? -pixelOffset : pixelOffset;
        }

        return Vector2.Zero;
    }

    public static Matrix GetScreenCameraMatrix()
    {
        var offset = GetCameraOffset() * HiresPixelSize;

        return Matrix.CreateTranslation(offset.X, offset.Y, 0);
    }

    public static float GetCameraScale()
    {
        return ZoomScaleMultiplier;
    }

    public static Vector2 GetSmoothedCameraPosition()
    {
        if (Engine.Scene is Level level)
        {
            var cameraState = (MotionSmoothingHandler.Instance.GetState(level.Camera) as IPositionSmoothingState)!;
            return cameraState.SmoothedRealPosition;
        }

        return Vector2.Zero;
    }

    private static void LevelRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Offset the camera *after* it is resized larger
        if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchLdsfld(typeof(Engine).GetField(nameof(Engine.ScreenMatrix))!)))
        {
            cursor.EmitDelegate(GetScreenCameraMatrix);
            cursor.EmitCall(typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
        }

        // (For the zoom mode) Slightly scale up the level to hide the empty pixels on the right/bottom
        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<SpriteBatch>(nameof(SpriteBatch.Begin))))
        {
            cursor.EmitLdloc(8); // scale
            cursor.EmitDelegate(GetCameraScale);
            cursor.EmitMul();
            cursor.EmitStloc(8);
        }
    }

    private static void HiresRendererBeginRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Scale the matrix for the HUD
        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdloc0()))
        {
            cursor.EmitDelegate(GetCameraScale);
            cursor.EmitCall(typeof(Matrix).GetMethod(nameof(Matrix.CreateScale), new[] { typeof(float) })!);
            cursor.EmitCall(typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
        }
    }

    private static void TalkComponentUiRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Use the smoothed camera position
        if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCallvirt<Camera>("get_Position"),
                instr => instr.MatchCall(typeof(Calc).GetMethod(nameof(Calc.Floor))!)))
        {
            // Ignore this value
            cursor.EmitPop();

            // Get just the smoothed position
            cursor.EmitCall(typeof(UnlockedCameraSmoother).GetMethod(nameof(GetSmoothedCameraPosition))!);
        }
    }

    private static void LookoutHudRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        if (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchStloc(5)))
        {
            // Add another pixel to the border size, so it covers up the empty pixels on the right/bottom
            cursor.EmitLdcI4(HiresPixelSize);
            cursor.EmitAdd();
        }
    }

    private static Vector2 CameraToScreenHook(On.Monocle.Camera.orig_CameraToScreen orig, Camera camera,
        Vector2 position)
    {
        return orig(camera, position + GetCameraOffset());
    }



    public static void HudRenderer_RenderContent(On.Celeste.HudRenderer.orig_RenderContent orig, HudRenderer self, Scene scene)
    {
        if (HiresRenderer.Instance is not { } renderer || scene is not Level level)
        {
            orig(self, scene);
            return;
        }

        Vector2 oldCameraPosition = level.Camera.Position;
        level.Camera.Position = GetSmoothedCameraPosition();
        _disableFloorFunctions = true;

        orig(self, scene);

        level.Camera.Position = oldCameraPosition;
        _disableFloorFunctions = false;
    }



    private delegate Vector2 orig_Floor(Vector2 self);

    private static Vector2 FloorHook(orig_Floor orig, Vector2 self)
    {
        if (!_disableFloorFunctions)
        {
            return orig(self);
        }

        return self;
    }

    private delegate Vector2 orig_Ceiling(Vector2 self);

    private static Vector2 CeilingHook(orig_Ceiling orig, Vector2 self)
    {
        if (!_disableFloorFunctions)
        {
            return orig(self);
        }

        return self;
    }

    private delegate Vector2 orig_Round(Vector2 self);

    private static Vector2 RoundHook(orig_Round orig, Vector2 self)
    {
        if (!_disableFloorFunctions)
        {
            return orig(self);
        }

        return self;
    }
}