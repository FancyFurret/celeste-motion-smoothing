using System;
using Celeste.Mod.Entities;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Monocle;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class UnlockedCameraSmoother : ToggleableFeature<UnlockedCameraSmoother>
{
    private const float ZoomScaleMultiplier = 181f / 180f;
    private const int HiresPixelSize = 1080 / 180;
    private const int BorderOffset = HiresPixelSize / 2;

    private static Matrix OldForegroundMatrix;

    public override void Load()
    {
        base.Load();
        //On.Celeste.GFX.LoadEffects += GfxLoadEffectsHook;
    }

    public override void Unload()
    {
        base.Unload();
        //_fxHiresDistort.Dispose();
        //On.Celeste.GFX.LoadEffects -= GfxLoadEffectsHook;
    }

    protected override void Hook()
    {
        base.Hook();

        //On.Celeste.Level.Render += Level_Render;
        IL.Celeste.Level.Render += LevelRenderHook;
        //On.Celeste.BloomRenderer.Apply += BloomRenderer_Apply;
        IL.Celeste.BloomRenderer.Apply += BloomRendererApplyHook;
        //On.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
        IL.Celeste.Godrays.Render += GodraysRenderHook;

        IL.Celeste.HiresRenderer.BeginRender += HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render += TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render += LookoutHudRenderHook;
        On.Monocle.Scene.Begin += Scene_Begin;
    }

    protected override void Unhook()
    {
        base.Unhook();

        //On.Celeste.Level.Render -= Level_Render;
        IL.Celeste.Level.Render -= LevelRenderHook;
        //On.Celeste.BloomRenderer.Apply -= BloomRenderer_Apply;
        IL.Celeste.BloomRenderer.Apply -= BloomRendererApplyHook;
        //On.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
        IL.Celeste.Godrays.Render -= GodraysRenderHook;

        IL.Celeste.HiresRenderer.BeginRender -= HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render -= TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render -= LookoutHudRenderHook;
        On.Monocle.Scene.Begin -= Scene_Begin;
    }

    private static void GfxLoadEffectsHook(On.Celeste.GFX.orig_LoadEffects orig)
    {
        orig();
        //_fxHiresDistort = new Effect(Engine.Graphics.GraphicsDevice,
        //    Everest.Content.Get("MotionSmoothing:/Effects/HiresDistort.cso").Data);
        //Logger.Log(nameof(MotionSmoothingModule), Everest.Content.Get("MotionSmoothing:/Effects/HiresDistort.cso").Data.ToString());
        //GFX.FxDistort = _fxHiresDistort;
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
            var pixelOffset = cameraState.SmoothedRealPosition.Floor() - cameraState.SmoothedRealPosition;
            return pixelOffset;
        }

        return Vector2.Zero;
    }

    public static Matrix GetScreenCameraMatrix()
    {
        var offset = GetCameraOffset() * HiresPixelSize;

        if (MotionSmoothingModule.Settings.UnlockCameraMode == UnlockCameraMode.Border ||
            MotionSmoothingModule.Settings.UnlockCameraMode == UnlockCameraMode.Extend)
            offset += new Vector2(BorderOffset, BorderOffset);

        return Matrix.CreateTranslation(offset.X, offset.Y, 0);
    }

    public static float GetCameraScale()
    {
        if (MotionSmoothingModule.Settings.UnlockCameraMode == UnlockCameraMode.Zoom)
            return ZoomScaleMultiplier;
        return 1;
    }

    public static Vector2 GetSmoothedCameraPosition()
    {
        if (Engine.Scene is Level level)
        {
            var cameraState = (MotionSmoothingHandler.Instance.GetState(level.Camera) as IPositionSmoothingState)!;
            var pos = cameraState.SmoothedRealPosition;
            if (MotionSmoothingModule.Settings.UnlockCameraMode == UnlockCameraMode.Border ||
                MotionSmoothingModule.Settings.UnlockCameraMode == UnlockCameraMode.Extend)
                pos -= new Vector2(.5f, .5f);
            return pos;
        }

        return Vector2.Zero;
    }



    private static void LevelRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Add delegate before Clear(BackgroundColor)
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdfld<Level>("BackgroundColor")))
        {
            cursor.EmitDelegate(BeforeBackgroundClear);
        }

        // Add delegate after Background.Render
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdfld<Level>("Background")))
        {
            // Move to after the Render call
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCallvirt<Renderer>("Render")))
            {
                cursor.EmitDelegate(AfterBackgroundRender);
            }
        }

        // Insert delegate after Distort.Render
        cursor.Index = 0; // Reset cursor
                          // First find Distort.Render
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCall(typeof(Distort), "Render")))
        {
            cursor.EmitDelegate(DrawDisplacedGameplayWithOffset); // Should return VirtualRenderTarget
        }

        // Replace first argument of Bloom.Apply
        cursor.Index = 0; // Reset cursor
        // Find the Level buffer load that comes before Bloom.Apply
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdfld<Level>("Bloom")))
        {
            // Move forward to find the GameplayBuffers.Level load
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchLdsfld(typeof(GameplayBuffers), "Level")))
            {
                // Verify this is the one before Bloom.Apply
                var savedIndex = cursor.Index;
                if (cursor.TryGotoNext(MoveType.Before,
                    instr => instr.MatchCallvirt<BloomRenderer>("Apply")))
                {
                    cursor.Index = savedIndex;
                    cursor.EmitPop();
                    cursor.EmitDelegate(GetLargeLevelBuffer); // Should return VirtualRenderTarget
                }
            }
        }

        // Insert delegates before and after Foreground.Render
        cursor.Index = 0; // Reset cursor
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdfld<Level>("Foreground")))
        {
            // Emit delegate before Foreground.Render
            cursor.EmitLdarg(0); // Load "this"
            cursor.EmitDelegate<Action<Level>>(BeforeForegroundRender);

            // Find the Render call after this
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCallvirt<Renderer>("Render")))
            {
                // Emit delegate after Foreground.Render
                cursor.EmitLdarg(0); // Load "this"
                cursor.EmitDelegate<Action<Level>>(AfterForegroundRender);
            }
        }

        // Replace first argument of Glitch.Apply
        cursor.Index = 0; // Reset cursor
                          // Find the Level buffer load that comes before Glitch.Apply
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdsfld(typeof(GameplayBuffers), "Level")))
        {
            // Look ahead to verify this is the one before Glitch.Apply
            var savedIndex = cursor.Index;
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCall(typeof(Glitch), "Apply")))
            {
                cursor.Index = savedIndex;
                cursor.EmitPop();
                cursor.EmitDelegate(GetLargeLevelBuffer); // Should return VirtualRenderTarget
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
                    // Multiply by the scale matrix
                    cursor.EmitDelegate(GetScaleMatrix);
                    cursor.EmitCall(typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
                }
            }
        }


        // Ditch the 6x scale and replace it with 181/180 to zoom
        // First find the viewport assignment to ensure we're at the right location
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCallvirt<GraphicsDevice>("set_Viewport")))
        {
            // Find the matrix multiplication
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchLdcR4(6f)))
            {
                cursor.EmitPop();
                cursor.EmitLdcR4(181f / 180f);
            }
        }

        // Find the final SpriteBatch.Draw call and replace GameplayBuffers.Level references
        // First, find and replace the texture parameter (first Level load)
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCall(typeof(Draw), "get_SpriteBatch")))
        {
            cursor.Index++;

            // Replace the two buffer references
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchLdsfld(typeof(GameplayBuffers), "Level")))
            {
                cursor.EmitPop();
                cursor.EmitDelegate(GetLargeLevelBuffer);
            }

            // Now find and modify the vector3 + vector4 addition
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCall(typeof(Vector2), "op_Addition")))
            {
                // Stack now has the result of vector3 + vector4
                // Multiply it by 6
                cursor.EmitLdcR4(6f);
                cursor.EmitCall(typeof(Vector2).GetMethod("op_Multiply", new[] { typeof(Vector2), typeof(float) })!);
            }

            // Change the next buffer
            if (cursor.TryGotoNext(MoveType.After,
                    instr => instr.MatchLdsfld(typeof(GameplayBuffers), "Level")))
            {
                cursor.EmitPop();
                cursor.EmitDelegate(GetLargeLevelBuffer);
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
    }

    private static void Level_Render(On.Celeste.Level.orig_Render orig, Level self)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
        self.GameplayRenderer.Render(self);
        self.Lighting.Render(self);
        //RenderGameplayToLargeBuffer(self); // Inserted

        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);

        BeforeBackgroundClear(); // Inserted
        Engine.Instance.GraphicsDevice.Clear(self.BackgroundColor);
        self.Background.Render(self);
        AfterBackgroundRender(); // Inserted

        Distort.Render((RenderTarget2D)GameplayBuffers.Gameplay, (RenderTarget2D)GameplayBuffers.Displacement, self.Displacement.HasDisplacement(self));
        DrawDisplacedGameplayWithOffset(); //Inserted

        self.Bloom.Apply(renderer.LargeLevelBuffer, self); // Argument modified

        BeforeForegroundRender(self); // Inserted
        self.Foreground.Render(self);
        AfterForegroundRender(self); // Inserted

        Glitch.Apply(renderer.LargeLevelBuffer, self.glitchTimer * 2f, self.glitchSeed, MathF.PI * 2f); // Argument modified

        if (Engine.DashAssistFreeze)
        {
            PlayerDashAssist entity = self.Tracker.GetEntity<PlayerDashAssist>();
            if (entity != null)
            {
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, renderer.ScaleMatrix * self.Camera.Matrix);
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
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, renderer.ScaleMatrix * self.Camera.Matrix); // Added scale matrix
                Player entity2 = self.Tracker.GetEntity<Player>();
                if (entity2 != null && entity2.Visible)
                {
                    entity2.Render();
                }
                Draw.SpriteBatch.End();
            }
        }
        Engine.Instance.GraphicsDevice.SetRenderTarget(null);
        Engine.Instance.GraphicsDevice.Clear(Color.Black);
        Engine.Instance.GraphicsDevice.Viewport = Engine.Viewport;
        Matrix matrix = Matrix.CreateScale(181 / 180f) * Engine.ScreenMatrix; // Matrix scale modified
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
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, ColorGrade.Effect, matrix);
        Draw.SpriteBatch.Draw((RenderTarget2D)renderer.LargeLevelBuffer, (vector3 + vector4) * 6f, renderer.LargeLevelBuffer.Bounds, Color.White, 0f, vector3 * 6f, scale, SaveData.Instance.Assists.MirrorMode ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f); // Arguments modified
        Draw.SpriteBatch.End();
        if (self.Pathfinder != null && self.Pathfinder.DebugRenderEnabled)
        {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, renderer.ScaleMatrix * self.Camera.Matrix * matrix);
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

    private static void BeforeBackgroundClear()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        // Swap the buffer to Small 1.
        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBackgroundBuffer);
    }

    private static void AfterBackgroundRender()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        // Go to Large3 for compositing time.
        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeLevelBuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

        // Draw the background upscaled out of Small 1
        Draw.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
        Draw.SpriteBatch.Draw(renderer.SmallBackgroundBuffer, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Reset to the usual Level buffer so the Distort.Render call that comes after renders into it
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
        ////Draw the boundary again
        //Draw.SpriteBatch.Draw(
        //    renderer.LargeDisplacedGameplayBuffer,
        //    offset + new Vector2(1920f, 0f),
        //    new Rectangle(1920 - 6, 0, 6, 1080),
        //    Color.White
        //);
        //Draw.SpriteBatch.Draw(
        //    renderer.LargeDisplacedGameplayBuffer,
        //    offset + new Vector2(0f, 1080f),
        //    new Rectangle(0, 1080 - 6, 1920, 6),
        //    Color.White
        //);
        Draw.SpriteBatch.End();
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

    private static void BeforeForegroundRender(Level level)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        Vector2 offset = GetCameraOffsetInternal();

        OldForegroundMatrix = level.Foreground.Matrix;
        level.Foreground.Matrix = Matrix.CreateTranslation(offset.X, offset.Y, 0f) * renderer.ScaleMatrix * level.Foreground.Matrix;
    }

    private static void AfterForegroundRender(Level level)
    {
        level.Foreground.Matrix = OldForegroundMatrix;
    }



    private static void BloomRendererApplyHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Add delegate just before TempA is loaded
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdsfld(typeof(GameplayBuffers), "TempA")))
        {
            Logger.Log(nameof(MotionSmoothingModule), "found");
            cursor.EmitDelegate(DownscaleLevelToBuffer);
        }

        // Repalce the argument in the GaussianBlur.Blur call
        // Find Blur call
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCall(typeof(GaussianBlur), "Blur")))
        {
            // Now search backwards for ldarg.1 (the target parameter) and swap it with GameplayBuffers.Level
            if (cursor.TryGotoPrev(MoveType.After,
                instr => instr.MatchLdarg(1)))
            {
                Logger.Log(nameof(MotionSmoothingModule), "found 2");

                cursor.EmitPop();
                cursor.EmitLdsfld(typeof(GameplayBuffers).GetField("Level"));
            }
        }

        // Find the LAST SpriteBatch.Begin call in the method
        int lastBeginIndex = -1;
        while (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
        {
            lastBeginIndex = cursor.Index;
            cursor.Index++; // Move forward to continue searching
        }

        if (lastBeginIndex >= 0)
        {
            cursor.Index = lastBeginIndex;

            // Add the additional parameters
            cursor.EmitLdsfld(typeof(SamplerState).GetField("PointClamp"));
            cursor.EmitLdsfld(typeof(DepthStencilState).GetField("Default"));
            cursor.EmitLdsfld(typeof(RasterizerState).GetField("CullNone"));
            cursor.EmitLdnull(); // null for Effect
            cursor.EmitDelegate(GetOffsetScaleMatrix); // Matrix from delegate

            // Now modify the saved instruction reference
            cursor.Next.Operand = typeof(SpriteBatch).GetMethod("Begin",
                    new[] { typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState),
                    typeof(DepthStencilState), typeof(RasterizerState), typeof(Effect), typeof(Matrix) })!;
        }
    }

    private static void DownscaleLevelToBuffer()
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        // Render down the level to the old small buffer with linear scaling
        Engine.Graphics.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);
        Engine.Graphics.GraphicsDevice.Clear(Color.Transparent);

        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone
        );

        Draw.SpriteBatch.Draw(
            renderer.LargeLevelBuffer,
            new Rectangle(0, 0, 320, 180),
            Color.White
        );

        Draw.SpriteBatch.End();
    }



    private static void BloomRenderer_Apply(On.Celeste.BloomRenderer.orig_Apply orig, BloomRenderer self, VirtualRenderTarget target, Scene scene)
    {
        if (SmoothParallaxRenderer.Instance is not { } renderer) return;

        if (!(self.Strength > 0f))
        {
            return;
        }

        DownscaleLevelToBuffer(); // Inserted



        VirtualRenderTarget tempA = GameplayBuffers.TempA;
        Texture2D texture = GaussianBlur.Blur((RenderTarget2D)GameplayBuffers.Level, GameplayBuffers.TempA, GameplayBuffers.TempB); // First argument modified
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

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null);
        Draw.Rect(-10f, -10f, 340f, 200f, Color.White * self.Base);
        Draw.SpriteBatch.End();
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BloomRenderer.BlurredScreenToMask);
        Draw.SpriteBatch.Draw(texture, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
        Engine.Instance.GraphicsDevice.SetRenderTarget(target);
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BloomRenderer.AdditiveMaskToScreen, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, GetOffsetScaleMatrix()); // Arguments modified
        for (int i = 0; (float)i < self.Strength; i++)
        {
            float num2 = (((float)i < self.Strength - 1f) ? 1f : (self.Strength - (float)i));
            Draw.SpriteBatch.Draw((RenderTarget2D)tempA, Vector2.Zero, Color.White * num2);
        }

        Draw.SpriteBatch.End();
    }



    //private static void BackdropRenderer_Render(On.Celeste.BackdropRenderer.orig_Render orig, BackdropRenderer self, Scene scene)
    //{
    //    BlendState blendState = BlendState.AlphaBlend;
    //    foreach (Backdrop backdrop in self.Backdrops)
    //    {
    //        if (!backdrop.Visible)
    //        {
    //            continue;
    //        }

    //        if (backdrop is Parallax parallax && (!self.usingLoopingSpritebatch || parallax.BlendState != blendState))
    //        {
    //            self.EndSpritebatch();
    //            blendState = parallax.BlendState;
    //        }

    //        if (!(backdrop is Parallax) && backdrop.UseSpritebatch && self.usingLoopingSpritebatch)
    //        {
    //            self.EndSpritebatch();
    //        }

    //        if (backdrop.UseSpritebatch && !self.usingSpritebatch)
    //        {
    //            if (backdrop is Parallax)
    //            {
    //                self.StartSpritebatchLooping(blendState);
    //            }
    //            else
    //            {
    //                self.StartSpritebatch(blendState);
    //            }
    //        }

    //        if (!backdrop.UseSpritebatch && self.usingSpritebatch)
    //        {
    //            self.EndSpritebatch();
    //        }

    //        backdrop.Render(scene);
    //    }

    //    if (self.Fade > 0f)
    //    {
    //        Draw.Rect(-10f * 6f, -10f * 6f, 340f * 6f, 200f * 6f, self.FadeColor * self.Fade);
    //    }

    //    self.EndSpritebatch();
    //}

    private static void RenderExtend(Vector2 origin, Vector2 offset, float scale)
    {
        const int textureWidth = 320;
        const int textureHeight = 180;

        if (MotionSmoothingModule.Settings.UnlockCameraMode != UnlockCameraMode.Extend)
            return;
        if (((Level)Engine.Scene).ScreenPadding > 0)
            return;

        var effect = SaveData.Instance.Assists.MirrorMode ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        var texture = GameplayBuffers.Level;

        Draw.SpriteBatch.Draw(texture, origin + offset + new Vector2(0, textureHeight * scale),
            new Rectangle(0, textureHeight - 1, textureWidth, 1), Color.White, 0.0f, origin, scale, effect, 0.0f);

        Draw.SpriteBatch.Draw(texture, origin + offset + new Vector2(textureWidth * scale, 0),
            new Rectangle(textureWidth - 1, 0, 1, textureHeight), Color.White, 0.0f, origin, scale, effect, 0.0f);

        Draw.SpriteBatch.Draw(texture, origin + offset + new Vector2(0, -1 * scale),
            new Rectangle(0, 0, textureWidth, 1), Color.White, 0.0f, origin, scale, effect, 0.0f);

        Draw.SpriteBatch.Draw(texture, origin + offset + new Vector2(-1 * scale, 0),
            new Rectangle(0, 0, 1, textureHeight), Color.White, 0.0f, origin, scale, effect, 0.0f);
    }

    private static void RenderBorder()
    {
        if (MotionSmoothingModule.Settings.UnlockCameraMode != UnlockCameraMode.Border)
            return;

        const int width = 1920;
        const int height = 1080;
        const int size = HiresPixelSize / 2;

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, Engine.ScreenMatrix);

        Draw.Rect(0, 0, width, size, Color.Black);
        Draw.Rect(0, 0, size, height, Color.Black);
        Draw.Rect(width - size, 0, size, height, Color.Black);
        Draw.Rect(0, height - size, width, size, Color.Black);

        Draw.SpriteBatch.End();
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

        // Replace the ideneity matrix with the scale one
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCall(typeof(Matrix), "get_Identity")))
        {
            cursor.EmitPop();
            cursor.EmitDelegate(GetScaleMatrix);
        }
    }
}