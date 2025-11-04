using Celeste.Mod.Core;
using Celeste.Mod.Entities;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class UnlockedCameraSmoother : ToggleableFeature<UnlockedCameraSmoother>
{
    private const float ZoomScaleMultiplier = 181f / 180f;
    private const int HiresPixelSize = 1080 / 180;

    private static Effect _fxHiresGaussianBlur;

    private readonly HashSet<Hook> _hooks = new();

    public override void Load()
    {
        base.Load();
        On.Celeste.GFX.LoadEffects += GfxLoadEffectsHook;
    }

    public override void Unload()
    {
        base.Unload();
        _fxHiresGaussianBlur.Dispose();
        On.Celeste.GFX.LoadEffects -= GfxLoadEffectsHook;
    }


    protected override void Hook()
    {
        base.Hook();

        On.Celeste.Level.Render += Level_Render;
        //IL.Celeste.Level.Render += LevelRenderHook;
        //On.Celeste.BloomRenderer.Apply += BloomRenderer_Apply;
        IL.Celeste.BloomRenderer.Apply += BloomRendererApplyHook;
        //On.Celeste.Glitch.Apply += Glitch_Apply;
        IL.Celeste.Glitch.Apply += GlitchApplyHook;
        IL.Celeste.Godrays.Render += GodraysRenderHook;

        IL.Celeste.HiresRenderer.BeginRender += HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render += TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render += LookoutHudRenderHook;

        On.Monocle.Scene.Begin += Scene_Begin;
        On.Celeste.Level.End += Level_End;

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Begin",
            new[]
            {
                typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState),
                typeof(DepthStencilState), typeof(RasterizerState), typeof(Effect), typeof(Matrix)
            })!, SpriteBatch_Begin));
    }

    protected void AddHook(Hook hook)
    {
        _hooks.Add(hook);
    }

    protected override void Unhook()
    {
        base.Unhook();

        On.Celeste.Level.Render -= Level_Render;
        //IL.Celeste.Level.Render -= LevelRenderHook;
        //On.Celeste.BloomRenderer.Apply -= BloomRenderer_Apply;
        IL.Celeste.BloomRenderer.Apply -= BloomRendererApplyHook;
        //On.Celeste.Glitch.Apply -= Glitch_Apply;
        IL.Celeste.Glitch.Apply -= GlitchApplyHook;
        IL.Celeste.Godrays.Render -= GodraysRenderHook;

        IL.Celeste.HiresRenderer.BeginRender -= HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render -= TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render -= LookoutHudRenderHook;

        On.Monocle.Scene.Begin -= Scene_Begin;
        On.Celeste.Level.End -= Level_End;

        foreach (var hook in _hooks)
            hook.Dispose();

        SmoothParallaxRenderer.DisableLargeLevelBuffer();
    }

    private static void GfxLoadEffectsHook(On.Celeste.GFX.orig_LoadEffects orig)
    {
        orig();
        _fxHiresGaussianBlur = new Effect(Engine.Graphics.GraphicsDevice,
            Everest.Content.Get("MotionSmoothing:/Effects/HiresGaussianBlur.cso").Data);
    }

    private static void Scene_Begin(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        // we hook Scene.Begin rather than Level.Begin to ensure it runs
        // after GameplayBuffers.Create, but before any calls to Entity.SceneBegin
        if (self is Level level)
        {
            level.Add(SmoothParallaxRenderer.Create());
        }

        orig(self);
    }

    private static void Level_End(On.Celeste.Level.orig_End orig, Level self)
    {
        SmoothParallaxRenderer.Destroy();

        orig(self);
    }

    private static Vector2 GetCameraOffset()
    {
        return Vector2.Zero;
    }

    private static Vector2 GetCameraOffsetInternal()
    {
        if (CelesteTasInterop.CenterCamera)
            return Vector2.Zero;

        if (Engine.Scene is Level level)
        {
            var cameraState = (MotionSmoothingHandler.Instance.GetState(level.Camera) as IPositionSmoothingState)!;

            return cameraState.SmoothedRealPosition.Floor() - cameraState.SmoothedRealPosition;
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

        // Emit a delegate at the very start of the level
        cursor.Index = 0;
        cursor.EmitDelegate(PrepareLevelRender);



        // Add delegates before and Distort.Render
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCall(typeof(Distort), "Render")))
        {
            cursor.EmitDelegate(BeforeDistortRender);
        }

        cursor.Index = 0;

        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCall(typeof(Distort), "Render")))
        {
            cursor.EmitDelegate(DrawDisplacedGameplayWithOffset);
        }



        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCallvirt<BloomRenderer>("Apply")))
        {
            cursor.EmitDelegate(EnableFixMatricesWithScale);
        }



        // Fix dash assist
        // Find where DashAssistFreeze is loaded
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdsfld(typeof(Engine), "DashAssistFreeze")))
        {
            // Now find the next SpriteBatch.Begin call
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
                // Save the Begin position
                var beginIndex = cursor.Index;

                // Go backwards to find Camera.get_Matrix()
                if (cursor.TryGotoPrev(MoveType.After,
                    instr => instr.MatchCallvirt<Camera>("get_Matrix")))
                {
                    // Pop the Camera.Matrix value and replace with delegate
                    cursor.EmitPop();
                    cursor.EmitLdarg(0); // Load "this"
                    cursor.EmitDelegate(GetScaledCameraMatrix);
                }
            }
        }



        // Fix flash
        // First find when flash is loaded
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdfld<Level>("flash")))
        {
            // Find the SpriteBatch.Begin and go just before
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCallvirt(typeof(SpriteBatch), "Begin")))
            {
                // Emit the scale matrix
                cursor.EmitDelegate(GetScaleMatrix);

                // Modify the Begin call's operand to use the 7-parameter overload
                cursor.Next.Operand = typeof(SpriteBatch).GetMethod("Begin",
                    new[] { typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState),
                    typeof(DepthStencilState), typeof(RasterizerState), typeof(Effect), typeof(Matrix) })!;

                // Go past this begin call.
                cursor.Index++;
            }

            // Go to the beginning of the next SpriteBatch.Begin
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCall(typeof(Draw), "get_SpriteBatch")))
            {
                // Go to the matrix parameter
                if (cursor.TryGotoNext(MoveType.After,
                    instr => instr.MatchCallvirt(typeof(Camera), "get_Matrix")))
                {
                    // Pop the Camera.Matrix value and replace with delegate
                    cursor.EmitPop();
                    cursor.EmitLdarg(0); // Load "this"
                    cursor.EmitDelegate(GetScaledCameraMatrix);
                }
            }
        }

        // Find SetRenderTarget(null)
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdnull(),
            instr => instr.MatchCallvirt<GraphicsDevice>("SetRenderTarget")))
        {
            cursor.EmitDelegate(DisableFixMatrices);
        }

        // Ditch the 6x scale and replace it with 181/180 to zoom
        // First find the viewport assignment to ensure we're at the right location
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCallvirt<GraphicsDevice>("set_Viewport")))
        {
            // Find the pattern and position cursor right before stloc.2
            if (cursor.TryGotoNext(MoveType.Before,
                i => i.MatchLdcR4(6f)
            ))
            {
                if (cursor.TryGotoNext(MoveType.Before,
                    i => i.MatchStloc(2)
                ))
                {
                    cursor.Emit(OpCodes.Pop);
                    cursor.EmitDelegate(GetHiresDisplayMatrix);
                }
            }
        }

        // Find the final SpriteBatch.Draw call and replace GameplayBuffers.Level references
        // First, find and replace the texture parameter (first Level load)
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCall(typeof(Draw), "get_SpriteBatch")))
        {
            cursor.Index++;

            // Now find and modify the vector3 + vector4 addition
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCall(typeof(Vector2), "op_Addition")))
            {
                // Stack now has the result of vector3 + vector4
                // Multiply it by 6
                cursor.EmitLdcR4(6f);
                cursor.EmitCall(typeof(Vector2).GetMethod("op_Multiply", new[] { typeof(Vector2), typeof(float) })!);
            }

            // Find the next vector3 load (for origin parameter)
            // This is the ldloc.s 5 that comes after the Color.White and ldc.r4 0.0
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCall(typeof(Color), "get_White"),
                instr => instr.MatchLdcR4(0.0f),
                instr => instr.OpCode == OpCodes.Ldloc_S))
            {
                // Stack now has vector3
                // Multiply it by 6
                cursor.EmitLdcR4(6f);
                cursor.EmitCall(typeof(Vector2).GetMethod("op_Multiply", new[] { typeof(Vector2), typeof(float) })!);
            }
        }

        ////Go after the next SpriteBatch.End call
        //if (cursor.TryGotoNext(MoveType.After,
        //    instr => instr.MatchCallvirt<SpriteBatch>("End")))
        //{
        //    cursor.EmitDelegate(EnableFixMatricesWithoutScale);
        //}
    }

    private static void Level_Render(On.Celeste.Level.orig_Render orig, Level self)
    {
        PrepareLevelRender(); // Inserted

        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
        self.GameplayRenderer.Render(self);
        self.Lighting.Render(self);
        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);
        Engine.Instance.GraphicsDevice.Clear(self.BackgroundColor);
        self.Background.Render(self);

        BeforeDistortRender(); // Inserted
        Distort.Render((RenderTarget2D)GameplayBuffers.Gameplay, (RenderTarget2D)GameplayBuffers.Displacement, self.Displacement.HasDisplacement(self));
        DrawDisplacedGameplayWithOffset(); //Inserted

        self.Bloom.Apply(GameplayBuffers.Level, self);
        EnableFixMatricesWithScale(); // Inserted

        self.Foreground.Render(self);

        Glitch.Apply(GameplayBuffers.Level, self.glitchTimer * 2f, self.glitchSeed, MathF.PI * 2f);

        

        if (Engine.DashAssistFreeze)
        {
            PlayerDashAssist entity = self.Tracker.GetEntity<PlayerDashAssist>();
            if (entity != null)
            {
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, GetScaledCameraMatrix(self)); // Changed matrix
                entity.Render();
                Draw.SpriteBatch.End();
            }
        }
        if (self.flash > 0f)
        {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, renderer.ScaleMatrix); // Added scale matrix
            Draw.Rect(-1f, -1f, 322f, 182f, self.flashColor * self.flash);
            Draw.SpriteBatch.End();
            if (self.flashDrawPlayer)
            {
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, GetScaledCameraMatrix(self)); // Changed matrix
                Player entity2 = self.Tracker.GetEntity<Player>();
                if (entity2 != null && entity2.Visible)
                {
                    entity2.Render();
                }
                Draw.SpriteBatch.End();
            }
        }
        DisableFixMatrices(); // Inserted
        Engine.Instance.GraphicsDevice.SetRenderTarget(null);
        Engine.Instance.GraphicsDevice.Clear(Color.Black);
        Engine.Instance.GraphicsDevice.Viewport = Engine.Viewport;
        Matrix matrix = Matrix.CreateScale(6f) * Engine.ScreenMatrix;
        Vector2 vector = new Vector2(320f, 180f);

        Vector2 vector2 = vector / self.ZoomTarget;
        Vector2 vector3 = ((self.ZoomTarget != 1f) ? ((self.ZoomFocusPoint - vector2 / 2f) / (vector - vector2) * vector) : Vector2.Zero);
        MTexture orDefault = GFX.ColorGrades.GetOrDefault(self.lastColorGrade, GFX.ColorGrades["none"]);
        MTexture orDefault2 = GFX.ColorGrades.GetOrDefault(self.Session.ColorGrade, GFX.ColorGrades["none"]);
        if (self.colorGradeEase > 0f && orDefault != orDefault2)
        {
            ColorGrade.Set(orDefault, orDefault2, self.colorGradeEase);
        }
        else
        {
            ColorGrade.Set(orDefault2);
        }
        float scale = self.Zoom * ((320f - self.ScreenPadding * 2f) / 320f);
        Vector2 vector4 = new Vector2(self.ScreenPadding, self.ScreenPadding * 0.5625f);
        if (SaveData.Instance.Assists.MirrorMode)
        {
            vector4.X = 0f - vector4.X;
            vector3.X = 160f - (vector3.X - 160f);
        }

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, ColorGrade.Effect, GetHiresDisplayMatrix()); // Matrix modified
        Draw.SpriteBatch.Draw((RenderTarget2D)GameplayBuffers.Level, (vector3 + vector4) * 6f, GameplayBuffers.Level.Bounds, Color.White, 0f, vector3 * 6f, scale, SaveData.Instance.Assists.MirrorMode ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f); // Arguments modified
        Draw.SpriteBatch.End();

        if (self.Pathfinder != null && self.Pathfinder.DebugRenderEnabled)
        {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, self.Camera.Matrix * matrix * renderer.ScaleMatrix);
            self.Pathfinder.Render();
            Draw.SpriteBatch.End();
        }
        self.SubHudRenderer.Render(self);
        if (((!self.Paused || !self.PauseMainMenuOpen) && !(self.wasPausedTimer < 1f)) || !Input.MenuJournal.Check || !self.AllowHudHide)
        {
            self.HudRenderer.Render(self);
        }
        if (self.Wipe != null)
        {
            self.Wipe.Render(self);
        }
        if (self.HiresSnow != null)
        {
            self.HiresSnow.Render(self);
        }
    }

    private static void PrepareLevelRender()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = false;
        SmoothParallaxRenderer.DisableLargeLevelBuffer();
    }

    private static void BeforeDistortRender()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        // Go to the large level buffer for compositing time.
        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeLevelBuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

        // Draw the background upscaled out of GameplayBuffers.Level
        Draw.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
        Draw.SpriteBatch.Draw(GameplayBuffers.Level, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Reset to the usual Level buffer (but clear it) so the Distort.Render call that comes after renders into it
        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
    }

    private static void DrawDisplacedGameplayWithOffset()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeDisplacedGameplayBuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

        Draw.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
        Draw.SpriteBatch.Draw(GameplayBuffers.Level, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeLevelBuffer);

        Vector2 offset = GetCameraOffsetInternal() * 6f;

        Draw.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        Draw.SpriteBatch.Draw(renderer.LargeDisplacedGameplayBuffer, offset, Color.White);

        //These are necessary even with the zoom to ensure that the bloom doesn't show through from the background on the edges
        Draw.SpriteBatch.Draw(
           renderer.LargeDisplacedGameplayBuffer,
           offset + new Vector2(1920f, 0f),
           new Rectangle(1920 - 6, 0, 6, 1080),
           Color.White
       );
        Draw.SpriteBatch.Draw(
            renderer.LargeDisplacedGameplayBuffer,
            offset + new Vector2(0f, 1080f),
            new Rectangle(0, 1080 - 6, 1920, 6),
            Color.White
        );

        Draw.SpriteBatch.End();

        SmoothParallaxRenderer.EnableLargeLevelBuffer(); // Replace GameplayBuffers.Level with the big one.
    }

    private static void DisableFixMatrices()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = false;
    }

    private static void EnableFixMatricesWithScale()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = true;
        renderer.ScaleMatricesForBloom = false;
    }

    private static void EnableFixMatricesForBloom()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = true;
        renderer.ScaleMatricesForBloom = true;
    }

    private static Matrix GetScaleMatrix()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return Matrix.Identity;

        return renderer.ScaleMatrix;
    }

    private static Matrix GetOffsetScaleMatrix()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return Matrix.Identity;

        Vector2 offset = GetCameraOffsetInternal();

        return Matrix.CreateTranslation(offset.X, offset.Y, 0f) * renderer.ScaleMatrix;
    }

    private static Matrix GetOffsetMatrix()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return Matrix.Identity;

        Vector2 offset = GetCameraOffsetInternal();

        return Matrix.CreateTranslation(offset.X, offset.Y, 0f);
    }

    private static Matrix GetScaledCameraMatrix(Level level)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return level.Camera.Matrix;

        return renderer.ScaleMatrix * level.Camera.Matrix;
    }


    // This one needs the camera matrix out in front to work properly.
    private static Matrix GetOffsetScaledCameraMatrixForBloom(Level level)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return level.Camera.Matrix;

        Vector2 offset = GetCameraOffsetInternal();

        return level.Camera.Matrix * Matrix.CreateTranslation(offset.X, offset.Y, 0f) * renderer.ScaleMatrix;
    }



    private static Matrix GetHiresDisplayMatrix()
    {
        if (SaveData.Instance.Assists.MirrorMode)
        {
            return Matrix.CreateTranslation(-1920, 0, 0) * Matrix.CreateScale(ZoomScaleMultiplier) * Matrix.CreateTranslation(1920, 0, 0) * Engine.ScreenMatrix;
        }

        return Matrix.CreateScale(ZoomScaleMultiplier) * Engine.ScreenMatrix;
    }



    private static VirtualRenderTarget GetLargeGameplayBuffer()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return GameplayBuffers.Gameplay;

        return renderer.LargeGameplayBuffer;
    }

    private static VirtualRenderTarget GetLargeDisplacementBuffer()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return GameplayBuffers.Displacement;

        return renderer.LargeDisplacementBuffer;
    }

    private static VirtualRenderTarget GetLargeLevelBuffer()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return GameplayBuffers.Level;

        return renderer.LargeLevelBuffer;
    }

    private static VirtualRenderTarget GetLargeTempABuffer()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return GameplayBuffers.TempA;

        return renderer.LargeTempABuffer;
    }

    private static VirtualRenderTarget GetLargeTempBBuffer()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return GameplayBuffers.TempB;

        return renderer.LargeTempBBuffer;
    }



    private static void BloomRendererApplyHook(ILContext il)
    {
        var cursor = new ILCursor(il);


        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdsfld(typeof(GameplayBuffers), "TempA")))
        {
            cursor.EmitPop();
            cursor.EmitDelegate(GetLargeTempABuffer);
        }

        // Find and replace the TempA in the Blur call parameters
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdsfld(typeof(GameplayBuffers), "TempA")))
        {
            cursor.EmitPop();
            cursor.EmitDelegate(GetLargeTempABuffer);
        }

        // Replace TempB
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdsfld(typeof(GameplayBuffers), "TempB")))
        {
            cursor.EmitPop();
            cursor.EmitDelegate(GetLargeTempBBuffer);
        }



        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCall(typeof(GaussianBlur), "Blur")))
        {
            cursor.EmitDelegate(EnableFixMatricesForBloom);
        }

        cursor.Index = 0;

        // Find and replace the Blur method call
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCall(typeof(GaussianBlur), "Blur")))
        {
            // Save reference to the instruction before modifying
            var blurInstruction = cursor.Next;

            // Replace with ModifiedBlur method
            blurInstruction.Operand = typeof(UnlockedCameraSmoother).GetMethod("ModifiedBlur", new[] {
                typeof(Texture2D),
                typeof(VirtualRenderTarget),
                typeof(VirtualRenderTarget),
                typeof(float),
                typeof(bool),
                typeof(GaussianBlur.Samples),
                typeof(float),
                typeof(GaussianBlur.Direction),
                typeof(float)
            });
        }

        // Start from the end of the method
        cursor.Index = cursor.Instrs.Count - 1;

        // Search backwards
        if (cursor.TryGotoPrev(MoveType.Before,
            instr => instr.MatchLdsfld(typeof(BloomRenderer), "BlurredScreenToMask"),
            instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
        {
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
                Console.WriteLine("found 1");
                cursor.EmitDelegate(DisableFixMatrices);
            }

            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
                Console.WriteLine("found 2");
                cursor.EmitDelegate(EnableFixMatricesForBloom);
            }
        }

        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdsfld(typeof(BloomRenderer), "AdditiveMaskToScreen"),
            instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
        {
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
                Console.WriteLine("found 3");
                cursor.EmitDelegate(DisableFixMatrices);
            }

            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
                Console.WriteLine("found 4");
                cursor.EmitDelegate(EnableFixMatricesForBloom);
            }
        }
    }



    private static void BloomRenderer_Apply(On.Celeste.BloomRenderer.orig_Apply orig, BloomRenderer self, VirtualRenderTarget target, Scene scene)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer)
        {
            orig(self, target, scene);
            return;
        }

        if (!(self.Strength > 0f))
        {
            return;
        }

        VirtualRenderTarget tempA = renderer.LargeTempABuffer; // Buffer replaced
        Texture2D texture = ModifiedBlur((RenderTarget2D)target, renderer.LargeTempABuffer, renderer.LargeTempBBuffer); // Arguments and method modified
        EnableFixMatricesForBloom(); // Inserted
        List<Component> components = scene.Tracker.GetComponents<BloomPoint>();
        List<Component> components2 = scene.Tracker.GetComponents<EffectCutout>();
        Engine.Instance.GraphicsDevice.SetRenderTarget(tempA);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
        if (self.Base < 1f)
        {
            Camera camera = (scene as Level).Camera;
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, camera.Matrix);
            float num = 1f / (float)self.gradient.Width;
            foreach (Component item in components)
            {
                BloomPoint bloomPoint = item as BloomPoint;
                if (bloomPoint.Visible && !(bloomPoint.Radius <= 0f) && !(bloomPoint.Alpha <= 0f))
                {
                    self.gradient.DrawCentered(bloomPoint.Entity.Position + bloomPoint.Position, Color.White * bloomPoint.Alpha, bloomPoint.Radius * 2f * num);
                }
            }

            foreach (CustomBloom component in scene.Tracker.GetComponents<CustomBloom>())
            {
                if (component.Visible && component.OnRenderBloom != null)
                {
                    component.OnRenderBloom();
                }
            }

            foreach (Entity entity in scene.Tracker.GetEntities<SeekerBarrier>())
            {
                Draw.Rect(entity.Collider, Color.White);
            }

            Draw.SpriteBatch.End();
            if (components2.Count > 0)
            {
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BloomRenderer.CutoutBlendstate, SamplerState.PointClamp, null, null, null, camera.Matrix);
                foreach (Component item2 in components2)
                {
                    EffectCutout effectCutout = item2 as EffectCutout;
                    if (effectCutout.Visible)
                    {
                        Draw.Rect(effectCutout.Left, effectCutout.Top, effectCutout.Right - effectCutout.Left, effectCutout.Bottom - effectCutout.Top, Color.White * (1f - effectCutout.Alpha));
                    }
                }

                Draw.SpriteBatch.End();
            }
        }

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        Draw.Rect(-10f, -10f, 340f, 200f, Color.White * self.Base);
        Draw.SpriteBatch.End();

        DisableFixMatrices(); // Inserted
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BloomRenderer.BlurredScreenToMask);
        EnableFixMatricesForBloom(); // Inserted

        Draw.SpriteBatch.Draw(texture, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
        Engine.Instance.GraphicsDevice.SetRenderTarget(target);

        DisableFixMatrices(); // Inserted
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BloomRenderer.AdditiveMaskToScreen);
        EnableFixMatricesForBloom(); // Inserted
        for (int i = 0; (float)i < self.Strength; i++)
        {
            float num2 = (((float)i < self.Strength - 1f) ? 1f : (self.Strength - (float)i));
            Draw.SpriteBatch.Draw((RenderTarget2D)tempA, Vector2.Zero, Color.White * num2);
        }
        Draw.SpriteBatch.End();
    }

    public static Texture2D ModifiedBlur(Texture2D texture, VirtualRenderTarget temp, VirtualRenderTarget output, float fade = 0f, bool clear = true, GaussianBlur.Samples samples = GaussianBlur.Samples.Nine, float sampleScale = 1f, GaussianBlur.Direction direction = GaussianBlur.Direction.Both, float alpha = 1f)
    {
        Effect fxGaussianBlur = _fxHiresGaussianBlur;
        if (fxGaussianBlur != null)
        {
            fxGaussianBlur.CurrentTechnique = fxGaussianBlur.Techniques["GaussianBlur9"];
            fxGaussianBlur.Parameters["fade"].SetValue(fade);
            fxGaussianBlur.Parameters["pixel"].SetValue(new Vector2(1f / (float)temp.Width, 0f) * sampleScale);
            Engine.Instance.GraphicsDevice.SetRenderTarget(temp);
            if (clear)
            {
                Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            }
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, fxGaussianBlur);
            Draw.SpriteBatch.Draw(texture, new Rectangle(0, 0, temp.Width, temp.Height), Color.White);
            Draw.SpriteBatch.End();
            fxGaussianBlur.Parameters["pixel"].SetValue(new Vector2(0f, 1f / (float)output.Height) * sampleScale);
            Engine.Instance.GraphicsDevice.SetRenderTarget(output);
            if (clear)
            {
                Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            }
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, fxGaussianBlur);
            Draw.SpriteBatch.Draw((RenderTarget2D)temp, new Rectangle(0, 0, output.Width, output.Height), Color.White);
            Draw.SpriteBatch.End();
            return (RenderTarget2D)output;
        }
        return texture;
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



    private static void Godrays_Render(On.Celeste.Godrays.orig_Render orig, Godrays self, Scene scene)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        if (self.vertexCount > 0 && self.fade > 0f)
        {
            GFX.DrawVertices(renderer.ScaleMatrix, self.vertices, self.vertexCount);
        }
    }

    private static void GodraysRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Replace the identity matrix with the scale one
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCall(typeof(Matrix), "get_Identity")))
        {
            cursor.EmitPop();
            cursor.EmitDelegate(GetScaleMatrix);
        }
    }



    private static void Glitch_Apply(On.Celeste.Glitch.orig_Apply orig, VirtualRenderTarget source, float timer, float seed, float amplitude)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        if (Glitch.Value > 0f && CoreModule.Settings.AllowGlitch)
        {
            Effect fxGlitch = GFX.FxGlitch;
            Vector2 value = new Vector2(Engine.Graphics.GraphicsDevice.Viewport.Width, Engine.Graphics.GraphicsDevice.Viewport.Height);
            fxGlitch.Parameters["dimensions"].SetValue(value);
            fxGlitch.Parameters["amplitude"].SetValue(amplitude);
            fxGlitch.Parameters["minimum"].SetValue(-1f);
            fxGlitch.Parameters["glitch"].SetValue(Glitch.Value);
            fxGlitch.Parameters["timer"].SetValue(timer);
            fxGlitch.Parameters["seed"].SetValue(seed);
            VirtualRenderTarget tempA = renderer.LargeTempABuffer; // Buffer modified
            Engine.Instance.GraphicsDevice.SetRenderTarget(tempA);
            Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, fxGlitch);
            Draw.SpriteBatch.Draw((RenderTarget2D)source, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();
            Engine.Instance.GraphicsDevice.SetRenderTarget(source);
            Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, fxGlitch);
            Draw.SpriteBatch.Draw((RenderTarget2D)tempA, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();
        }
    }

    private static void GlitchApplyHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Replace GameplayBuffers.TempA with the large temp A one
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdsfld(typeof(GameplayBuffers), "TempA")))
        {
            cursor.EmitPop();
            cursor.EmitDelegate(GetLargeTempABuffer);
        }
    }



    private static void SpriteBatch_Begin(Action<SpriteBatch, SpriteSortMode, BlendState, SamplerState, DepthStencilState, RasterizerState, Effect, Matrix> orig, SpriteBatch self, SpriteSortMode sortMode, BlendState blendState,
        SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect, Matrix transformMatrix)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer)
        {
            orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
            return;
        }

        if (!renderer.FixMatrices)
        {
            orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
            return;
        }

        if (renderer.ScaleMatricesForBloom)
        {
            // Bloom needs this scale matrix precomposed.
            transformMatrix = transformMatrix * GetOffsetScaleMatrix();
            orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
            return;
        }

        transformMatrix = GetOffsetScaleMatrix() * transformMatrix;
        orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
    }
}