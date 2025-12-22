using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Build.Framework;
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

    private static Matrix ScaleMatrix = Matrix.CreateScale(6f);
    private static Matrix InverseScaleMatrix = Matrix.CreateScale(1f / 6f);

	// Flag set by the SpriteBatch.Begin hook when it it currently scaling by 6x.
	private static bool _currentlyScaling = false;
	private static Texture _currentRenderTarget;

    private static bool _offsetDrawing = false;
    // Textures can be dded to this to prevent offset drawing when they are the target.
    private static HashSet<Texture> _excludeFromOffsetDrawing = new HashSet<Texture>();
    private static bool _scaleSpriteBatchBeginMatrices = true;

    private static bool _useHiresGaussianBlur = false;
    private static bool _currentlyRenderingBackground = false;
    private static bool _allowParallaxOneBackgrounds = false;
    private static bool _disableFloorFunctions = false;

    private static readonly FieldInfo _beginCalledField = typeof(SpriteBatch)
	.GetField("beginCalled", BindingFlags.NonPublic | BindingFlags.Instance);
    private static (SpriteSortMode, BlendState, SamplerState, DepthStencilState, RasterizerState, Effect, Matrix)? _lastSpriteBatchBeginParams;

	// This maps references to all external textures (i.e. created by other mods)
    // that are supposed to be scaled by 6x. We hot-swap those with large buffers
    // when they get other large buffers drawn into them.
	private static Dictionary<Texture, VirtualRenderTarget> _largeExternalTextureMap = new Dictionary<Texture, VirtualRenderTarget>();

    // This is a set containing just the large versions of large textures, but
    // we also include the four that we enlarge (Level, Gameplay, TempA, and TempB).
    private static HashSet<Texture> _largeTextures = new HashSet<Texture>();

    // Meanwhile this just contains the four textures we make and nothing else.
    // Offsetting the camera position is only allowed when drawing into one of these.
    private static HashSet<Texture> _internalLargeTextures = new HashSet<Texture>();

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



    public static void DestroyLargeExternalTextureData()
	{
        if (HiresRenderer.Instance is not { } renderer)
        {
            return;
        }

        foreach (var (smallTexture, largeTarget) in _largeExternalTextureMap)
        {
            largeTarget.Dispose();
            _largeExternalTextureMap.Remove(smallTexture);
        }

        Logger.Log(LogLevel.Info, "MotionSmoothingModule", "Disposed all large external buffers");

        _internalLargeTextures.Clear();
        _largeTextures.Clear();
	}

    public static void CreateLargeExternalTextureData()
	{
        if (HiresRenderer.Instance is not { } renderer)
        {
            return;
        }

        DestroyLargeExternalTextureData();

        _internalLargeTextures.Add(renderer.LargeLevelBuffer.Target);
        _internalLargeTextures.Add(renderer.LargeGameplayBuffer.Target);
        _internalLargeTextures.Add(renderer.LargeTempABuffer.Target);
        _internalLargeTextures.Add(renderer.LargeTempBBuffer.Target);

        _largeTextures.UnionWith(_internalLargeTextures);
	}


    protected override void Hook()
    {
        base.Hook();

        IL.Celeste.Level.Render += LevelRenderHook;

        On.Celeste.BloomRenderer.Apply += BloomRenderer_Apply;
        IL.Celeste.BloomRenderer.Apply += BloomRendererApplyHook;
        
		On.Celeste.GaussianBlur.Blur += GaussianBlur_Blur;
		IL.Celeste.GaussianBlur.Blur += GaussianBlurBlurHook;

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

            CreateLargeExternalTextureData();
        }

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Begin",
            new[]
            {
                typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState),
                typeof(DepthStencilState), typeof(RasterizerState), typeof(Effect), typeof(Matrix)
            })!, SpriteBatch_Begin));

        HookSpriteBatchDraw();

		AddHook(new Hook(typeof(SpriteBatch).GetMethod("PushSprite", MotionSmoothingModule.AllFlags)!,
            PushSpriteHook));

		AddHook(new Hook(typeof(SpriteBatch).GetMethod("End", Type.EmptyTypes)!, SpriteBatch_End));

        AddHook(new Hook(typeof(GraphicsDevice).GetMethod("SetRenderTargets",
            new[] { typeof(RenderTargetBinding[])
        })!, GraphicsDevice_SetRenderTargets));

        AddHook(new Hook(typeof(VirtualRenderTarget).GetMethod("Dispose", Type.EmptyTypes)!, VirtualRenderTarget_Dispose));

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

        IL.Celeste.Level.Render -= LevelRenderHook;

        On.Celeste.BloomRenderer.Apply -= BloomRenderer_Apply;
        IL.Celeste.BloomRenderer.Apply -= BloomRendererApplyHook;

        On.Celeste.GaussianBlur.Blur -= GaussianBlur_Blur;
		IL.Celeste.GaussianBlur.Blur -= GaussianBlurBlurHook;

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

        DestroyLargeExternalTextureData();
    }

    private static void Scene_Begin(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        // we hook Scene.Begin rather than Level.Begin to ensure it runs
        // after GameplayBuffers.Create, but before any calls to Entity.SceneBegin
        if (self is Level level)
        {
			var renderer = HiresRenderer.Create();
            level.Add(renderer);

            CreateLargeExternalTextureData();
        }

        orig(self);
    }

    private static void Level_End(On.Celeste.Level.orig_End orig, Level self)
    {
        HiresRenderer.Destroy();
        DestroyLargeExternalTextureData();

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
						Matrix.CreateTranslation(new Vector3(-SmoothedCameraPosition, 0.0f)) *
						Matrix.CreateRotationZ(camera.angle) *
						Matrix.CreateScale(new Vector3(camera.zoom, 1f)) *
						Matrix.CreateTranslation(new Vector3(
							new Vector2((int)Math.Floor(camera.origin.X), (int)Math.Floor(camera.origin.Y)), 0.0f));
		SmoothedCameraInverse = Matrix.Invert(SmoothedCameraMatrix);
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

        // if (cursor.TryGotoNext(MoveType.Before,
        //     instr => instr.MatchLdfld(typeof(Level), "HudRenderer")))
        // {
        //     cursor.EmitDelegate(DrawDebugBuffers);
        // }
    }

    private static void DrawDebugBuffers()
    {
        int index = 0;
        Engine.Instance.GraphicsDevice.SetRenderTarget(null);

        foreach (var (smallTexture, largeTarget) in _largeExternalTextureMap)
        {
            if (largeTarget == null)
            {
                _largeExternalTextureMap.Remove(smallTexture);
                continue;
            }

            if (smallTexture is not Texture2D texture2d)
            {
                return;
            }

            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone, null);
            Draw.SpriteBatch.Draw(largeTarget, new Vector2(320 * index, 0), Color.White);
			Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", $"{index}: {largeTarget.Width}x{largeTarget.Height}");
            Draw.SpriteBatch.End();

            index++;
        }
    }

    private static void PrepareLevelRender(Level level)
    {
        _offsetDrawing = false;
        _excludeFromOffsetDrawing.Clear();
        _scaleSpriteBatchBeginMatrices = true;
        _useHiresGaussianBlur = false;
        _allowParallaxOneBackgrounds = false;
        _currentlyRenderingBackground = true;
        _disableFloorFunctions = false;

        ComputeSmoothedCameraData(level);

        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            HiresRenderer.EnableLargeLevelBuffer();
        }

        else
        {
            HiresRenderer.DisableLargeLevelBuffer();
        }

		Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
    }

    private static void AfterLevelClear(Level level)
    {
        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            _disableFloorFunctions = true;
            SmoothCameraPosition(level);
        }

        else
        {
            _disableFloorFunctions = false;
        }
    }



    private static void DisableScaleSpriteBatchBeginMatrices()
    {
        _scaleSpriteBatchBeginMatrices = false;
    }

    private static void EnableScaleSpriteBatchBeginMatrices()
    {
        _scaleSpriteBatchBeginMatrices = true;
    }

    private static void DisableOffsetDrawing()
    {
        _offsetDrawing = false;
    }

    private static void EnableOffsetDrawing()
    {
        _offsetDrawing = true;
    }



    private static Matrix GetScaleMatrix()
    {
        return ScaleMatrix;
    }

    private static Matrix GetOffsetScaleMatrix()
    {
        Vector2 offset = GetCameraOffset();

        return Matrix.CreateTranslation(offset.X, offset.Y, 0f) * ScaleMatrix;
    }

    private static Matrix GetOffsetMatrix()
    {
        Vector2 offset = GetCameraOffset();

        return Matrix.CreateTranslation(offset.X, offset.Y, 0f);
    }

    private static Matrix GetScaledCameraMatrix(Level level)
    {
        return ScaleMatrix * level.Camera.Matrix;
    }

    private static Matrix GetHiresDisplayMatrix()
    {
		// Note that we leave the scale intact here! That's because the SpriteBatch.Draw
		// hook will strip it off later.
        return Matrix.CreateScale(6f * ZoomScaleMultiplier) * Engine.ScreenMatrix;
    }

    private static VirtualRenderTarget GetLargeTempBBuffer()
    {
        if (HiresRenderer.Instance is not { } renderer) return GameplayBuffers.TempB;

        return renderer.LargeTempBBuffer;
    }



    private static void BloomRenderer_Apply(On.Celeste.BloomRenderer.orig_Apply orig, BloomRenderer self, VirtualRenderTarget target, Scene scene)
    {
		HiresRenderer.EnableLargeTempABuffer();
        HiresRenderer.EnableLargeTempBBuffer();

        // This fixes issues with offsets happening in SJ's bloom masks.
        _excludeFromOffsetDrawing.Add(GameplayBuffers.Level.Target);

        orig(self, target, scene);

        _excludeFromOffsetDrawing.Remove(GameplayBuffers.Level.Target);

		HiresRenderer.DisableLargeTempABuffer();
        HiresRenderer.DisableLargeTempBBuffer();
    }

    // Effect cutouts need to be offset in order for them not to jitter.
    private static void BloomRendererApplyHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCall(typeof(GaussianBlur), "Blur")))
        {
            cursor.EmitDelegate(EnableOffsetDrawing);
        }

        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdsfld(typeof(BloomRenderer), "BlurredScreenToMask")))
        {
            cursor.EmitDelegate(DisableOffsetDrawing);
        }
    }



    private static Texture2D GaussianBlur_Blur(On.Celeste.GaussianBlur.orig_Blur orig, Texture2D texture, VirtualRenderTarget temp, VirtualRenderTarget output, float fade, bool clear, GaussianBlur.Samples samples, float sampleScale, GaussianBlur.Direction direction, float alpha)
	{
		if (_largeTextures.Contains(texture))
		{
			_useHiresGaussianBlur = true;
		}

        var outputTexture = orig(texture, temp, output, fade, clear, samples, sampleScale, direction, alpha);

		_useHiresGaussianBlur = false;

		return outputTexture;
    }

	private static void GaussianBlurBlurHook(ILContext il)
    {
        var cursor = new ILCursor(il);

		var getModifiedParameter = () => _useHiresGaussianBlur ? 6f : 1f;

		// When blurring something large, sample at points 6x farther away so they still
		// line up with pixels.
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdcR4(1f)))
        {
            cursor.Emit(OpCodes.Pop);
			cursor.EmitDelegate(getModifiedParameter);
        }

        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdcR4(1f)))
        {
            cursor.Emit(OpCodes.Pop);
			cursor.EmitDelegate(getModifiedParameter);
        }
    }



    private static void BackdropRenderer_Render(On.Celeste.BackdropRenderer.orig_Render orig, BackdropRenderer self, Scene scene)
    {
        if (HiresRenderer.Instance is not { } renderer)
        {
            orig(self, scene);
            return;
        }

        // The foreground gets rendered with an offset to move with the gameplay.
        if (!_currentlyRenderingBackground)
        {
            _offsetDrawing = true;
            orig(self, scene);
            _offsetDrawing = false;
            return;
        }

        // When rendering the background Hires, we don't need to composite anything
        // ourselves.
        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            // The background very much does *not* get an offset.
            orig(self, scene);
            _currentlyRenderingBackground = false;
            return;
        }


        _allowParallaxOneBackgrounds = false;
        orig(self, scene);

        // Go to the large level buffer for compositing time.
        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeLevelBuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

        // Draw the background into GameplayBuffers.Level. It'll get upscaled for us automatically.
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        // Draw the non-parallax one backgrounds.
        Draw.SpriteBatch.Draw(GameplayBuffers.Level, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();



        // Now draw the parallax-one backgrounds
        _allowParallaxOneBackgrounds = true;

        orig(self, scene);

        _currentlyRenderingBackground = false;

        HiresRenderer.EnableLargeLevelBuffer();
        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);
    }

    private static void GameplayRenderer_Render(On.Celeste.GameplayRenderer.orig_Render orig, GameplayRenderer self, Scene scene)
    {
		if (HiresRenderer.Instance is not { } renderer || scene is not Level level)
		{
			orig(self, scene);
			return;
		}

		if (!MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
        {
			Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBuffer);
			Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
			
			orig(self, scene);

			Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
			// The upscaling will happen automatically whenever we draw into the large
			// gameplay buffer, so we don't add a scale matrix.
			Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			Draw.SpriteBatch.Draw(renderer.SmallBuffer, Vector2.Zero, Color.White);
			Draw.SpriteBatch.End();

            return;
        }

		// If we're rendering with subpixels, we need to draw everything at 6x. We do
		// this by recreating the gameplay rendering loop, and every time we encounter
		// an entity that needs to be rendered at a fractional position, we draw everything
		// we have to renderer.LargeGameplayBuffer, clear the small buffer, draw
		// the single sprite at a precise position, clear it again, and then keep going.

		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBuffer);
		Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
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

					Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);

                    // The upscaling will happen automatically whenever we draw into the large
                    // gameplay buffer, so we don't add a scale matrix.
					Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
					Draw.SpriteBatch.Draw(renderer.SmallBuffer, Vector2.Zero, Color.White);
					Draw.SpriteBatch.End();

					Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBuffer);
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

					Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
					Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
					Draw.SpriteBatch.Draw(renderer.SmallBuffer, offset, Color.White);
					Draw.SpriteBatch.End();
					
					Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBuffer);
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
		Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
	
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        Draw.SpriteBatch.Draw(renderer.SmallBuffer, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
    }

    private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion)
    {
		if (HiresRenderer.Instance is not { } renderer || Engine.Scene is not Level level)
        {
			orig(source, map, hasDistortion);
            return;
        }
		
		_disableFloorFunctions = false;

		if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            UnsmoothCameraPosition(level);
        }

		var renderTargets = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets();



		if (!MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
		{
			// If we're not doing subpixel rendering, then we don't need to do that much.
			Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBuffer);
			Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
			
			orig(source, map, hasDistortion);

			Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);

            _offsetDrawing = true;
			Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			Draw.SpriteBatch.Draw(renderer.SmallBuffer, Vector2.Zero, Color.White);
			Draw.SpriteBatch.End();
            _offsetDrawing = false;

			return;
		}



		

		// Draw the distortion map into LargeTempA. It will get scaled automatically.
		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeTempABuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
	
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        Draw.SpriteBatch.Draw(map, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

		// Now draw this into whatever we were supposed to be drawing into, *with offset*.
        // Distort.Render draws source, which in this case is the large gameplay buffer.
        // That's totally fine, since it's big drawing into big, and so the scale will
        // get removed, leaving only the offset.
        _offsetDrawing = true;
		Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);
		orig(source, renderer.LargeTempABuffer, hasDistortion);
        _offsetDrawing = false;
    }



	private static void Glitch_Apply(On.Celeste.Glitch.orig_Apply orig, VirtualRenderTarget source, float timer, float seed, float amplitude)
	{
		HiresRenderer.EnableLargeTempABuffer();

		orig(source, timer, seed, amplitude);

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
        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
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
        if (_currentlyRenderingBackground)
        {
            bool isParallaxOne = self.Scroll.X == 1.0 && self.Scroll.Y == 1.0;

            if (isParallaxOne == _allowParallaxOneBackgrounds)
            {
                orig(self, scene);
            }

            return;
        }

        orig(self, scene);
    }



    public static void HudRenderer_RenderContent(On.Celeste.HudRenderer.orig_RenderContent orig, HudRenderer self, Scene scene)
    {
        if (scene is not Level level)
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
        if (!_scaleSpriteBatchBeginMatrices)
        {
            orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
            return;
        }

        _lastSpriteBatchBeginParams = (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);

        

        // If we're drawing to a large target, scale.
        if (_largeTextures.Contains(_currentRenderTarget))
        {
            transformMatrix = transformMatrix * ScaleMatrix;

            _currentlyScaling = true;
        }

        orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
    }


    // These are all five overloads that take a source rectangle. If, and only if, it's
    // specified when drawing a large texture, it needs to be scaled. We can't really do
    // this in the PushSprite hook, because it would require scaling sourceW and
    // sourceH, which we don't want to do if they're using their default values (i.e.
    // if this rectangle wasn't specified). NOTE: we do not do this if we're drawing
    // a small texture that's going to be replaced with a big one! Source rectangles are
	// on the scale [0, 1] and are computed as such by all of the draw overloads by dividing
	// by texture width, so only when the actual draw call is made with a large texture will
	// the source rectangle need to be scaled.
    private void HookSpriteBatchDraw()
    {
        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color) })!, SpriteBatch_Draw1));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float) })!, SpriteBatch_Draw2));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float) })!, SpriteBatch_Draw3));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", new[] { typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color) })!, SpriteBatch_Draw4));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", new[] { typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(SpriteEffects), typeof(float) })!, SpriteBatch_Draw5));
    }

    private static void SpriteBatch_Draw1(Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color> orig, SpriteBatch self, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color)
    {
        if (
			sourceRectangle is Rectangle rect
			&& _largeTextures.Contains(texture)
			&& rect.Right <= texture.Width / 6
			&& rect.Bottom <= texture.Height / 6
		) {
            Rectangle scaledRect = new Rectangle(6 * rect.X, 6 * rect.Y, 6 * rect.Width, 6 * rect.Height);
            orig(self, texture, position, scaledRect, color);
            return;
        }

        orig(self, texture, position, sourceRectangle, color);
    }

    private static void SpriteBatch_Draw2(Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color, float, Vector2, float, SpriteEffects, float> orig, SpriteBatch self, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
    {
        if (
			sourceRectangle is Rectangle rect
			&& _largeTextures.Contains(texture)
			&& rect.Right <= texture.Width / 6
			&& rect.Bottom <= texture.Height / 6
		) {
            Rectangle scaledRect = new Rectangle(6 * rect.X, 6 * rect.Y, 6 * rect.Width, 6 * rect.Height);
            orig(self, texture, position, scaledRect, color, rotation, origin, scale, effects, layerDepth);
            return;
        }

        orig(self, texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void SpriteBatch_Draw3(Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color, float, Vector2, Vector2, SpriteEffects, float> orig, SpriteBatch self, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
    {
        if (
			sourceRectangle is Rectangle rect
			&& _largeTextures.Contains(texture)
			&& rect.Right <= texture.Width / 6
			&& rect.Bottom <= texture.Height / 6
		) {
            Rectangle scaledRect = new Rectangle(6 * rect.X, 6 * rect.Y, 6 * rect.Width, 6 * rect.Height);
            orig(self, texture, position, scaledRect, color, rotation, origin, scale, effects, layerDepth);
            return;
        }

        orig(self, texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void SpriteBatch_Draw4(Action<SpriteBatch, Texture2D, Rectangle, Rectangle?, Color> orig, SpriteBatch self, Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color)
    {
        if (
			sourceRectangle is Rectangle rect
			&& _largeTextures.Contains(texture)
			&& rect.Right <= texture.Width / 6
			&& rect.Bottom <= texture.Height / 6
		) {
            Rectangle scaledRect = new Rectangle(6 * rect.X, 6 * rect.Y, 6 * rect.Width, 6 * rect.Height);
            orig(self, texture, destinationRectangle, scaledRect, color);
            return;
        }

        orig(self, texture, destinationRectangle, sourceRectangle, color);
    }

    private static void SpriteBatch_Draw5(Action<SpriteBatch, Texture2D, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float> orig, SpriteBatch self, Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
    {
        if (
			sourceRectangle is Rectangle rect
			&& _largeTextures.Contains(texture)
			&& rect.Right <= texture.Width / 6
			&& rect.Bottom <= texture.Height / 6
		) {
            Rectangle scaledRect = new Rectangle(6 * rect.X, 6 * rect.Y, 6 * rect.Width, 6 * rect.Height);
            orig(self, texture, destinationRectangle, scaledRect, color, rotation, origin, effects, layerDepth);
            return;
        }

        orig(self, texture, destinationRectangle, sourceRectangle, color, rotation, origin, effects, layerDepth);
    }



	private delegate void orig_PushSprite(SpriteBatch self, Texture2D texture, float sourceX, float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH, Color color, float originX, float originY, float rotationSin, float rotationCos, float depth, byte effects);

	private static void PushSpriteHook(orig_PushSprite orig, SpriteBatch self, Texture2D texture, float sourceX, float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH, Color color, float originX, float originY, float rotationSin, float rotationCos, float depth, byte effects)
    {
        if (texture == null)
		{
			orig(self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
			return;
		}

        // If you're drawing the small version of this texture, no you're not!
        if (_largeExternalTextureMap.TryGetValue(texture, out VirtualRenderTarget largeRenderTarget))
        {
            texture = largeRenderTarget.Target;
            destinationW *= 6;
            destinationH *= 6;
            destinationX *= 6;
            destinationY *= 6;
            originX *= 6;
            originY *= 6;
        }

        // If instead we're drawing a natively large texture to a large one or the screen with an offset
        // (e.g. SJ's color grade masks or TempA to its bloom masks), that offset needs to be scaled.
		// We do *not* scale the width and height because we aren't changing the size of the texture!
        else if ((_currentRenderTarget == null || _currentlyScaling) && _largeTextures.Contains(texture))
        {
            destinationX *= 6;
            destinationY *= 6;
            originX *= 6;
            originY *= 6;
        }



        float offsetDestinationX = destinationX;
        float offsetDestinationY = destinationY;

        // Apply the subpixel offset if needed. We only allow offsetting to our own buffers.
        if (_offsetDrawing && _internalLargeTextures.Contains(_currentRenderTarget) && !_excludeFromOffsetDrawing.Contains(_currentRenderTarget))
        {
            Vector2 offset = GetCameraOffset();
            offsetDestinationX = destinationX + offset.X * (_currentlyScaling ? 1 : 6);
            offsetDestinationY = destinationY + offset.Y * (_currentlyScaling ? 1 : 6);
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
                        Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, InverseScaleMatrix * matrix);

                        if (_offsetDrawing && _internalLargeTextures.Contains(_currentRenderTarget) && !_excludeFromOffsetDrawing.Contains(_currentRenderTarget))
                        {
                            Vector2 offset = GetCameraOffset();
                            offsetDestinationX = destinationX + offset.X * 6;
                            offsetDestinationY = destinationY + offset.Y * 6;
                        }

                        orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestinationX, offsetDestinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);

                        Draw.SpriteBatch.End();
                        Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix);
                    }
                }

                return;
			}

            if (_currentRenderTarget is not Texture2D texture2D)
            {
                orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestinationX, offsetDestinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
                return;
            }



            // If we get to this point, then we're drawing something large into something
            // small. Danger! We need to replace that small buffer with a larger one.
            HotCreateLargeBuffer(texture2D);

            if ((bool)_beginCalledField.GetValue(Draw.SpriteBatch))
            {
                // Since we're drawing something large into the new large buffer,
                // we ditch the scale exactly like above.
                if (_lastSpriteBatchBeginParams is var (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix))
                {	
                    Draw.SpriteBatch.End();
                    Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, InverseScaleMatrix * matrix);

                    // We completely skip the offset check since this can never be an internal
					// render target.

                    orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestinationX, offsetDestinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);

                    Draw.SpriteBatch.End();
                    Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix);
                }
            }

			return;
		}



        orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestinationX, offsetDestinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
    }



    private static bool HotCreateLargeBuffer(Texture2D smallTexture)
    {
        // We cap the dimensions here since the maximum allowable texture is
        // 4096x4096 (and this is close to that at 6x)
        if (smallTexture.Width > 640 || smallTexture.Height > 640)
        {
			// Ah well. Despite not being able to create a large buffer, this code path
			// is still useful: a common reason to want a buffer this big is to capture
			// what's drawn to the screen (ahem CelesteNet), in which case we will still
			// be ditching the scale above where this is called -- which is exactly correct
			// in that case!
            return false;
        }

        VirtualRenderTarget largeTarget = GameplayBuffers.Create(smallTexture.Width * 6, smallTexture.Height * 6);

        // Now we switch to that buffer and proceed. To preserve whatever was in here before,
        // we copy it into the large buffer. Since we haven't registered this buffer anywhere yet,
        // none of this will be hooked.
        Engine.Instance.GraphicsDevice.SetRenderTarget(largeTarget);

        bool inSpriteBatch = (bool)_beginCalledField.GetValue(Draw.SpriteBatch);

        if (inSpriteBatch && _lastSpriteBatchBeginParams is var (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix))
        {
            Draw.SpriteBatch.End();

            Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, ScaleMatrix);
            Draw.SpriteBatch.Draw(smallTexture, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            _largeExternalTextureMap[smallTexture] = largeTarget;
            _largeTextures.Add(largeTarget.Target);

            Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix);
        }

        else
        {
            // We have to duplicate this code since otherwise the Begin call will
            // overwrite the lastParams variable.
            Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, ScaleMatrix);
            Draw.SpriteBatch.Draw(smallTexture, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            _largeExternalTextureMap[smallTexture] = largeTarget;
            _largeTextures.Add(largeTarget.Target);
        }

        Logger.Log(LogLevel.Info, "MotionSmoothingModule", $"Hot created a {largeTarget.Target.Width}x{largeTarget.Target.Height} buffer! Total existing: {_largeExternalTextureMap.Count}");

        return true;
    }



	private static void SpriteBatch_End(Action<SpriteBatch> orig, SpriteBatch self)
	{
		_currentlyScaling = false;
		orig(self);
	}



    // Dispose large textures when the small one is disposed.
    private static void VirtualRenderTarget_Dispose(Action<VirtualRenderTarget> orig, VirtualRenderTarget self)
    {
        if (self.Target is Texture && _largeExternalTextureMap.TryGetValue(self.Target, out VirtualRenderTarget largeRenderTarget))
        {
            _largeExternalTextureMap.Remove(self.Target);

            if (largeRenderTarget?.Target is Texture2D texture2D)
            {
                Logger.Log(LogLevel.Info, "MotionSmoothingModule", $"Disposing a {texture2D.Width}x{texture2D.Height} hot-created buffer. Total left: {_largeExternalTextureMap.Count}");

                largeRenderTarget?.Dispose();
            }
        }

        orig(self);
    }



    private static Matrix GetScaleMatrixForDrawVertices()
    {
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

        return ScaleMatrix;
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