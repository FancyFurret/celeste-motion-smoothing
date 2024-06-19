using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.HiresRenderer;

public class HiresFixes : ToggleableFeature<HiresFixes>
{
    public bool EnableFixMatrices { get; set; }

    private bool ShouldFixMatrices
    {
        get
        {
            if (!EnableFixMatrices) return false;
            var currentRenderTarget = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets()[0];
            return currentRenderTarget.RenderTarget == HiresLevelRenderer.HiresLevel.Target;
        }
    }

    protected override void Hook()
    {
        base.Hook();

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Begin",
            new[]
            {
                typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState),
                typeof(DepthStencilState), typeof(RasterizerState), typeof(Effect), typeof(Matrix)
            })!, BeginHook));

        // These might be a terrible idea, but we'll try them out for now and see if anything awful happens
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Floor), new[] { typeof(Vector2) })!, FloorHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Ceiling), new[] { typeof(Vector2) })!, CeilingHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Round), new[] { typeof(Vector2) })!, RoundHook));
        // IL.Celeste.Parallax.Render += RemoveFloors;
        // AddHook(new ILHook(typeof(Parallax).GetMethod("orig_Render")!, RemoveFloors));

        HookDrawVertices<VertexPositionColor>();
        HookDrawVertices<VertexPositionColorTexture>();
        HookDrawVertices<LightingRenderer.VertexPositionColorMaskTexture>();
    }

    private void HookDrawVertices<T>() where T : struct, IVertexType
    {
        AddHook(new Hook(typeof(GFX).GetMethod(nameof(GFX.DrawVertices))!.MakeGenericMethod(typeof(T)),
            DrawVerticesHook<T>));
        AddHook(new Hook(typeof(GFX).GetMethod(nameof(GFX.DrawIndexedVertices))!.MakeGenericMethod(typeof(T)),
            DrawIndexedVerticesHook<T>));
    }

    private static void RemoveFloors(ILContext il)
    {
        var c = new ILCursor(il);
        while (c.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(Calc), "Floor")))
        {
            // For compatibility, instead of removing the Floor call, have it floor a dummy Vector
            c.EmitCall(typeof(Vector2).GetProperty(nameof(Vector2.Zero))!.GetGetMethod()!);
            c.Index++; // Vector2.Zero.Floor()
            c.EmitPop(); // Pop the result of Vector2.Zero.Floor()
        }
    }

    // ReSharper disable once InconsistentNaming
    private delegate void orig_Begin(SpriteBatch self, SpriteSortMode sortMode, BlendState blendState,
        SamplerState samplerState,
        DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect, Matrix transformMatrix);

    private static void BeginHook(orig_Begin orig, SpriteBatch self, SpriteSortMode sortMode, BlendState blendState,
        SamplerState samplerState,
        DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect, Matrix transformMatrix)
    {
        if (Instance.ShouldFixMatrices)
            transformMatrix *= HiresLevelRenderer.ToHires;
        orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
    }

    // ReSharper disable once InconsistentNaming
    private delegate void orig_DrawVertices<in T>(Matrix matrix, T[] vertices, int vertexCount, Effect effect,
        BlendState blendState) where T : struct, IVertexType;

    private static void DrawVerticesHook<T>(orig_DrawVertices<T> orig, Matrix matrix, T[] vertices, int vertexCount,
        Effect effect, BlendState blendState) where T : struct, IVertexType
    {
        if (Instance.ShouldFixMatrices)
            matrix *= HiresLevelRenderer.ToHires;
        orig(matrix, vertices, vertexCount, effect, blendState);
    }

    // ReSharper disable once InconsistentNaming
    private delegate void orig_DrawIndexedVertices<in T>(Matrix matrix, T[] vertices, int vertexCount, int[] indices,
        int primitiveCount, Effect effect, BlendState blendState) where T : struct, IVertexType;

    private static void DrawIndexedVerticesHook<T>(orig_DrawIndexedVertices<T> orig, Matrix matrix, T[] vertices,
        int vertexCount,
        int[] indices, int primitiveCount, Effect effect, BlendState blendState) where T : struct, IVertexType
    {
        if (Instance.ShouldFixMatrices)
            matrix *= HiresLevelRenderer.ToHires;
        orig(matrix, vertices, vertexCount, indices, primitiveCount, effect, blendState);
    }

    // ReSharper disable once InconsistentNaming
    private delegate Vector2 orig_Floor(Vector2 self);

    private static Vector2 FloorHook(orig_Floor orig, Vector2 self)
    {
        return Instance.EnableFixMatrices ? self : orig(self);
    }

    // ReSharper disable once InconsistentNaming
    private delegate Vector2 orig_Ceiling(Vector2 self);

    private static Vector2 CeilingHook(orig_Ceiling orig, Vector2 self)
    {
        return Instance.EnableFixMatrices ? self : orig(self);
    }

    // ReSharper disable once InconsistentNaming
    private delegate Vector2 orig_Round(Vector2 self);

    private static Vector2 RoundHook(orig_Round orig, Vector2 self)
    {
        return Instance.EnableFixMatrices ? self : orig(self);
    }
}