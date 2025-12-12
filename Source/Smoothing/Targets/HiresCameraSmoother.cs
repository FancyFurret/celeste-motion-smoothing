using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class HiresCameraSmoother : ToggleableFeature<HiresCameraSmoother>
{
    private const float ZoomScaleMultiplier = 181f / 180f;
    private const int HiresPixelSize = 1080 / 180;

	// Flag set by the SpriteBatch.Begin hook when it it currently scaling by 6x.
	private static bool _currentlyScaling = false;
	private static Texture _currentRenderTarget;

    private static readonly FieldInfo _beginCalledField = typeof(SpriteBatch)
	.GetField("beginCalled", BindingFlags.NonPublic | BindingFlags.Instance);
    private static (SpriteSortMode, BlendState, SamplerState, DepthStencilState, RasterizerState, Effect, Matrix)? _lastSpriteBatchBeginParams;

	// This maps references to all external textures (i.e. created by other mods)
    // that are supposed to be scaled by 6x. We hot-swap those with large buffers
    // when they get other large buffers drawn into them.
	private static Dictionary<Texture, VirtualRenderTarget> _largeExternalTextureMap = new Dictionary<Texture, VirtualRenderTarget>();

    // This is a set containing just the large versions of large textures, but
    // we also include two of the three that we enlarge (Level and Gameplay).
    // We don't watch for when TempA is drawn somewhere since it doesn't have a
    // dedicated function and it 
    private static HashSet<Texture> _largeTextures = new HashSet<Texture>();

	private static Effect _fxHiresDistort;
	private static Effect _fxOrigDistort;


    private static Vector2 SmoothedCameraPosition;
    private static Matrix SmoothedCameraMatrix;
    private static Matrix SmoothedCameraInverse;

    private static Vector2 UnsmoothedCameraPosition;
    private static Matrix UnsmoothedCameraMatrix;
    private static Matrix UnsmoothedCameraInverse;

    private readonly HashSet<Hook> _hooks = new();

    public override void Load()
    {
        base.Load();

		On.Celeste.GFX.LoadEffects += GfxLoadEffectsHook;
    }

    public override void Unload()
    {
        base.Unload();

		DisableHiresDistort();
		_fxHiresDistort.Dispose();
        On.Celeste.GFX.LoadEffects -= GfxLoadEffectsHook;
    }

	private static void GfxLoadEffectsHook(On.Celeste.GFX.orig_LoadEffects orig)
    {
        orig();
        _fxHiresDistort = new Effect(Engine.Graphics.GraphicsDevice,
            Everest.Content.Get("MotionSmoothing:/Effects/HiresDistort.cso").Data);
		_fxOrigDistort = GFX.FxDistort;
	}

	public static void EnableHiresDistort()
	{
        GFX.FxDistort = _fxHiresDistort;
	}

	public static void DisableHiresDistort()
	{
        GFX.FxDistort = _fxOrigDistort;
	}


    protected override void Hook()
    {
        base.Hook();

        // On.Celeste.Level.Render += Level_Render;
        IL.Celeste.Level.Render += LevelRenderHook;

        IL.Celeste.BloomRenderer.Apply += BloomRendererApplyHook;
        On.Celeste.GaussianBlur.Blur += GaussianBlur_Blur;

        On.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
        On.Celeste.GameplayRenderer.Render += GameplayRenderer_Render;
        On.Celeste.Distort.Render += Distort_Render;

		On.Celeste.Glitch.Apply += Glitch_Apply;

        On.Celeste.Parallax.Render += Parallax_Render;
        On.Celeste.Godrays.Update += Godrays_Update;

        On.Celeste.HudRenderer.RenderContent += HudRenderer_RenderContent;

        IL.Celeste.HiresRenderer.BeginRender += HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render += TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render += LookoutHudRenderHook;

        On.Monocle.Scene.Begin += Scene_Begin;
        On.Celeste.Level.End += Level_End;

        if (Engine.Scene is Level)
        {
            HiresRenderer.Destroy();
            var renderer = HiresRenderer.Create();

			_largeTextures.Clear();
			_largeTextures.Add(renderer.LargeLevelBuffer.Target);
			_largeTextures.Add(renderer.LargeGameplayBuffer.Target);
        }

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Begin",
            new[]
            {
                typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState),
                typeof(DepthStencilState), typeof(RasterizerState), typeof(Effect), typeof(Matrix)
            })!, SpriteBatch_Begin));

		AddHook(new Hook(typeof(SpriteBatch).GetMethod("PushSprite", MotionSmoothingModule.AllFlags)!,
            PushSpriteHook));

		AddHook(new Hook(typeof(SpriteBatch).GetMethod("End", Type.EmptyTypes)!, SpriteBatch_End));

        AddHook(new Hook(typeof(GraphicsDevice).GetMethod("SetRenderTargets",
            new[] { typeof(RenderTargetBinding[])
        })!, GraphicsDevice_SetRenderTargets));

        HookDrawVertices<VertexPositionColor>();
        HookDrawVertices<VertexPositionColorTexture>();
        HookDrawVertices<LightingRenderer.VertexPositionColorMaskTexture>();

        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Floor), new[] { typeof(Vector2) })!, FloorHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Ceiling), new[] { typeof(Vector2) })!, CeilingHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Round), new[] { typeof(Vector2) })!, RoundHook));

		if (MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
		{
			EnableHiresDistort();
		}
    }

    protected override void Unhook()
    {
        base.Unhook();

        // On.Celeste.Level.Render -= Level_Render;
        IL.Celeste.Level.Render -= LevelRenderHook;

        IL.Celeste.BloomRenderer.Apply -= BloomRendererApplyHook;
        On.Celeste.GaussianBlur.Blur -= GaussianBlur_Blur;

        On.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
        On.Celeste.GameplayRenderer.Render -= GameplayRenderer_Render;
        On.Celeste.Distort.Render -= Distort_Render;

		On.Celeste.Glitch.Apply -= Glitch_Apply;

        On.Celeste.Parallax.Render -= Parallax_Render;
		On.Celeste.Godrays.Update -= Godrays_Update;

        On.Celeste.HudRenderer.RenderContent -= HudRenderer_RenderContent;

        IL.Celeste.HiresRenderer.BeginRender -= HiresRendererBeginRenderHook;
        IL.Celeste.TalkComponent.TalkComponentUI.Render -= TalkComponentUiRenderHook;
        IL.Celeste.Lookout.Hud.Render -= LookoutHudRenderHook;

        On.Monocle.Scene.Begin -= Scene_Begin;
        On.Celeste.Level.End -= Level_End;

        foreach (var hook in _hooks)
            hook.Dispose();

        HiresRenderer.Destroy();
		DisableHiresDistort();
    }

    private static void Scene_Begin(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        // we hook Scene.Begin rather than Level.Begin to ensure it runs
        // after GameplayBuffers.Create, but before any calls to Entity.SceneBegin
        if (self is Level level)
        {
			var renderer = HiresRenderer.Create();
            level.Add(renderer);

			_largeTextures.Clear();
			_largeTextures.Add(renderer.LargeLevelBuffer.Target);
			_largeTextures.Add(renderer.LargeGameplayBuffer.Target);
        }

        orig(self);
    }

    private static void Level_End(On.Celeste.Level.orig_End orig, Level self)
    {
        HiresRenderer.Destroy();

        orig(self);
    }

    private static Vector2 GetCameraOffset()
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

    private static void ComputeSmoothedCameraData(Level level)
    {
        // Camera's UpdateMatrices method ALSO floors the position, so manually create the matrix here, and set
        // the private fields instead of using the public properties.
        var cameraState = (MotionSmoothingHandler.Instance.GetState(level.Camera) as IPositionSmoothingState)!;
        var camera = level.Camera;

        UnsmoothedCameraPosition = camera.position;
        UnsmoothedCameraMatrix = camera.matrix;
        UnsmoothedCameraInverse = camera.inverse;

        SmoothedCameraPosition = cameraState.SmoothedRealPosition;
        SmoothedCameraMatrix = Matrix.Identity *
                        Matrix.CreateTranslation(new Vector3(-new Vector2(camera.position.X, camera.position.Y),
                            0.0f)) *
                        Matrix.CreateRotationZ(camera.angle) * Matrix.CreateScale(new Vector3(camera.zoom, 1f)) *
                        Matrix.CreateTranslation(new Vector3(
                            new Vector2((int)Math.Floor(camera.origin.X), (int)Math.Floor(camera.origin.Y)), 0.0f));
        SmoothedCameraInverse = Matrix.Invert(camera.matrix);
    }

    private static void SmoothCameraPosition(Level level)
    {
        var camera = level.Camera;
        camera.position = SmoothedCameraPosition;
        camera.matrix = SmoothedCameraMatrix;
        camera.inverse = SmoothedCameraInverse;

        camera.changed = false;
    }

    private static void UnsmoothCameraPosition(Level level)
    {
        var camera = level.Camera;
        camera.position = UnsmoothedCameraPosition;
        camera.matrix = UnsmoothedCameraMatrix;
        camera.inverse = UnsmoothedCameraInverse;

        camera.changed = false;
    }



    private static void LevelRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Emit a delegate at the very start of the level
        cursor.Index = 0;
        cursor.EmitLdarg(0); // Load "this"
        cursor.EmitDelegate(PrepareLevelRender);



        cursor.Index = 0;

        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdfld<Level>("BackgroundColor")))
        {
            if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCallvirt<GraphicsDevice>("Clear")))
            {
                cursor.EmitLdarg(0); // Load "this"
                cursor.EmitDelegate(AfterLevelClear);
            }
        }



        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCallvirt<BloomRenderer>("Apply")))
        {
            cursor.EmitLdarg(0); // Load "this"
            cursor.EmitDelegate(AfterBloomApply);
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



        if (cursor.TryGotoNext(MoveType.Before,
           instr => instr.MatchLdnull(),
           instr => instr.MatchCallvirt<GraphicsDevice>("SetRenderTarget")))
        {
            cursor.EmitLdarg(0); // Load "this"
            cursor.EmitDelegate(BeforeDrawToScreen);
        }

        // Replce the definition of the scale matrix

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

        // Multiply the offset vectors.
        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCallvirt(typeof(SpriteBatch), "Begin")))
        {
            cursor.EmitLdloca(5);
            cursor.EmitLdloca(9);
            cursor.EmitDelegate(MultiplyVectors);
        }

        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchLdfld(typeof(Level), "HudRenderer")))
        {
            cursor.EmitDelegate(DrawDebugBuffers);
        }
    }

    private static void Level_Render(On.Celeste.Level.orig_Render orig, Level self)
    {
        PrepareLevelRender(self); // Inserted

        if (HiresRenderer.Instance is not { } renderer) return;

        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
        self.GameplayRenderer.Render(self);
        self.Lighting.Render(self);

        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);
        Engine.Instance.GraphicsDevice.Clear(self.BackgroundColor);
        AfterLevelClear(self); // Inserted
        self.Background.Render(self);
        
        Distort.Render((RenderTarget2D)GameplayBuffers.Gameplay, (RenderTarget2D)GameplayBuffers.Displacement, self.Displacement.HasDisplacement(self));

        self.Bloom.Apply(GameplayBuffers.Level, self);
        AfterBloomApply(self); // Inserted

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
        BeforeDrawToScreen(self); // Inserted
        Engine.Instance.GraphicsDevice.SetRenderTarget(null);
        Engine.Instance.GraphicsDevice.Clear(Color.Black);
        Engine.Instance.GraphicsDevice.Viewport = Engine.Viewport;
        Matrix matrix = GetHiresDisplayMatrix(); // Matrix modified
        Vector2 vector = new Vector2(320f, 180f);
        Vector2 vector2 = vector / self.ZoomTarget;
        Vector2 vector3 = (self.ZoomTarget != 1f) ? ((self.ZoomFocusPoint - vector2 / 2f) / (vector - vector2) * vector) : Vector2.Zero; // Zoom focus point multiplied by 6
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

        vector3 *= 6f; // Vector scaled
        vector4 *= 6f; // Vector scaled
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, ColorGrade.Effect, matrix); // Matrix modified
        Draw.SpriteBatch.Draw((RenderTarget2D)GameplayBuffers.Level, vector3 + vector4, GameplayBuffers.Level.Bounds, Color.White, 0f, vector3, scale, SaveData.Instance.Assists.MirrorMode ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        Draw.SpriteBatch.End();

        if (self.Pathfinder != null && self.Pathfinder.DebugRenderEnabled)
        {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, self.Camera.Matrix * matrix);
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

    private static void ShortCircuit(Level self)
    {
        Engine.Instance.GraphicsDevice.SetRenderTarget(null);
        Engine.Instance.GraphicsDevice.Clear(Color.Black);
        Engine.Instance.GraphicsDevice.Viewport = Engine.Viewport;
        Matrix matrix = Matrix.CreateScale(6f) * Engine.ScreenMatrix;
        Vector2 vector = new Vector2(1920f, 1080f); // Vector multiplied by 6
        Vector2 vector2 = vector / (self.ZoomTarget * 6f); // zoom multiplied by 6
        Vector2 vector3 = ((self.ZoomTarget != 1f) ? (((self.ZoomFocusPoint * 6f) - vector2 / 2f) / (vector - vector2) * vector) : Vector2.Zero); // Zoom focus point multiplied by 6
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
        Draw.SpriteBatch.Draw((RenderTarget2D)GameplayBuffers.Level, vector3 + vector4, GameplayBuffers.Level.Bounds, Color.White, 0f, vector3, scale, SaveData.Instance.Assists.MirrorMode ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        Draw.SpriteBatch.End();
    }

    private static void DrawDebugBuffers()
    {
        if (HiresRenderer.Instance is not { } renderer) return;

        int index = 0;
        Engine.Instance.GraphicsDevice.SetRenderTarget(null);

        foreach (var (smallTexture, largeTarget) in _largeExternalTextureMap)
        {
            // if (largeTarget == null)
            // {
            //     _largeExternalTextureMap.Remove(smallTexture);
            //     continue;
            // }

            // if (smallTexture is not Texture2D texture2d)
            // {
            //     return;
            // }

            // Draw.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone, null);
            // Draw.Rect(0, 0, 320, 180, Color.Gray);
            // Draw.SpriteBatch.Draw(largeTarget, Vector2.Zero, Color.White);
            // Draw.SpriteBatch.Draw(texture2d, Vector2.Zero, Color.White);
            // Draw.SpriteBatch.End();

            index++;
        }

        Console.WriteLine(index);
    }

    private static void PrepareLevelRender(Level level)
    {
        if (HiresRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = false;
        renderer.FixMatricesWithoutOffset = false;
        renderer.AllowParallaxOneBackdrops = false;
        renderer.CurrentlyRenderingBackground = true;

        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            HiresRenderer.EnableLargeLevelBuffer();
            ComputeSmoothedCameraData(level);
        }

        else
        {
            HiresRenderer.DisableLargeLevelBuffer();
        }

		if (MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
		{
			HiresRenderer.DisableLargeGameplayBuffer();
		}

		Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
    }

    private static void AfterLevelClear(Level level)
    {
        if (HiresRenderer.Instance is not { } renderer)
        {
            return;
        }

        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            renderer.DisableFloorFunctions = true;
            renderer.FixMatrices = true;
            renderer.FixMatricesWithoutOffset = false;
            SmoothCameraPosition(level);
        }

        else
        {
            renderer.DisableFloorFunctions = false;
            renderer.FixMatrices = false;
        }
    }

    private static void AfterBloomApply(Level level)
    {
        HiresRenderer.DisableLargeTempABuffer(); // Inserted
        EnableFixMatricesWithScale(); // Inserted
    }

    private static void BeforeDrawToScreen(Level level)
    {
        // DisableFixMatrices();
    }

    private static void MultiplyVectors(ref Vector2 vector3, ref Vector2 vector4)
    {
        if (HiresRenderer.Instance is not { } renderer) return;

        vector3 *= 6f;
        vector4 *= 6f;
    }

    private static void DisableFixMatrices()
    {
        if (HiresRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = false;
    }

    private static void EnableFixMatricesWithScale()
    {
        if (HiresRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = true;
        renderer.ScaleMatricesForBloom = false;
    }

    private static void EnableFixMatricesForBloom()
    {
        if (HiresRenderer.Instance is not { } renderer) return;

        renderer.FixMatrices = true;
        renderer.ScaleMatricesForBloom = true;
    }

    private static Matrix GetScaleMatrix()
    {
        if (HiresRenderer.Instance is not { } renderer) return Matrix.Identity;

        return renderer.ScaleMatrix;
    }

    private static Matrix GetOffsetScaleMatrix()
    {
        if (HiresRenderer.Instance is not { } renderer) return Matrix.Identity;

        Vector2 offset = GetCameraOffset();

        return Matrix.CreateTranslation(offset.X, offset.Y, 0f) * renderer.ScaleMatrix;
    }

    private static Matrix GetOffsetMatrix()
    {
        if (HiresRenderer.Instance is not { } renderer) return Matrix.Identity;

        Vector2 offset = GetCameraOffset();

        return Matrix.CreateTranslation(offset.X, offset.Y, 0f);
    }

    private static Matrix GetScaledCameraMatrix(Level level)
    {
        if (HiresRenderer.Instance is not { } renderer) return level.Camera.Matrix;

        return renderer.ScaleMatrix * level.Camera.Matrix;
    }


    // This one needs the camera matrix out in front to work properly.
    private static Matrix GetOffsetScaledCameraMatrixForBloom(Level level)
    {
        if (HiresRenderer.Instance is not { } renderer) return level.Camera.Matrix;

        Vector2 offset = GetCameraOffset();

        return level.Camera.Matrix * Matrix.CreateTranslation(offset.X, offset.Y, 0f) * renderer.ScaleMatrix;
    }



    private static Matrix GetHiresDisplayMatrix()
    {
        if (SaveData.Instance.Assists.MirrorMode)
        {
            return Matrix.CreateTranslation(-1920, 0, 0) * Matrix.CreateScale(6f * ZoomScaleMultiplier) * Matrix.CreateTranslation(1920, 0, 0) * Engine.ScreenMatrix;
        }

		// Note that we leave the scale intact here! That's because the SpriteBatch.Draw
		// hook will strip it off later.
        return Matrix.CreateScale(6f * ZoomScaleMultiplier) * Engine.ScreenMatrix;
    }



    private static VirtualRenderTarget GetLargeLevelBuffer()
    {
        if (HiresRenderer.Instance is not { } renderer) return GameplayBuffers.Level;

        return renderer.LargeLevelBuffer;
    }

    private static VirtualRenderTarget GetLargeTempABuffer()
    {
        if (HiresRenderer.Instance is not { } renderer) return GameplayBuffers.TempA;

        return renderer.LargeTempABuffer;
    }

    private static VirtualRenderTarget GetLargeTempBBuffer()
    {
        if (HiresRenderer.Instance is not { } renderer) return GameplayBuffers.TempB;

        return renderer.LargeTempBBuffer;
    }



    private static void BloomRendererApplyHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        cursor.EmitDelegate(HiresRenderer.EnableLargeTempABuffer);

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

        // Start from the end of the method
        cursor.Index = cursor.Instrs.Count - 1;

        // Search backwards for the two different renders and exempt them from upscaling
        if (cursor.TryGotoPrev(MoveType.Before,
            instr => instr.MatchLdsfld(typeof(BloomRenderer), "BlurredScreenToMask"),
            instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
        {
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
                cursor.EmitDelegate(DisableFixMatrices);
            }

            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
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
                cursor.EmitDelegate(DisableFixMatrices);
            }

            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchCallvirt<SpriteBatch>("Begin")))
            {
                cursor.EmitDelegate(EnableFixMatricesForBloom);
            }
        }
    }



    private static void BloomRenderer_Apply(On.Celeste.BloomRenderer.orig_Apply orig, BloomRenderer self, VirtualRenderTarget target, Scene scene)
    {
        if (HiresRenderer.Instance is not { } renderer)
        {
            orig(self, target, scene);
            return;
        }

        if (!(self.Strength > 0f))
        {
            return;
        }

        HiresRenderer.EnableLargeTempABuffer();

        VirtualRenderTarget tempA = GameplayBuffers.TempA;
        Texture2D texture = ModifiedBlur((RenderTarget2D)target, GameplayBuffers.TempA, renderer.LargeTempBBuffer); // Argument modified
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



    private static Texture2D GaussianBlur_Blur(On.Celeste.GaussianBlur.orig_Blur orig, Texture2D texture, VirtualRenderTarget temp, VirtualRenderTarget output, float fade, bool clear, GaussianBlur.Samples samples, float sampleScale, GaussianBlur.Direction direction, float alpha)
    {
        if (HiresRenderer.Instance is not { } renderer || !renderer.UseModifiedBlur)
        {
            return orig(texture, temp, output, fade, clear, samples, sampleScale, direction, alpha);
        }
        
        return ModifiedBlur(texture, temp, output, fade, clear, samples, sampleScale, direction, alpha);
    }

    public static Texture2D ModifiedBlur(Texture2D texture, VirtualRenderTarget temp, VirtualRenderTarget output, float fade = 0f, bool clear = true, GaussianBlur.Samples samples = GaussianBlur.Samples.Nine, float sampleScale = 1f, GaussianBlur.Direction direction = GaussianBlur.Direction.Both, float alpha = 1f)
    {
        Effect fxGaussianBlur = GFX.FxGaussianBlur;
        if (fxGaussianBlur != null)
        {
            fxGaussianBlur.CurrentTechnique = fxGaussianBlur.Techniques["GaussianBlur9"];
            fxGaussianBlur.Parameters["fade"].SetValue(fade);
            fxGaussianBlur.Parameters["pixel"].SetValue(new Vector2(6f / (float)temp.Width, 0f) * sampleScale);
            Engine.Instance.GraphicsDevice.SetRenderTarget(temp);
            if (clear)
            {
                Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            }
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, fxGaussianBlur);
            Draw.SpriteBatch.Draw(texture, new Rectangle(0, 0, temp.Width, temp.Height), Color.White);
            Draw.SpriteBatch.End();
            fxGaussianBlur.Parameters["pixel"].SetValue(new Vector2(0f, 6f / (float)output.Height) * sampleScale);
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



    private static void BackdropRenderer_Render(On.Celeste.BackdropRenderer.orig_Render orig, BackdropRenderer self, Scene scene)
    {
        if (HiresRenderer.Instance is not { } renderer || scene is not Level level || !renderer.CurrentlyRenderingBackground || MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            orig(self, scene);
            return;
        }

        orig(self, scene);

        // Go to the large level buffer for compositing time.
        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeLevelBuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
        // Draw the background upscaled out of GameplayBuffers.Level
        Draw.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
        // Draw the non-parallax one backgrounds upscaled
        Draw.SpriteBatch.Draw(GameplayBuffers.Level, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();



        // Now draw the parallax-one backgrounds
        renderer.AllowParallaxOneBackdrops = true;
        renderer.FixMatrices = true;

        orig(self, scene);

        renderer.AllowParallaxOneBackdrops = false;
        renderer.CurrentlyRenderingBackground = false;

        HiresRenderer.EnableLargeLevelBuffer();
        renderer.FixMatricesWithoutOffset = true;

        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);
    }

    private static void GameplayRenderer_Render(On.Celeste.GameplayRenderer.orig_Render orig, GameplayRenderer self, Scene scene)
    {
		if (HiresRenderer.Instance is not { } renderer || !MotionSmoothingModule.Settings.RenderMadelineWithSubpixels || scene is not Level level)
        {
			orig(self, scene);
            return;
        }

		// If we're rendering with subpixels, we need to draw everything at 6x. We do
		// this by recreating the gameplay rendering loop, and every time we encounter
		// an entity that needs to be rendered at a fractional position, we draw everything
		// we have to renderer.LargeGameplayBuffer, clear GameplayBuffers.Gameplay, draw
		// the single sprite at a precise position, clear it again, and then keep going.

		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);
		Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

		Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
		GameplayRenderer.Begin();

		foreach (Entity entity in level.Entities)
		{
			if (entity.Visible && !entity.TagCheck(Tags.HUD | TagsExt.SubHUD))
			{
                var player = MotionSmoothingHandler.Instance.Player;

				if (entity == player || player?.Holding?.Entity == entity)
				{
					// Render the things below this entity
					GameplayRenderer.End();

					Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);

					Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
					Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
					Draw.SpriteBatch.End();

					Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
					Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

					GameplayRenderer.Begin();




					// Now render just this entity and copy it in at subpixel-precise position
					entity.Render();

					GameplayRenderer.End();
                    var state = MotionSmoothingHandler.Instance.GetState(entity) as IPositionSmoothingState;
					Vector2 offset = state.SmoothedRealPosition - state.SmoothedRealPosition.Round();
	
					if (Math.Abs(player.Speed.X) < float.Epsilon)
					{
						offset.X = 0;
					}

					if (Math.Abs(player.Speed.Y) < float.Epsilon)
					{
						offset.Y = 0;
					}

					Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);
					Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
					Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, offset, Color.White);
					Draw.SpriteBatch.End();
					
					Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
					Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

					// Keep going for things above this entity
					GameplayRenderer.Begin();

					continue;
				}

				entity.Render();
			}
		}

		GameplayRenderer.End();

		// Draw the topmost things.
		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);
	
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
        Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();


		
		// Now call this the large gameplay buffer in case other hooks reference it.
		HiresRenderer.EnableLargeGameplayBuffer();
		Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);

		// Very important! Some things live the SJ expert lobby draw into this buffer going forward
		renderer.FixMatrices = true;
		renderer.FixMatricesWithoutOffset = true;
    }

    private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion)
    {
		if (HiresRenderer.Instance is not { } renderer || Engine.Scene is not Level level)
        {
			orig(source, map, hasDistortion);
            return;
        }
		
		renderer.FixMatrices = false;
		renderer.DisableFloorFunctions = false;
		renderer.FixMatricesWithoutOffset = false;

		if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            UnsmoothCameraPosition(level);
        }

		var renderTargets = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets();



		if (!MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
		{
			// If we're not doing subpixel rendering, then we don't need to do that much.
			Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallLevelBuffer);
			Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
			
			orig(source, map, hasDistortion);

			Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);
			renderer.FixMatrices = true;

			Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			Draw.SpriteBatch.Draw(renderer.SmallLevelBuffer, Vector2.Zero, Color.White);
			Draw.SpriteBatch.End();

			renderer.FixMatrices = false;

			return;
		}



		

		renderer.FixMatrices = false;

		// Scale up the distortion map into LargeTempA
		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeTempABuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
	
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, renderer.ScaleMatrix);
        Draw.SpriteBatch.Draw(map, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

		// Now draw this into LargeTempB
		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeTempBBuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
		orig(source, renderer.LargeTempABuffer, hasDistortion);

		// Finally, draw this back into whatever we were supposed to be drawing to, but with offset.
		Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);
		Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        Draw.SpriteBatch.Draw(renderer.LargeTempBBuffer, GetCameraOffset() * 6f, Color.White);
        Draw.SpriteBatch.End();
    }



	private static void Glitch_Apply(On.Celeste.Glitch.orig_Apply orig, VirtualRenderTarget source, float timer, float seed, float amplitude)
	{
		DisableFixMatrices();
		HiresRenderer.EnableLargeTempABuffer();

		orig(source, timer, seed, amplitude);

		EnableFixMatricesWithScale();
		HiresRenderer.DisableLargeTempABuffer();
	}


	private static void Godrays_Update(On.Celeste.Godrays.orig_Update orig, Godrays self, Scene scene)
	{
		if (HiresRenderer.Instance is not { } renderer)
		{
			orig(self, scene);
			return;
		}

		Level level = scene as Level;
		bool flag = self.IsVisible(level);
		self.fade = Calc.Approach(self.fade, (float)(flag ? 1 : 0), Engine.DeltaTime);
		self.Visible = (self.fade > 0f);
		if (!self.Visible)
		{
			return;
		}
		Player entity = level.Tracker.GetEntity<Player>();
		Vector2 vector = Calc.AngleToVector(-1.6707964f, 1f);
		Vector2 value = new Vector2(-vector.Y, vector.X);
		int num = 0;
		for (int i = 0; i < self.rays.Length; i++)
		{
			if (self.rays[i].Percent >= 1f)
			{
				self.rays[i].Reset();
			}
			Godrays.Ray[] array = self.rays;
			int num2 = i;
			array[num2].Percent = array[num2].Percent + Engine.DeltaTime / self.rays[i].Duration;
			Godrays.Ray[] array2 = self.rays;
			int num3 = i;
			array2[num3].Y = array2[num3].Y + 8f * Engine.DeltaTime;
			float percent = self.rays[i].Percent;
			float num4 = -32f + self.Mod(self.rays[i].X - level.Camera.X * 0.9f, 384f);
			float num5 = -32f + self.Mod(self.rays[i].Y - level.Camera.Y * 0.9f, 244f);
			float width = self.rays[i].Width;
			float length = self.rays[i].Length;
			Vector2 value2 = new Vector2(num4, num5); // Removed casting
			Color color = self.rayColor * Ease.CubeInOut(Calc.Clamp(((percent < 0.5f) ? percent : (1f - percent)) * 2f, 0f, 1f)) * self.fade;
			if (entity != null)
			{
				float num6 = (value2 + level.Camera.Position - entity.Position).Length();
				if (num6 < 64f)
				{
					color *= 0.25f + 0.75f * (num6 / 64f);
				}
			}
			VertexPositionColor vertexPositionColor = new VertexPositionColor(new Vector3(value2 + value * width + vector * length, 0f), color);
			VertexPositionColor vertexPositionColor2 = new VertexPositionColor(new Vector3(value2 - value * width, 0f), color);
			VertexPositionColor vertexPositionColor3 = new VertexPositionColor(new Vector3(value2 + value * width, 0f), color);
			VertexPositionColor vertexPositionColor4 = new VertexPositionColor(new Vector3(value2 - value * width - vector * length, 0f), color);
			self.vertices[num++] = vertexPositionColor;
			self.vertices[num++] = vertexPositionColor2;
			self.vertices[num++] = vertexPositionColor3;
			self.vertices[num++] = vertexPositionColor2;
			self.vertices[num++] = vertexPositionColor3;
			self.vertices[num++] = vertexPositionColor4;
		}
		self.vertexCount = num;
	}



    public static void Parallax_Render(On.Celeste.Parallax.orig_Render orig, Parallax self, Scene scene)
    {
        if (HiresRenderer.Instance is not { } renderer || scene is not Level level || MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            orig(self, scene);
            return;
        }

        // We save the parallax-one backgrounds (which should move in lockstep with
        // the gameplay layer) until after the background has been upscaled, so that
        // We can draw them with the camera offset. This does mean they're drawn slightly
        // out of place from where they would normally be if other mods insert draw calls
        // after background rendering but before gameplay rendering, but in practice this
        // doesn't pose an issue.
        if (renderer.CurrentlyRenderingBackground)
        {
            bool isParallaxOne = self.Scroll.X == 1.0 && self.Scroll.Y == 1.0;

            if (isParallaxOne == renderer.AllowParallaxOneBackdrops)
            {
                orig(self, scene);
            }

            return;
        }

        orig(self, scene);
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
        renderer.DisableFloorFunctions = true;

        orig(self, scene);

        level.Camera.Position = oldCameraPosition;
        renderer.DisableFloorFunctions = false;
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

    // Despite having a fix for this more broadly by disaling Floor()s, some very
    // obscure places like the gate to Raspberry Roots in SJ still benefit from this more targeted fix
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


    
    private delegate void orig_SetRenderTargets(GraphicsDevice self, RenderTargetBinding[] renderTargets);

    private static void GraphicsDevice_SetRenderTargets(orig_SetRenderTargets orig, GraphicsDevice self, RenderTargetBinding[] renderTargetBindings)
    {
        if (renderTargetBindings == null || renderTargetBindings.Length == 0)
        {
            _currentRenderTarget = null;
            orig(self, renderTargetBindings);
            return;
        }

        for (int i = 0; i < renderTargetBindings.Length; i++)
        {
            // If there's a large version of this, then we use that instead.
            if (_largeExternalTextureMap.TryGetValue(renderTargetBindings[i].RenderTarget, out VirtualRenderTarget largeRenderTarget))
            {
                renderTargetBindings[i] = new RenderTargetBinding(largeRenderTarget.Target);
            }
        }

        bool needToRestartSpriteBatch = _currentRenderTarget != renderTargetBindings[0].RenderTarget && (bool)_beginCalledField.GetValue(Draw.SpriteBatch);

        _currentRenderTarget = renderTargetBindings[0].RenderTarget;

        orig(self, renderTargetBindings);

        if (needToRestartSpriteBatch)
        {
            if (_lastSpriteBatchBeginParams is var (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix))
            {
                Draw.SpriteBatch.End();
                Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix);
            }
        }
    }



    private delegate void orig_SpriteBatch_Begin(SpriteBatch self, SpriteSortMode sortMode, BlendState blendState,
        SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect, Matrix transformMatrix);

    private static void SpriteBatch_Begin(orig_SpriteBatch_Begin orig, SpriteBatch self, SpriteSortMode sortMode, BlendState blendState,
        SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect, Matrix transformMatrix)
    {
        if (HiresRenderer.Instance is not { } renderer)
        {
            orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
            return;
        }

        _lastSpriteBatchBeginParams = (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);

        // For reasons I haven't fully understood, all of these
        // coming scale matrices *must* be precomposed to work properly.

        if (_currentRenderTarget == renderer.LargeTempABuffer.Target)
        {
            if (renderer.FixMatrices && renderer.ScaleMatricesForBloom)
            {
                transformMatrix = transformMatrix * GetOffsetScaleMatrix();

                _currentlyScaling = true;
            }
        }

        // Otherwise, only modify the matrix if we're rendering to the one buffer that's bigger than things expect.
        else if (_currentRenderTarget == renderer.LargeLevelBuffer.Target || _currentRenderTarget == renderer.LargeGameplayBuffer.Target)
        {
            if (renderer.FixMatrices)
            {
                if (renderer.FixMatricesWithoutOffset)
                {
                    transformMatrix = transformMatrix * renderer.ScaleMatrix;
                }

                else
                {
                    transformMatrix = transformMatrix * GetOffsetScaleMatrix();
                }

                _currentlyScaling = true;
            }
        }

        // We don't check for renderer.FixMatrices here, since these buffers should always
        // have things scaled when drawing into them.
		else if (_largeTextures.Contains(_currentRenderTarget))
		{
			transformMatrix = transformMatrix * renderer.ScaleMatrix;

			_currentlyScaling = true;
		}

        orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
    }

	private delegate void orig_PushSprite(SpriteBatch self, Texture2D texture, float sourceX, float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH, Color color, float originX, float originY, float rotationSin, float rotationCos, float depth, byte effects);

	private static void PushSpriteHook(orig_PushSprite orig, SpriteBatch self, Texture2D texture, float sourceX, float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH, Color color, float originX, float originY, float rotationSin, float rotationCos, float depth, byte effects)
    {
		if (HiresRenderer.Instance is not { } renderer || texture == null)
		{
			orig(self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
			return;
		}

        // If you're drawing the small version of this texture, no you're not!
        if (_largeExternalTextureMap.TryGetValue(texture, out VirtualRenderTarget largeRenderTarget))
        {
            texture = largeRenderTarget.Target;
        }

        // We handle scaling when the target is large in the Begin hook, so the only things
        // we're handling here are when the *source* is large.
		if (_largeTextures.Contains(texture))
		{
			// If we're drawing something large and it's going to be scaled, we need to
            // not do that scaling to avoid drawing at 36x. Similarly, drawing something
			// large to the screen should also get unscaled. That's because if a buffer
            // has become 6x larger, then it definitely used to have a scaling matrix,
            // and so now it ought not to.
			if (_currentlyScaling || _currentRenderTarget == null)
			{
                if ((bool)_beginCalledField.GetValue(Draw.SpriteBatch))
                {
                    if (_lastSpriteBatchBeginParams is var (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix))
                    {
                        Draw.SpriteBatch.End();
                        Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix * Matrix.CreateScale(1f / 6f));
                        orig(self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
                        Draw.SpriteBatch.End();
                        Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix);
                    }
                }
                return;
			}

            if (_currentRenderTarget is not Texture2D texture2D)
            {
                orig(self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
                return;
            }

             // If we get to this point, then we're drawing something large into something
            // small. Danger! We need to replace that small buffer with a larger one.
            HotCreateLargeBuffer(texture2D);
		}

        orig(self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
    }

    private static VirtualRenderTarget HotCreateLargeBuffer(Texture2D smallTexture)
    {
        // We cap the dimensions here since the maximum allowable texture is
        // 4096x4096 (and this is close to that at 6x)
        if (smallTexture.Width > 640 || smallTexture.Height > 640)
        {
            return null;
        }

        VirtualRenderTarget largeTarget = GameplayBuffers.Create(smallTexture.Width * 6, smallTexture.Height * 6);

        _largeExternalTextureMap[smallTexture] = largeTarget;
        _largeTextures.Add(largeTarget.Target);

        return largeTarget;
    }

	private static void SpriteBatch_End(Action<SpriteBatch> orig, SpriteBatch self)
	{
		_currentlyScaling = false;
		orig(self);
	}



    private static Matrix GetScaleMatrixForDrawVertices()
    {
        if (HiresRenderer.Instance is not { } renderer) return Matrix.Identity;

        var renderTargets = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets();

        if (renderTargets == null || renderTargets.Length == 0)
        {
            return Matrix.Identity;
        }

        var currentRenderTarget = renderTargets[0].RenderTarget;

        if (!_largeTextures.Contains(currentRenderTarget))
        {
            return Matrix.Identity;
        }

        return renderer.ScaleMatrix;
    }

    private static Matrix MultiplyMatrices(Matrix matrix1, Matrix matrix2)
    {
        return matrix1 * matrix2;
    }

    private void HookDrawVertices<T>() where T : struct, IVertexType
    {
        var drawVerticesMethod = typeof(GFX).GetMethod(nameof(GFX.DrawVertices))!.MakeGenericMethod(typeof(T));
        var drawIndexedVerticesMethod = typeof(GFX).GetMethod(nameof(GFX.DrawIndexedVertices))!.MakeGenericMethod(typeof(T));

        AddHook(new ILHook(drawVerticesMethod, DrawVerticesILHook<T>));
        AddHook(new ILHook(drawIndexedVerticesMethod, DrawIndexedVerticesILHook<T>));
    }



    private void DrawVerticesILHook<T>(ILContext il) where T : struct, IVertexType
    {
        var cursor = new ILCursor(il);

        // Move to the beginning
        cursor.Index = 0;

        //Emit the matrix parameter
        cursor.Emit(OpCodes.Ldarg_0);

        // Create and multiply by scale matrix
        cursor.EmitDelegate(GetScaleMatrixForDrawVertices);
        cursor.EmitDelegate(MultiplyMatrices);

        // Store back to arg 0
        cursor.Emit(OpCodes.Starg_S, (byte)0);
    }

    private void DrawIndexedVerticesILHook<T>(ILContext il) where T : struct, IVertexType
    {
        var cursor = new ILCursor(il);

        // Move to the beginning
        cursor.Index = 0;

        //Emit the matrix parameter
        cursor.Emit(OpCodes.Ldarg_0);

        // Create and multiply by scale matrix
        cursor.EmitDelegate(GetScaleMatrixForDrawVertices);
        cursor.EmitDelegate(MultiplyMatrices);

        // Store back to arg 0
        cursor.Emit(OpCodes.Starg_S, (byte)0);
    }




    private delegate Vector2 orig_Floor(Vector2 self);

    private static Vector2 FloorHook(orig_Floor orig, Vector2 self)
    {
        if (HiresRenderer.Instance is not { } renderer || !renderer.DisableFloorFunctions)
        {
            return orig(self);
        }

        return self;
    }

    private delegate Vector2 orig_Ceiling(Vector2 self);

    private static Vector2 CeilingHook(orig_Ceiling orig, Vector2 self)
    {
        if (HiresRenderer.Instance is not { } renderer || !renderer.DisableFloorFunctions)
        {
            return orig(self);
        }

        return self;
    }

    private delegate Vector2 orig_Round(Vector2 self);

    private static Vector2 RoundHook(orig_Round orig, Vector2 self)
    {
        if (HiresRenderer.Instance is not { } renderer || !renderer.DisableFloorFunctions)
        {
            return orig(self);
        }

        return self;
    }
}