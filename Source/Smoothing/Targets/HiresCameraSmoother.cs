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
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class HiresCameraSmoother : ToggleableFeature<HiresCameraSmoother>
{
    public static float ZoomScale = 181f / 180f;

	public static Matrix ZoomMatrix = Matrix.CreateScale(ZoomScale);

	public static float Scale = 6f;

	private static bool _needSmallBufferSizeUpdate = false;

	// Flag set by the SpriteBatch.Begin hook when it it currently scaling by 6x.
	private static bool _currentlyScaling = false;
	private static Texture _currentRenderTarget;
    
    // Large textures can be added to this to receive the subpixel offset when drawn to.
    private static HashSet<Texture> _offsetWhenDrawnTo = new HashSet<Texture>();
    private static HashSet<Texture> _inverseOffsetWhenDrawnFrom = new HashSet<Texture>();

	private static bool _scaleSourceAndDestinationForLargeTextures = true;

	// A blunt tool for fixing weird mods like SpirialisHelper. When this is enabled,
	// spritebatch.begin will use the 181/180 scale matrix.
	private static bool _forceZoomDrawingToScreen = false;
    private static bool _currentlyRenderingBackground = false;
	private static bool _currentlyRenderingGameplay = false;
    private static bool _currentlyRenderingPlayerOnTopOfFlash = false;
    private static bool _allowParallaxOneBackgrounds = false;
    private static bool _interceptDistortRender = false;

    private enum DisableFloorFunctionsMode
    {
        Continuous,
        Rational,
        Integer
    }
    private static DisableFloorFunctionsMode _disableFloorFunctions = DisableFloorFunctionsMode.Integer;

    private static readonly FieldInfo _beginCalledField = typeof(SpriteBatch)
	.GetField("beginCalled", BindingFlags.NonPublic | BindingFlags.Instance);
    private static (SpriteSortMode, BlendState, SamplerState, DepthStencilState, RasterizerState, Effect, Matrix)? _lastSpriteBatchBeginParams;

	// This maps references to all external textures (i.e. created by other mods)
    // that are supposed to be scaled by 6x. We hot-swap those with large buffers
    // when they get other large buffers drawn into them.
	private static Dictionary<Texture, VirtualRenderTarget> _largeExternalTextureMap = new Dictionary<Texture, VirtualRenderTarget>();

    private static bool _enableLargeGameplayBuffer = false;
    private static bool _enableLargeLevelBuffer = false;
    private static bool _enableLargeTempABuffer = false;
    private static bool _enableLargeTempBBuffer = false;

	private const int MAX_EXTERNAL_BUFFERS = 32;

    // This is a set containing just the large versions of large textures, but
    // we also include the four that we enlarge (Level, Gameplay, TempA, and TempB).
    private static HashSet<Texture> _largeTextures = new HashSet<Texture>();

    // Meanwhile this just contains the four textures we make and nothing else.
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
		_needSmallBufferSizeUpdate = true;
	}

	public static void DisableHiresDistort()
	{
        GFX.FxDistort = _fxOrigDistort;
	}

    public static void DisableLargeGameplayBuffer()
    {
        _enableLargeGameplayBuffer = false;
    }



    public static void DestroyLargeTextures()
	{
        DestroyExternalLargeTextures();

        _internalLargeTextures.Clear();
        _largeTextures.Clear();
	}

	public static void DestroyExternalLargeTextures()
	{
        foreach (var (smallTexture, largeTarget) in _largeExternalTextureMap)
        {
            largeTarget.Dispose();
            _largeExternalTextureMap.Remove(smallTexture);
        }

        Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", "Disposed all large external buffers.");
	}

    public static void InitializeLargeTextures()
	{
		try { HiresRenderer.Create(); }
		catch (Exception) { return; }

        if (HiresRenderer.Instance is not { } renderer)
        {
            return;
        }

        DestroyLargeTextures();

        _internalLargeTextures.Add(renderer.LargeLevelBuffer.Target);
        _internalLargeTextures.Add(renderer.LargeGameplayBuffer.Target);
        _internalLargeTextures.Add(renderer.LargeTempABuffer.Target);
        _internalLargeTextures.Add(renderer.LargeTempBBuffer.Target);

        _largeTextures.UnionWith(_internalLargeTextures);

		_needSmallBufferSizeUpdate = true;
	}


    protected override void Hook()
    {
        base.Hook();

        IL.Celeste.Level.Render += LevelRenderHook;

        On.Celeste.BloomRenderer.Apply += BloomRenderer_Apply;

		On.Celeste.GaussianBlur.Blur += GaussianBlur_Blur;

		On.Celeste.BackdropRenderer.Update += BackdropRenderer_Update;
        On.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
        IL.Celeste.BackdropRenderer.Render += BackdropRendererRenderHook;

        On.Celeste.GameplayRenderer.Render += GameplayRenderer_Render;
		IL.Monocle.EntityList.Render += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnly += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnlyFullMatch += EntityListRenderHook;
        IL.Monocle.EntityList.RenderExcept += EntityListRenderHook;

        On.Celeste.Player.Render += Player_Render;

        On.Celeste.Distort.Render += Distort_Render;
		On.Celeste.Glitch.Apply += Glitch_Apply;

		IL.Celeste.SeekerBarrierRenderer.OnRenderBloom += SeekerBarrierRendererRenderHook;

        IL.Celeste.Godrays.Update += GodraysUpdateHook;

        On.Celeste.HudRenderer.RenderContent += HudRenderer_RenderContent;

        IL.Celeste.HiresRenderer.BeginRender += HiresRendererBeginRenderHook;
        IL.Celeste.Lookout.Hud.Render += LookoutHudRenderHook;

		On.Celeste.GameplayBuffers.Create += GameplayBuffers_Create;
        On.Monocle.Scene.Begin += Scene_Begin;
        On.Celeste.Level.End += Level_End;

        if (Engine.Scene is Level)
        {
            InitializeLargeTextures();
        }

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Begin",
            [
                typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState),
                typeof(DepthStencilState), typeof(RasterizerState), typeof(Effect), typeof(Matrix)
            ])!, SpriteBatch_Begin));

        HookSpriteBatchDraw();

		AddHook(new Hook(typeof(SpriteBatch).GetMethod("PushSprite", MotionSmoothingModule.AllFlags)!,
            PushSpriteHook));

		AddHook(new Hook(typeof(SpriteBatch).GetMethod("End", Type.EmptyTypes)!, SpriteBatch_End));

        AddHook(new Hook(typeof(GraphicsDevice).GetMethod("SetRenderTargets",
            [ typeof(RenderTargetBinding[])
        ])!, GraphicsDevice_SetRenderTargets));

        AddHook(new Hook(
            typeof(TextureCollection).GetProperty("Item").GetSetMethod(),
            TextureCollection_SetItem
        ));

        AddHook(new Hook(typeof(VirtualRenderTarget).GetMethod("Dispose", Type.EmptyTypes)!, VirtualRenderTarget_Dispose));

        HookDrawVertices<VertexPositionColor>();
        HookDrawVertices<VertexPositionColorTexture>();
        HookDrawVertices<LightingRenderer.VertexPositionColorMaskTexture>();

        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Floor), [typeof(Vector2)])!, FloorHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Ceiling), [typeof(Vector2)])!, CeilingHook));
        AddHook(new Hook(typeof(Calc).GetMethod(nameof(Calc.Round), [typeof(Vector2)])!, RoundHook));

		if (MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
		{
			EnableHiresDistort();
		}

		HookUnmaintainedMods();
    }

    protected override void Unhook()
    {
        base.Unhook();

        IL.Celeste.Level.Render -= LevelRenderHook;

        On.Celeste.BloomRenderer.Apply -= BloomRenderer_Apply;

        On.Celeste.GaussianBlur.Blur -= GaussianBlur_Blur;

		On.Celeste.BackdropRenderer.Update -= BackdropRenderer_Update;
        On.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
		IL.Celeste.BackdropRenderer.Render -= BackdropRendererRenderHook;

        On.Celeste.GameplayRenderer.Render -= GameplayRenderer_Render;
		IL.Monocle.EntityList.Render -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnly -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnlyFullMatch -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderExcept -= EntityListRenderHook;

        On.Celeste.Player.Render -= Player_Render;

        On.Celeste.Distort.Render -= Distort_Render;
		On.Celeste.Glitch.Apply -= Glitch_Apply;

		IL.Celeste.SeekerBarrierRenderer.OnRenderBloom -= SeekerBarrierRendererRenderHook;

        IL.Celeste.Godrays.Update -= GodraysUpdateHook;

        On.Celeste.HudRenderer.RenderContent -= HudRenderer_RenderContent;

        IL.Celeste.HiresRenderer.BeginRender -= HiresRendererBeginRenderHook;
        IL.Celeste.Lookout.Hud.Render -= LookoutHudRenderHook;

		On.Celeste.GameplayBuffers.Create -= GameplayBuffers_Create;
        On.Monocle.Scene.Begin -= Scene_Begin;
        On.Celeste.Level.End -= Level_End;

        foreach (var hook in _hooks)
            hook.Dispose();

        HiresRenderer.Destroy();
		DisableHiresDistort();

        DestroyLargeTextures();
    }

	private static void GameplayBuffers_Create(On.Celeste.GameplayBuffers.orig_Create orig)
    {
		orig();

		InitializeLargeTextures();
    }

    private static void Scene_Begin(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        // we hook Scene.Begin rather than Level.Begin to ensure it runs
        // after GameplayBuffers.Create, but before any calls to Entity.SceneBegin
        if (self is Level level && HiresRenderer.Instance is { } renderer)
		{
			level.Add(renderer);
		}

        orig(self);
    }

    private static void Level_End(On.Celeste.Level.orig_End orig, Level self)
    {
        HiresRenderer.Destroy();
        DestroyLargeTextures();

        orig(self);
    }

    public static Vector2 GetCameraOffset()
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
        return ZoomScale;
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



        if (cursor.TryGotoNext(MoveType.Before,
            instr => instr.MatchCall(typeof(Distort), "Render")))
        {
            cursor.EmitDelegate(enableInterceptDistortRender);
            cursor.Index++;
            cursor.EmitDelegate(disableInterceptDistortRender);


            static void enableInterceptDistortRender()
            {
                _interceptDistortRender = true;
            }

            static void disableInterceptDistortRender()
            {
                _interceptDistortRender = false;
            }
        }



        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdfld<Level>("flashDrawPlayer")))
        {
            cursor.EmitDelegate(FlagCurrentlyRenderingPlayerOnTopOfFlash);
        }



        // // Debug: disable level clearing to be able to draw buffers
        // // opaquely to the screen for debugging.
        // if (cursor.TryGotoNext(
        //     i => i.MatchCall(typeof(Color), "get_Black")
        // )) {
        //     cursor.Index -= 2;
        //     cursor.RemoveRange(4);
        // }



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

        // We can't On hook the SubHudRenderer, so we do it like this.
        if (cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchLdfld<Level>("SubHudRenderer")))
        {
            if (cursor.TryGotoNext(MoveType.Before,
                instr => instr.MatchCallvirt<Renderer>("Render")))
            {
                cursor.EmitLdarg0();
                cursor.EmitDelegate(SmoothCameraPosition);

                cursor.Index++;

                cursor.EmitLdarg0();
                cursor.EmitDelegate(UnsmoothCameraPosition);
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
		if (_largeExternalTextureMap.Count > MAX_EXTERNAL_BUFFERS)
		{
			Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", $"Too many large external buffers! ({_largeExternalTextureMap.Count})");
			DestroyExternalLargeTextures();
		}
        
        _offsetWhenDrawnTo.Clear();
        _allowParallaxOneBackgrounds = false;
        _currentlyRenderingBackground = true;
        _currentlyRenderingPlayerOnTopOfFlash = false;
        _disableFloorFunctions = DisableFloorFunctionsMode.Integer;
        _interceptDistortRender = false;

        ComputeSmoothedCameraData(level);

        _enableLargeLevelBuffer = MotionSmoothingModule.Settings.RenderBackgroundHires;
		_enableLargeGameplayBuffer = false;
    }

    private static void AfterLevelClear(Level level)
    {
        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            _disableFloorFunctions = DisableFloorFunctionsMode.Rational;
        }

        else
        {
            _disableFloorFunctions = DisableFloorFunctionsMode.Integer;
        }

		SmoothCameraPosition(level);
    }



    private static void FlagCurrentlyRenderingPlayerOnTopOfFlash()
    {
		_currentlyRenderingPlayerOnTopOfFlash = true;
    }



    private static Matrix GetHiresDisplayMatrix()
    {
		// Note that we leave the scale intact here! That's because the SpriteBatch.Draw
		// hook will strip it off later.

		// This is also the one single place that we use 6f instead of the actual scale value.
		// This is to account for ExCameraDynamics adjusting the scale value itself -- we leave
		// this alone to prevent the whole level from being stuck in the top-left.
        return Matrix.CreateScale(6f * ZoomScale) * Engine.ScreenMatrix;
    }

    

    private static void HideStretchedLevelEdges()
    {
        if (HiresRenderer.Instance is not { } renderer)
        {
            return;
        }

        // This is kind of a goofy fix. We extend the level buffer down and right, since
		// otherwise there's a gap of background after the gameplay ends (since it's offset). We use
		// the level buffer itself because it's past the foreground layer, and we can't *just* extend
		// like this because we're drawing weird offset-pixel stuff in general. This whole dance is 
		// necessary because the blurring from the bloom can still reach back up into the visible section!

        var renderTargets = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets();

        // Copy the level buffer into tempA since we can't draw from a texture into itself.
        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeTempABuffer);

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		Draw.SpriteBatch.Draw(renderer.LargeLevelBuffer, Vector2.Zero, Color.White);
		Draw.SpriteBatch.End();

		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeLevelBuffer);

		Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);

		int width = renderer.LargeLevelBuffer.Width;
		int height = renderer.LargeLevelBuffer.Height;

		Vector2 offset = -GetCameraOffset() * Scale;
		int gapX = (int)Math.Ceiling(offset.X);
		int gapY = (int)Math.Ceiling(offset.Y);

		_scaleSourceAndDestinationForLargeTextures = false;

		if (gapX > 0)
		{
			var sourceRectangle = new Rectangle(width - gapX - 1, 0, 1, height - gapY);
			var destinationRectangle = new Rectangle(width - gapX, 0, gapX, height - gapY);
			Draw.SpriteBatch.Draw(renderer.LargeTempABuffer, destinationRectangle, sourceRectangle, Color.White);
		}

		// Bottom edge: last contentful row → stretched to fill gap
		if (gapY > 0)
		{
			var sourceRectangle = new Rectangle(0, height - gapY - 1, width - gapX, 1);
			var destinationRectangle = new Rectangle(0, height - gapY, width - gapX, gapY);
			Draw.SpriteBatch.Draw(renderer.LargeTempABuffer, destinationRectangle, sourceRectangle, Color.White);
		}

		// Corner: single pixel stretched to fill
		if (gapX > 0 && gapY > 0)
		{
			var sourceRectangle = new Rectangle(width - gapX - 1, height - gapY - 1, 1, 1);
			var destinationRectangle = new Rectangle(width - gapX, height - gapY, gapX, gapY);
			Draw.SpriteBatch.Draw(renderer.LargeTempABuffer, destinationRectangle, sourceRectangle, Color.White);
		}

		_scaleSourceAndDestinationForLargeTextures = true;

		Draw.SpriteBatch.End();

		Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);
    }
    


    // In order to allow other mods' code to mess with the bloom as they see fit, as well
    // as support vanilla's desire to draw cutouts and entities into the bloom mask at positions
    // corresponding to gameplay objects (which we've offset), we instead draw all entities like
    // normal, but draw the level with an *inverse* offset to line up the gameplay, and then draw
    // that whole thing back into the level buffer with a regular offset to line it up with the
    // game again.
    private static void BloomRenderer_Apply(On.Celeste.BloomRenderer.orig_Apply orig, BloomRenderer self, VirtualRenderTarget target, Scene scene)
    {
		if (scene is not Level level || HiresRenderer.Instance is not { } renderer)
		{
			orig(self, target, scene);
			return;
		}

		UnsmoothCameraPosition(level);

        _enableLargeTempABuffer = true;
        _enableLargeTempBBuffer = true;

        HideStretchedLevelEdges();

        _offsetWhenDrawnTo.Clear();
        _offsetWhenDrawnTo.Add(renderer.LargeLevelBuffer);

        _inverseOffsetWhenDrawnFrom.Clear();
        // This is a lil weird -- it's what the output of the gaussian blur 
        // (i.e. GameplayBuffers.Level) becomes after it gets scaled up
        // by the time we're checking for what's in here.
        _inverseOffsetWhenDrawnFrom.Add(renderer.LargeLevelBuffer);

        orig(self, target, scene);

        _offsetWhenDrawnTo.Clear();
        _inverseOffsetWhenDrawnFrom.Clear();

        if (!MotionSmoothingModule.Settings.HideStretchedEdges)
        {
            // We only do this a second time here because we're covering up the gap where the bloom was left
            // which will be completely hidden by the zoom, unlike the blooming edges from before.
            HideStretchedLevelEdges();
        }

		_enableLargeTempABuffer = false;
        _enableLargeTempBBuffer = false;

		SmoothCameraPosition(level);
    }



    private static Texture2D GaussianBlur_Blur(On.Celeste.GaussianBlur.orig_Blur orig, Texture2D texture, VirtualRenderTarget temp, VirtualRenderTarget output, float fade, bool clear, GaussianBlur.Samples samples, float sampleScale, GaussianBlur.Direction direction, float alpha)
	{
		if (HiresRenderer.Instance is not { } renderer)
		{
			return orig(texture, temp, output, fade, clear, samples, sampleScale, direction, alpha);
		}
        
        if (GetPotentiallyLargeTexture(texture) is Texture2D largeTexture2D)
        {
            texture = largeTexture2D;
        }

        if (GetLargeTargetOrNull(output) is VirtualRenderTarget largeTarget)
        {
            output = largeTarget;
        }

		// This is a really blunt fix, but for things like Extended Variants that blur at small scales,
		// they tend to look gross since they blur the sharp pixel boundaries. This just disables them
		// when the scale is small enough that it wouldn't be noticible at low res anyway.
		if (_largeTextures.Contains(texture))
		{
			if (sampleScale < 0.175f)
			{
				sampleScale = 0;
			}

			sampleScale *= Scale;
		}

		if (texture.Width != temp.Width || texture.Height != temp.Height)
		{
			renderer.GaussianBlurTempBuffer.Width = texture.Width;
			renderer.GaussianBlurTempBuffer.Height = texture.Height;
			renderer.GaussianBlurTempBuffer.Reload();
			temp = renderer.GaussianBlurTempBuffer;
		}

		return orig(texture, temp, output, fade, clear, samples, sampleScale, direction, alpha);
    }

	

	private static void BackdropRenderer_Update(On.Celeste.BackdropRenderer.orig_Update orig, BackdropRenderer self, Scene scene)
	{
        if (MotionSmoothingModule.Settings.RenderBackgroundHires)
        {
            _disableFloorFunctions = DisableFloorFunctionsMode.Rational;
        }

		orig(self, scene);

		_disableFloorFunctions = DisableFloorFunctionsMode.Integer;
	}

    private static void BackdropRenderer_Render(On.Celeste.BackdropRenderer.orig_Render orig, BackdropRenderer self, Scene scene)
    {
        if (
            HiresRenderer.Instance is not { } renderer
            || MotionSmoothingModule.Settings.RenderBackgroundHires
            || !_currentlyRenderingBackground
            || (
                _currentRenderTarget != GameplayBuffers.Level.Target
                && _currentRenderTarget != renderer.LargeLevelBuffer.Target
            )
        ) {
            // The foreground gets rendered like normal, and the smoothed camera position automatically lines it
            // up with the gameplay. We don't menually offset this because then parallax foregrounds don't work.
            // Similarly, when rendering the background Hires, we don't need to composite anything ourselves.
            _disableFloorFunctions = DisableFloorFunctionsMode.Rational;

            orig(self, scene);

            _disableFloorFunctions = DisableFloorFunctionsMode.Integer;

            return;
        }

        _enableLargeLevelBuffer = false;
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
        _disableFloorFunctions = DisableFloorFunctionsMode.Rational;

		_offsetWhenDrawnTo.Clear();
        _offsetWhenDrawnTo.Add(renderer.LargeLevelBuffer);

        orig(self, scene);

		_offsetWhenDrawnTo.Clear();

        _enableLargeLevelBuffer = true;
        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);

        _disableFloorFunctions = DisableFloorFunctionsMode.Integer;
    }

	private static void BackdropRendererRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

		// Emit some IL to add a conditional for drawing each backdrop, so that we can offset only
		// the parallax-one backdrops.

		var sceneLocal = new VariableDefinition(il.Import(typeof(Scene)));
		il.Body.Variables.Add(sceneLocal);
		
		if (cursor.TryGotoNext(MoveType.Before,
			i => i.MatchCallvirt<Backdrop>("Render")))
		{
			// Stack: [Backdrop, Scene]
			
			ILLabel doRender = cursor.DefineLabel();
			ILLabel skip = cursor.DefineLabel();
			
			// Store Scene, dup Backdrop for our check
			cursor.Emit(OpCodes.Stloc, sceneLocal);   // Stack: [Backdrop]
			cursor.Emit(OpCodes.Dup);                  // Stack: [Backdrop, Backdrop]
			cursor.EmitDelegate(ShouldRenderBackdrop);  // Stack: [Backdrop, bool]
			cursor.Emit(OpCodes.Brtrue, doRender);     // Stack: [Backdrop]
			
			// Skip path: pop the Backdrop and jump past callvirt
			cursor.Emit(OpCodes.Pop);                  // Stack: []
			cursor.Emit(OpCodes.Br, skip);
			
			// Render path: restore Scene and fall through to callvirt
			cursor.MarkLabel(doRender);
			cursor.Emit(OpCodes.Ldloc, sceneLocal);    // Stack: [Backdrop, Scene]
			
			// Move past the callvirt to mark the skip label
			cursor.GotoNext(MoveType.After, i => i.MatchCallvirt<Backdrop>("Render"));
			cursor.MarkLabel(skip);
		}
    }

	private static bool ShouldRenderBackdrop(Backdrop self)
	{
		if (
			MotionSmoothingModule.Settings.RenderBackgroundHires
			|| !_currentlyRenderingBackground
		) {
			return true;
		}

		bool isParallaxOne = self is Parallax && self.Scroll.X == 1.0 && self.Scroll.Y == 1.0;

		return _allowParallaxOneBackgrounds == isParallaxOne;
	}



    private static void GameplayRenderer_Render(On.Celeste.GameplayRenderer.orig_Render orig, GameplayRenderer self, Scene scene)
    {
		if (HiresRenderer.Instance is not { } renderer || !MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
		{
			orig(self, scene);
			return;
		}

        _enableLargeGameplayBuffer = false;

		// If we're rendering with subpixels, we need to draw everything at 6x. Every
		// time we encounter an entity that needs to be rendered at a fractional position
		// (a player or held holdable) we draw everything we have to renderer.LargeGameplayBuffer,
		// clear the small buffer, draw the single sprite at a precise position, clear
		// it again, and then keep going.
		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);
		Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

		Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
		
		_currentlyRenderingGameplay = true;
		orig(self, scene);
		_currentlyRenderingGameplay = false;

		// Draw the topmost things that are left (we got the others in Player.Render)
		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);
	
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

		if (_needSmallBufferSizeUpdate)
		{
			_needSmallBufferSizeUpdate = false;

			_fxHiresDistort?.Parameters["bufferSize"]?.SetValue(new Vector2(
				GameplayBuffers.Gameplay.Width,
				GameplayBuffers.Gameplay.Height
			));
		}

		_enableLargeGameplayBuffer = true;
    }

	private static void EntityListRenderHook(ILContext il)
	{
		var cursor = new ILCursor(il);
		
		if (cursor.TryGotoNext(MoveType.Before,
			i => i.MatchCallvirt<Entity>("Render")))
		{
			ILLabel doNormalRender = cursor.DefineLabel();
			ILLabel skip = cursor.DefineLabel();
			
			// Stack: [Entity]
			cursor.Emit(OpCodes.Dup);                                      // [Entity, Entity]
			cursor.EmitDelegate<Func<Entity, bool>>(ShouldInterceptEntityRender);      // [Entity, bool]
			cursor.Emit(OpCodes.Brfalse, doNormalRender);                  // [Entity]
			
			// Intercept path: call custom delegate, skip callvirt
			cursor.EmitDelegate<Action<Entity>>(RenderEntityAtSubpixelPosition);             // []
			cursor.Emit(OpCodes.Br, skip);
			
			// Normal path label (right before original callvirt)
			cursor.MarkLabel(doNormalRender);
			
			// Move past callvirt to place skip label
			cursor.GotoNext(MoveType.After, i => i.MatchCallvirt<Entity>("Render"));
			cursor.MarkLabel(skip);
		}
	}

	private static bool ShouldInterceptEntityRender(Entity self)
	{
		if (
			!_currentlyRenderingGameplay
			|| !MotionSmoothingModule.Settings.RenderMadelineWithSubpixels
		) {
			return false;
		}

		var player = MotionSmoothingHandler.Instance.Player;

		return self == player
			|| player?.Holding?.Entity == self // A currently-held holdable
			|| self is Strawberry { Golden: true } strawberry && strawberry.Follower.Leader != null; // A golden attacked to the player

	}

	private static void RenderEntityAtSubpixelPosition(Entity self)
	{
		if (HiresRenderer.Instance is not { } renderer)
		{
			return;
		}

        Vector2 offset;
        Vector2 spriteOffset = Vector2.Zero;

		

		if (self is Strawberry strawberry)
		{
            IPositionSmoothingState state = MotionSmoothingHandler.Instance.GetState(self) as IPositionSmoothingState;
            offset = state.SmoothedRealPosition - state.SmoothedRealPosition.Round();

			// The visual-only bobbing animation of strawberry interacts really badly
			// with position smoothing, so we just disable it, add the offset into ours,
			// and then put it back later (necessary since it only gets set at 60fps).
			spriteOffset = new Vector2(strawberry.sprite.X, strawberry.sprite.Y);

			offset += spriteOffset;
			
			strawberry.sprite.X = 0;
			strawberry.sprite.Y = 0;
		}

         // The player or a holdable
        else
		{
            IPositionSmoothingState state = MotionSmoothingHandler.Instance.GetState(
                MotionSmoothingHandler.Instance.Player
            ) as IPositionSmoothingState;
            offset = state.SmoothedRealPosition - state.SmoothedRealPosition.Round();

            if (!PlayerSmoother.AllowSubpixelRenderingX)
			{
				offset.X = 0;
			}

			if (!PlayerSmoother.AllowSubpixelRenderingY)
			{
				offset.Y = 0;
			}
		}

        if (Engine.Scene is Level { Transitioning: true } or { Paused: true })
		{
			offset = Vector2.Zero;
		}

		// Render the things below this entity.
		GameplayRenderer.End();

		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);

		// Shoutout to the single worst bug I have encountered in writing this godforsaken mod
		Strategies.PushSpriteSmoother.TemporarilyDisablePushSpriteSmoothing = true;
		Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
		Draw.SpriteBatch.End();
		Strategies.PushSpriteSmoother.TemporarilyDisablePushSpriteSmoothing = false;
		


		Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
		Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

		GameplayRenderer.Begin();

		// Now render just this entity and copy it in at subpixel-precise position
		self.Render();

		GameplayRenderer.End();



		// If we messed with a strawberry, put it back
		if (self is Strawberry strawberry2)
		{
			strawberry2.sprite.X = spriteOffset.X;
			strawberry2.sprite.Y = spriteOffset.Y;
		}
		

		Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.LargeGameplayBuffer);

		Strategies.PushSpriteSmoother.TemporarilyDisablePushSpriteSmoothing = true;
		Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, offset, Color.White);
		Draw.SpriteBatch.End();
		Strategies.PushSpriteSmoother.TemporarilyDisablePushSpriteSmoothing = false;
		
		Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
		Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

		// Keep going for things above this entity
		GameplayRenderer.Begin();
	}

    private static void Player_Render(On.Celeste.Player.orig_Render orig, Player self)
    {
        if (HiresRenderer.Instance is not { } renderer || !_currentlyRenderingPlayerOnTopOfFlash)
        {
            orig(self);
            return;
        }

        var renderTargets = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets();



        var state = MotionSmoothingHandler.Instance.GetState(self) as IPositionSmoothingState;

		Vector2 offset = state.SmoothedRealPosition - state.SmoothedRealPosition.Round();

        if (!PlayerSmoother.AllowSubpixelRenderingX)
        {
            offset.X = 0;
        }

        if (!PlayerSmoother.AllowSubpixelRenderingY)
        {
            offset.Y = 0;
        }



        Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBuffer);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

        orig(self);

        Draw.SpriteBatch.End();

        Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);

        Strategies.PushSpriteSmoother.TemporarilyDisablePushSpriteSmoothing = true;
		Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
		Draw.SpriteBatch.End();
		Strategies.PushSpriteSmoother.TemporarilyDisablePushSpriteSmoothing = false;



        // There's still a SpriteBatch.End coming, so we throw in an extra begin
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);

        _currentlyRenderingPlayerOnTopOfFlash = false;
    }

	

    private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion)
    {
        if (HiresRenderer.Instance is not { } renderer || !_interceptDistortRender)
        {
			orig(source, map, hasDistortion);
            return;
        }
		
		_currentlyRenderingBackground = false;

		var renderTargets = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets();



		if (!MotionSmoothingModule.Settings.RenderMadelineWithSubpixels)
		{
			// If we're not doing subpixel rendering, then we don't need to do that much.
			Engine.Instance.GraphicsDevice.SetRenderTarget(renderer.SmallBuffer);
			Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
			
			orig(source, map, hasDistortion);

			Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);

            _offsetWhenDrawnTo.Clear();
            foreach (var target in renderTargets)
            {
                _offsetWhenDrawnTo.Add(target.RenderTarget);
            }

			Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			Draw.SpriteBatch.Draw(renderer.SmallBuffer, Vector2.Zero, Color.White);
			Draw.SpriteBatch.End();

            _offsetWhenDrawnTo.Clear();

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
        _offsetWhenDrawnTo.Clear();
        foreach (var target in renderTargets)
        {
            _offsetWhenDrawnTo.Add(target.RenderTarget);
        }

		Engine.Instance.GraphicsDevice.SetRenderTargets(renderTargets);
		orig(source, renderer.LargeTempABuffer, hasDistortion);
        _offsetWhenDrawnTo.Clear();
    }



	private static void Glitch_Apply(On.Celeste.Glitch.orig_Apply orig, VirtualRenderTarget source, float timer, float seed, float amplitude)
	{
		if (Engine.Scene is not Level level)
		{
			orig(source, timer, seed, amplitude);
			return;
		}

		_disableFloorFunctions = DisableFloorFunctionsMode.Integer;
		UnsmoothCameraPosition(level);

		_enableLargeTempABuffer = true;

		orig(source, timer, seed, amplitude);

		_enableLargeTempABuffer = false;
	}



	private static void SeekerBarrierRendererRenderHook(ILContext il)
	{
		ILCursor cursor = new ILCursor(il);

		// Find Draw.Line(Vector2, Vector2, Color)
		if (cursor.TryGotoNext(MoveType.Before,
			instr => instr.MatchCall(typeof(Draw).GetMethod("Line", [typeof(Vector2), typeof(Vector2), typeof(Color)]))))
		{
			// Stack is [Vector2, Vector2, Color]. Store the Color, round the Vector2, restore the Color.
			var colorLocal = new VariableDefinition(il.Import(typeof(Color)));
			il.Body.Variables.Add(colorLocal);

			cursor.Emit(OpCodes.Stloc, colorLocal);
			cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("Round", [typeof(Vector2)]));
			cursor.Emit(OpCodes.Ldloc, colorLocal);
		}
	}

    
    // This is a complicated solution to fix GFX.DrawVertices at high res.
    // We have to manually compute the shape of the polygon to simulate
    // a 320x180 buffer, but the actual logic is still pretty light on the CPU
    // and the GPU call is functionally just as fast as the original.
    private static class PixelatedRenderer
    {
        private static VertexPositionColor[] outputVertices = new VertexPositionColor[4096];
        private static int outputCount;

        public static void DrawPixelated(Matrix matrix, VertexPositionColor[] vertices, int vertexCount)
        {
            outputCount = 0;

            float offsetX = 0f, offsetY = 0f;

            for (int i = 0; i + 2 < vertexCount; i += 3)
            {
                bool newGroup = (i == 0);

                if (!newGroup)
                {
                    newGroup = true;

                    for (int a = 0; a < 3 && newGroup; a++)
                    {
                        for (int b = 0; b < 3 && newGroup; b++)
                        {
                            if (vertices[i + a].Position == vertices[i - 3 + b].Position)
                            {
                                newGroup = false;
                            }
                        }
                    }
                }

                if (newGroup)
                {
                    float anchorX = vertices[i].Position.X;
                    float anchorY = vertices[i].Position.Y;
                    offsetX = anchorX - (float)Math.Floor(anchorX);
                    offsetY = anchorY - (float)Math.Floor(anchorY);
                }

                Vector3 off = new Vector3(offsetX, offsetY, 0f);

                RasterizeTriangle(
                    vertices[i].Position - off, vertices[i].Color,
                    vertices[i + 1].Position - off, vertices[i + 1].Color,
                    vertices[i + 2].Position - off, vertices[i + 2].Color,
                    offsetX, offsetY
                );
            }

            if (outputCount > 0)
            {
                GFX.DrawVertices(matrix * Matrix.CreateScale(1f / Scale), outputVertices, outputCount);
            }
        }

        private static void RasterizeTriangle(
            Vector3 p0, Color c0,
            Vector3 p1, Color c1,
            Vector3 p2, Color c2,
            float offsetX, float offsetY)
        {
            float x0 = p0.X, y0 = p0.Y;
            float x1 = p1.X, y1 = p1.Y;
            float x2 = p2.X, y2 = p2.Y;

            if (y0 > y1) { Swap(ref x0, ref x1); Swap(ref y0, ref y1); Swap(ref c0, ref c1); }
            if (y0 > y2) { Swap(ref x0, ref x2); Swap(ref y0, ref y2); Swap(ref c0, ref c2); }
            if (y1 > y2) { Swap(ref x1, ref x2); Swap(ref y1, ref y2); Swap(ref c1, ref c2); }

            float denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (Math.Abs(denom) < 1e-6f) return;
            float invDenom = 1f / denom;

            bool uniformColor = (c0 == c1 && c1 == c2);

            int lowResWidth = GameplayBuffers.Gameplay.Width;
            int lowResHeight = GameplayBuffers.Gameplay.Height;

            int minY = Math.Max((int)Math.Ceiling(y0 - 0.5f), -1);
            int maxY = Math.Min((int)Math.Floor(y2 - 0.5f), lowResHeight - 1);

            for (int py = minY; py <= maxY; py++)
            {
                float cy = py + 0.5f;

                float leftX = float.MaxValue;
                float rightX = float.MinValue;

                IntersectEdge(x0, y0, x1, y1, cy, ref leftX, ref rightX);
                IntersectEdge(x1, y1, x2, y2, cy, ref leftX, ref rightX);
                IntersectEdge(x0, y0, x2, y2, cy, ref leftX, ref rightX);

                if (leftX > rightX) continue;

                int minX = Math.Max((int)Math.Ceiling(leftX - 0.5f), -1);
                int maxX = Math.Min((int)Math.Ceiling(rightX - 0.5f) - 1, lowResWidth - 1);

                if (uniformColor)
                {
                    for (int px = minX; px <= maxX; px++)
                    {
                        EmitQuad(px, py, c0, offsetX, offsetY);
                    }
                }

                else
                {
                    float cx0 = minX + 0.5f;

                    float w0 = ((y1 - y2) * (cx0 - x2) + (x2 - x1) * (cy - y2)) * invDenom;
                    float w1 = ((y2 - y0) * (cx0 - x2) + (x0 - x2) * (cy - y2)) * invDenom;

                    float dw0 = (y1 - y2) * invDenom;
                    float dw1 = (y2 - y0) * invDenom;

                    for (int px = minX; px <= maxX; px++)
                    {
                        float w2 = 1f - w0 - w1;

                        Color color = new Color(
                            (byte)MathHelper.Clamp(c0.R * w0 + c1.R * w1 + c2.R * w2, 0, 255),
                            (byte)MathHelper.Clamp(c0.G * w0 + c1.G * w1 + c2.G * w2, 0, 255),
                            (byte)MathHelper.Clamp(c0.B * w0 + c1.B * w1 + c2.B * w2, 0, 255),
                            (byte)MathHelper.Clamp(c0.A * w0 + c1.A * w1 + c2.A * w2, 0, 255)
                        );

                        EmitQuad(px, py, color, offsetX, offsetY);

                        w0 += dw0;
                        w1 += dw1;
                    }
                }
            }
        }

        private static void IntersectEdge(
            float x0, float y0, float x1, float y1,
            float y, ref float leftX, ref float rightX)
        {
            if ((y0 <= y && y1 > y) || (y1 <= y && y0 > y))
            {
                float t = (y - y0) / (y1 - y0);
                float x = x0 + t * (x1 - x0);

                if (x < leftX) leftX = x;
                if (x > rightX) rightX = x;
            }
        }

        private static void EmitQuad(int px, int py, Color color, float offsetX, float offsetY)
        {
            if (outputCount + 6 > outputVertices.Length)
            {
                Array.Resize(ref outputVertices, outputVertices.Length * 2);
            }

            float sx = (px + offsetX) * Scale;
            float sy = (py + offsetY) * Scale;
            float ex = sx + Scale;
            float ey = sy + Scale;

            var tl = new VertexPositionColor(new Vector3(sx, sy, 0f), color);
            var tr = new VertexPositionColor(new Vector3(ex, sy, 0f), color);
            var bl = new VertexPositionColor(new Vector3(sx, ey, 0f), color);
            var br = new VertexPositionColor(new Vector3(ex, ey, 0f), color);

            outputVertices[outputCount++] = tl;
            outputVertices[outputCount++] = bl;
            outputVertices[outputCount++] = tr;
            outputVertices[outputCount++] = bl;
            outputVertices[outputCount++] = tr;
            outputVertices[outputCount++] = br;
        }

        private static void Swap<T>(ref T a, ref T b)
        {
            T tmp = a;
            a = b;
            b = tmp;
        }
    }

    private static void GodraysUpdateHook(ILContext il)
    {
        ILCursor cursor = new ILCursor(il);

        int vec2Local = -1;
        int num2Local = -1;
        int num3Local = -1;

        #pragma warning disable CL0006 // Multiple predicates to ILCursor.(Try)Goto*
        if (cursor.TryGotoNext(MoveType.Before,
            i => i.MatchLdloca(out vec2Local),
            i => i.MatchLdloc(out num2Local),
            i => i.Match(OpCodes.Conv_I4),
            i => i.Match(OpCodes.Conv_R4),
            i => i.MatchLdloc(out num3Local),
            i => i.Match(OpCodes.Conv_I4),
            i => i.Match(OpCodes.Conv_R4),
            i => i.MatchCall<Vector2>(".ctor"))
        ) {
            #pragma warning disable CL0005 // ILCursor.Remove or RemoveRange used
            cursor.RemoveRange(8);
            #pragma warning restore CL0005 // ILCursor.Remove or RemoveRange used

            cursor.Emit(OpCodes.Ldloc, il.Body.Variables[num2Local]);
            cursor.Emit(OpCodes.Ldloc, il.Body.Variables[num3Local]);
            cursor.Emit(OpCodes.Newobj, typeof(Vector2).GetConstructor(new[] { typeof(float), typeof(float) }));
            cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("Floor", new[] { typeof(Vector2) }));
            cursor.Emit(OpCodes.Stloc, il.Body.Variables[vec2Local]);
        }
        #pragma warning restore CL0006 // Multiple predicates to ILCursor.(Try)Goto*
    }



    public static void HudRenderer_RenderContent(On.Celeste.HudRenderer.orig_RenderContent orig, HudRenderer self, Scene scene)
    {
        if (scene is not Level level)
        {
            orig(self, scene);
            return;
        }

        Vector2 oldCameraPosition = level.Camera.Position;
        SmoothCameraPosition(level);
        _disableFloorFunctions = DisableFloorFunctionsMode.Continuous;

        orig(self, scene);
        
        // This needs to not be a call to UnsmoothCameraPosition: for whatever reason,
        // that causes bizarre camera locking after resizing the window.
        level.Camera.Position = oldCameraPosition;
        _disableFloorFunctions = DisableFloorFunctionsMode.Integer;
    }



    private static void HiresRendererBeginRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        // Scale the matrix for the HUD
        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdloc0()))
        {
            cursor.EmitDelegate(GetCameraScale);
            cursor.EmitCall(typeof(Matrix).GetMethod(nameof(Matrix.CreateScale), [typeof(float)])!);
            cursor.EmitCall(typeof(Matrix).GetMethod("op_Multiply", [typeof(Matrix), typeof(Matrix)])!);
        }
    }

    private static void LookoutHudRenderHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        if (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchStloc(5)))
        {
            // Add another pixel to the border size, so it covers up the empty pixels on the right/bottom
            cursor.EmitLdcI4((int) Math.Ceiling(Scale));
            cursor.EmitAdd();
        }
    }



    private static VirtualRenderTarget GetLargeTargetOrNull(Texture texture)
    {
        if (HiresRenderer.Instance is not { } renderer || texture == null)
        {
            return null;
        }

        if (_largeExternalTextureMap.TryGetValue(texture, out var largeRenderTarget))
        {
            if (largeRenderTarget?.Target != null)
            {
                return largeRenderTarget;
            }

            else
            {
                // Large target was disposed, remove stale entry
                _largeExternalTextureMap.Remove(texture);
                _largeTextures.Remove(largeRenderTarget?.Target);
            }
        }

        else if (texture == GameplayBuffers.Gameplay.Target && _enableLargeGameplayBuffer)
        {
            return renderer.LargeGameplayBuffer;
        }

        else if (texture == GameplayBuffers.Level.Target && _enableLargeLevelBuffer)
        {
            return renderer.LargeLevelBuffer;
        }

        else if (texture == GameplayBuffers.TempA.Target && _enableLargeTempABuffer)
        {
            return renderer.LargeTempABuffer;
        }

        else if (texture == GameplayBuffers.TempB.Target && _enableLargeTempBBuffer)
        {
            return renderer.LargeTempBBuffer;
        }

        return null;
    }

    private static Texture GetPotentiallyLargeTexture(Texture texture)
    {
        if (HiresRenderer.Instance is not { } renderer || texture == null)
        {
            return texture;
        }

        if (_largeExternalTextureMap.TryGetValue(texture, out var largeRenderTarget))
        {
            if (largeRenderTarget?.Target != null)
            {
                return largeRenderTarget.Target;
            }

            else
            {
                // Large target was disposed, remove stale entry
                _largeExternalTextureMap.Remove(texture);
                _largeTextures.Remove(largeRenderTarget?.Target);
                return texture;
            }
        }

        else if (texture == GameplayBuffers.Gameplay.Target && _enableLargeGameplayBuffer)
        {
            return renderer.LargeGameplayBuffer;
        }

        else if (texture == GameplayBuffers.Level.Target && _enableLargeLevelBuffer)
        {
            return renderer.LargeLevelBuffer;
        }

        else if (texture == GameplayBuffers.TempA.Target && _enableLargeTempABuffer)
        {
            return renderer.LargeTempABuffer;
        }

        else if (texture == GameplayBuffers.TempB.Target && _enableLargeTempBBuffer)
        {
            return renderer.LargeTempBBuffer;
        }

        return texture;
    }

    private static string GetLargeTextureDebugName(Texture texture)
    {
        #if DEBUG
            if (HiresRenderer.Instance is not { } renderer || texture == null)
            {
                return "an unknown buffer";
            }

            if (texture == renderer.LargeGameplayBuffer.Target)
            {
                return "Large Gameplay";
            }

            else if (texture == renderer.LargeLevelBuffer.Target)
            {
                return "Large Level";
            }

            else if (texture == renderer.LargeTempABuffer.Target)
            {
                return "Large TempA";
            }

            else if (texture == renderer.LargeTempBBuffer.Target)
            {
                return "Large TempB";
            }

            foreach (var (smallTexture, largeTarget) in _largeExternalTextureMap)
            {
               if (largeTarget.Target == texture)
               {
                   return $"a hot-created buffer named {largeTarget.Name}";
               }
            }

            return "an unknown buffer";
        #else
            return "";
        #endif
    }

    

    private delegate void orig_SetRenderTargets(GraphicsDevice self, RenderTargetBinding[] renderTargets);

    private static void GraphicsDevice_SetRenderTargets(orig_SetRenderTargets orig, GraphicsDevice self, RenderTargetBinding[] renderTargetBindings)
    {
        if (HiresRenderer.Instance is not { } renderer)
        {
            orig(self, renderTargetBindings);
            return;
        }

        if (renderTargetBindings == null || renderTargetBindings.Length == 0)
        {
            _currentRenderTarget = null;
            orig(self, renderTargetBindings);
            return;
        }

        for (int i = 0; i < renderTargetBindings.Length; i++)
        {
            // If there's a large version of this, then we use that instead.
            if (renderTargetBindings[i].RenderTarget is Texture texture && GetLargeTargetOrNull(texture) is VirtualRenderTarget largeRenderTarget)
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


    
    // This hooks calls like Engine.Graphics.GraphicsDevice.Textures[1] = BloomBuffer,
    // which otherwise wouldn't use the hot-created buffer.
    private delegate void orig_TextureCollection_SetItem(TextureCollection self, int index, Texture texture);

    private static void TextureCollection_SetItem(orig_TextureCollection_SetItem orig, TextureCollection self, int index, Texture texture)
    {
        orig(self, index, GetPotentiallyLargeTexture(texture));
    }



    private delegate void orig_SpriteBatch_Begin(SpriteBatch self, SpriteSortMode sortMode, BlendState blendState,
        SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect, Matrix transformMatrix);

    private static void SpriteBatch_Begin(orig_SpriteBatch_Begin orig, SpriteBatch self, SpriteSortMode sortMode, BlendState blendState,
        SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect, Matrix transformMatrix)
    {
        _lastSpriteBatchBeginParams = (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);

        

        // If we're drawing to a large target, scale.
        if (_largeTextures.Contains(_currentRenderTarget))
        {
            transformMatrix = transformMatrix * Matrix.CreateScale(Scale);

            _currentlyScaling = true;

            // Disable the use of linear filtering and replace it with nearest neighbor.
            if (samplerState == SamplerState.LinearClamp)
            {
                samplerState = SamplerState.PointClamp;
            }

            else if (samplerState == SamplerState.LinearWrap)
            {
                samplerState = SamplerState.PointWrap;
            }
        }

		else if (_forceZoomDrawingToScreen && _currentRenderTarget == null)
		{
			transformMatrix = transformMatrix * ZoomMatrix;
		}

        orig(self, sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, transformMatrix);
    }


    // These are all five overloads that take a source rectangle and/or origin. If, and only
    // if, these are specified when drawing a large texture, it needs to be scaled. We can't
    // do this in the PushSprite hook, because these things are all in the range [0, 1] by then.
    // NOTE: we do not do this if we're drawing a small texture that's going to be replaced 
	// with a big one! Since source rectangles are on the scale [0, 1] and are computed as such
	// by all of the draw overloads by dividing by texture width, only when the actual draw call
	// is made with a large texture does the source rectangle need to be scaled. We do the exact
	// same thing with the origin parameters.
    private void HookSpriteBatchDraw()
    {
		// The bizarre numbering is just the order these overloads appear in the SpriteBatch class.

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", [typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color)])!, SpriteBatch_Draw2));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", [typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float)])!, SpriteBatch_Draw3));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", [typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float)])!, SpriteBatch_Draw4));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", [typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color)])!, SpriteBatch_Draw6));

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("Draw", [typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(SpriteEffects), typeof(float)])!, SpriteBatch_Draw7));
    }

    private static void SpriteBatch_Draw2(Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color> orig, SpriteBatch self, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color)
    {
        if (_largeTextures.Contains(texture) && _scaleSourceAndDestinationForLargeTextures)
		{
			if (
				sourceRectangle is Rectangle rect
				&& rect.Right <= texture.Width / Scale
				&& rect.Bottom <= texture.Height / Scale
			) {
				sourceRectangle = new Rectangle((int) (Scale * rect.X), (int) (Scale * rect.Y), (int) (Scale * rect.Width), (int) (Scale * rect.Height));
			}
        }

        orig(self, texture, position, sourceRectangle, color);
    }

    private static void SpriteBatch_Draw3(Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color, float, Vector2, float, SpriteEffects, float> orig, SpriteBatch self, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
    {
        if (_largeTextures.Contains(texture) && _scaleSourceAndDestinationForLargeTextures)
		{
			origin *= Scale;

			if (
				sourceRectangle is Rectangle rect
				&& rect.Right <= texture.Width / Scale
				&& rect.Bottom <= texture.Height / Scale
			) {
				sourceRectangle = new Rectangle((int) (Scale * rect.X), (int) (Scale * rect.Y), (int) (Scale * rect.Width), (int) (Scale * rect.Height));
			}
        }

        orig(self, texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void SpriteBatch_Draw4(Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color, float, Vector2, Vector2, SpriteEffects, float> orig, SpriteBatch self, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
    {
        if (_largeTextures.Contains(texture) && _scaleSourceAndDestinationForLargeTextures)
		{
			origin *= Scale;

			if (
				sourceRectangle is Rectangle rect
				&& rect.Right <= texture.Width / Scale
				&& rect.Bottom <= texture.Height / Scale
			) {
				sourceRectangle = new Rectangle((int) (Scale * rect.X), (int) (Scale * rect.Y), (int) (Scale * rect.Width), (int) (Scale * rect.Height));
			}
        }

        orig(self, texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void SpriteBatch_Draw6(Action<SpriteBatch, Texture2D, Rectangle, Rectangle?, Color> orig, SpriteBatch self, Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color)
    {
        if (_largeTextures.Contains(texture) && _scaleSourceAndDestinationForLargeTextures)
		{
			if (
				sourceRectangle is Rectangle rect
				&& rect.Right <= texture.Width / Scale
				&& rect.Bottom <= texture.Height / Scale
			) {
				sourceRectangle = new Rectangle((int) (Scale * rect.X), (int) (Scale * rect.Y), (int) (Scale * rect.Width), (int) (Scale * rect.Height));
			}
        }

        orig(self, texture, destinationRectangle, sourceRectangle, color);
    }

    private static void SpriteBatch_Draw7(Action<SpriteBatch, Texture2D, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float> orig, SpriteBatch self, Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
    {
        if (_largeTextures.Contains(texture) && _scaleSourceAndDestinationForLargeTextures)
		{
			origin *= Scale;

			if (
				sourceRectangle is Rectangle rect
				&& rect.Right <= texture.Width / Scale
				&& rect.Bottom <= texture.Height / Scale
			) {
				sourceRectangle = new Rectangle((int) (Scale * rect.X), (int) (Scale * rect.Y), (int) (Scale * rect.Width), (int) (Scale * rect.Height));
			}
        }

        orig(self, texture, destinationRectangle, sourceRectangle, color, rotation, origin, effects, layerDepth);
    }

    

    private static Vector2 GetCurrentDrawingOffset(Texture sourceTexture, float x, float y, float scale)
    {
        if (_offsetWhenDrawnTo.Contains(_currentRenderTarget))
        {
            Vector2 offset = GetCameraOffset();
            return new Vector2(x + offset.X * scale, y + offset.Y * scale);
        }

        if (_inverseOffsetWhenDrawnFrom.Contains(sourceTexture))
        {
            Vector2 offset = GetCameraOffset();
            return new Vector2(x - offset.X * scale, y - offset.Y * scale);
        }

        return new Vector2(x, y);
    }

	private delegate void orig_PushSprite(SpriteBatch self, Texture2D texture, float sourceX, float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH, Color color, float originX, float originY, float rotationSin, float rotationCos, float depth, byte effects);

	private static void PushSpriteHook(orig_PushSprite orig, SpriteBatch self, Texture2D texture, float sourceX, float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH, Color color, float originX, float originY, float rotationSin, float rotationCos, float depth, byte effects)
    {
        if (texture == null)
		{
			orig(self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
			return;
		}

        var largeTexture = GetPotentiallyLargeTexture(texture);

        bool changedTexture = largeTexture != texture;

        if (largeTexture is Texture2D largeTexture2D)
        {
            texture = largeTexture2D;
        }

        // If you're drawing the small version of this texture, no you're not!
        if (changedTexture)
        {
            destinationW *= Scale;
            destinationH *= Scale;
            destinationX *= Scale;
            destinationY *= Scale;
        }

        // If instead we're drawing a natively large texture to a large one or the screen with an offset
        // (e.g. SJ's color grade masks or TempA to its bloom masks), that offset needs to be scaled.
		// We do *not* scale the width and height because we aren't changing the size of the texture!
        else if (
            (_currentRenderTarget == null || _currentlyScaling)
            && _largeTextures.Contains(texture)
            && _scaleSourceAndDestinationForLargeTextures
        ) {
            destinationX *= Scale;
            destinationY *= Scale;
        }



        Vector2 offsetDestination = GetCurrentDrawingOffset(texture, destinationX, destinationY, _currentlyScaling ? 1 : Scale);



        bool sourceAndTargetAreSimilarSize = _currentRenderTarget is Texture2D targetTexture2D
            && Math.Abs(targetTexture2D.Width - texture.Width) < 8
			&& Math.Abs(targetTexture2D.Height - texture.Height) < 8;

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
                        Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, Matrix.CreateScale(1f / Scale) * matrix);
                        
                        offsetDestination = GetCurrentDrawingOffset(texture, destinationX, destinationY, Scale);

                        orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestination.X, offsetDestination.Y, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);

                        FillInverseOffsetEdgeGaps(orig, self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, offsetDestination, color, depth, effects);

                        Draw.SpriteBatch.End();
                        Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix);
                    }
                }

                return;
			}



            if (_currentRenderTarget is not Texture2D targetTexture2D2)
            {
                orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestination.X, offsetDestination.Y, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
                return;
			}            

            // If we get to this point, then we're drawing something large into something
            // small. Danger! We need to replace that small buffer with a larger one.
            var createdSuccessfully = HotCreateLargeBuffer(targetTexture2D2);

            if (createdSuccessfully)
            {
                #if DEBUG
                    Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", new StackTrace(true).ToString());
                    Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", $"Hot created a {targetTexture2D2.Width * Scale}x{targetTexture2D2.Height * Scale} buffer!");
                    Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", $"Reason: Drew {GetLargeTextureDebugName(texture)} into a small target called {_currentRenderTarget.Name}.");
                    Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", $"Total existing hot-created buffers: {_largeExternalTextureMap.Count}\n");
                #endif
            }

			// If we failed to create a large buffer, but we're drawing something into a buffer
			// that's very nearly the same size as the source, then we can just assume something
			// else resized the target to match the source (e.g. DBBHelper), and we can skip the
			// downscaling and just draw straight into the large buffer, marking the target as large
			// since the source was.
			// However! Things like CelesteNet can also do this when they're drawing the level to
			// a buffer the size of the screen. In that case, we absolutely do *not* want the target
			// to be large, since it isn't actually. So we use this somewhat sneaky heuristic to
			// check: if we're drawing with scale, like CelesteNet does, then we don't mark the
			// target as large.
			if (!createdSuccessfully && sourceAndTargetAreSimilarSize)
            {
				bool hasScaling = _lastSpriteBatchBeginParams is (_, _, _, _, _, _, var matrix)
					&& MatrixHasSignificantScaling(matrix);

				if (!hasScaling)
				{
					_largeTextures.Add(targetTexture2D2);
				}
			}
			

            if ((bool)_beginCalledField.GetValue(Draw.SpriteBatch))
            {
                // Since we're drawing something large into the new large buffer,
                // we ditch the scale exactly like above. However, at this point,
				// we're drawing something large into something large (either officially
				// or not), but *it's not being scaled*. Since we're applying an inverse
				// scale matrix to destination coordinates that weren't scaled in the first
				// place, we have to now multiply them by Scale to offset it.
                if (_lastSpriteBatchBeginParams is var (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix))
                {
                    Draw.SpriteBatch.End();
                    Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, Matrix.CreateScale(1f / Scale) * matrix);

                    // If the source texture is offsetable, the hot-created buffer should also
                    // be offsetable, and we need to apply the offset now since we didn't earlier
                    // (the target wasn't in _largeTextures yet when we computed the offset).
                    // Note: we check _offsetableLargeTextures even if _offsetDrawing is false,
                    // because mods like StyleMaskHelper run after DisableOffsetDrawing but still
                    // need offset applied for TempA draws.
                    offsetDestination = GetCurrentDrawingOffset(texture, destinationX, destinationY, Scale);

                    orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestination.X, offsetDestination.Y, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);

                    FillInverseOffsetEdgeGaps(orig, self, texture, sourceX, sourceY, sourceW, sourceH, destinationX, destinationY, destinationW, destinationH, offsetDestination, color, depth, effects);

                    Draw.SpriteBatch.End();
                    Draw.SpriteBatch.Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix);
                }
            }

			return;
		}



        orig(self, texture, sourceX, sourceY, sourceW, sourceH, offsetDestination.X, offsetDestination.Y, destinationW, destinationH, color, originX, originY, rotationSin, rotationCos, depth, effects);
    }

    /// <summary>
    /// When drawing from an _inverseOffsetWhenDrawnFrom texture, the content is shifted right/down,
    /// leaving a gap at the left/top edges. This fills those gaps by stretching the first whole
    /// game-pixel column/row from the source, analogous to how HideStretchedLevelEdges fills the
    /// right/bottom gaps.
    /// </summary>
    private static void FillInverseOffsetEdgeGaps(orig_PushSprite orig, SpriteBatch self, Texture2D texture, float sourceX, float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH, Vector2 offsetDestination, Color color, float depth, byte effects)
    {
        if (!_inverseOffsetWhenDrawnFrom.Contains(texture))
            return;

        float gapW = offsetDestination.X - destinationX;
        float gapH = offsetDestination.Y - destinationY;

        // Left edge: stretch the first Scale-wide source column to fill the left gap
        if (gapW > 0)
        {
            orig(self, texture,
                sourceX, sourceY, Scale / destinationW, sourceH,
                destinationX, offsetDestination.Y, gapW, destinationH,
                color, 0, 0, 0, 1, depth, effects);
        }

        // Top edge: stretch the first Scale-tall source row to fill the top gap
        if (gapH > 0)
        {
            orig(self, texture,
                sourceX, sourceY, sourceW, Scale / destinationH,
                offsetDestination.X, destinationY, destinationW, gapH,
                color, 0, 0, 0, 1, depth, effects);
        }

        // Corner: stretch the first Scale x Scale source pixel to fill the corner gap
        if (gapW > 0 && gapH > 0)
        {
            orig(self, texture,
                sourceX, sourceY, Scale / destinationW, Scale / destinationH,
                destinationX, destinationY, gapW, gapH,
                color, 0, 0, 0, 1, depth, effects);
        }
    }



	private static bool MatrixHasSignificantScaling(Matrix m)
	{
		float scaleX = new Vector2(m.M11, m.M12).Length();
		float scaleY = new Vector2(m.M21, m.M22).Length();
		return Math.Abs(scaleX - 1f) > 0.1f || Math.Abs(scaleY - 1f) > 0.1f;
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

        VirtualRenderTarget largeTarget = GameplayBuffers.Create(
			(int) (smallTexture.Width * Scale),
			(int) (smallTexture.Height * Scale)
		);

        // Now we switch to that buffer and proceed. To preserve whatever was in here before,
        // we copy it into the large buffer. Since we haven't registered this buffer anywhere yet,
        // none of this will be hooked.
        Engine.Instance.GraphicsDevice.SetRenderTarget(largeTarget);

        bool inSpriteBatch = (bool)_beginCalledField.GetValue(Draw.SpriteBatch);

        if (inSpriteBatch && _lastSpriteBatchBeginParams is var (sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect, matrix))
        {
            Draw.SpriteBatch.End();

            Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, Matrix.CreateScale(Scale));
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
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, Matrix.CreateScale(Scale));
            Draw.SpriteBatch.Draw(smallTexture, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            _largeExternalTextureMap[smallTexture] = largeTarget;
            _largeTextures.Add(largeTarget.Target);
        }

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
                Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", new StackTrace(true).ToString());
                Logger.Log(LogLevel.Verbose, "MotionSmoothingModule", $"Disposed a {texture2D.Width}x{texture2D.Height} hot-created buffer. Total left: {_largeExternalTextureMap.Count}");

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

        return Matrix.CreateScale(Scale);
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



    private static bool _inPixelatedDraw = false;

    private static bool TryDrawPixelated<T>(Matrix matrix, T[] vertices, int vertexCount) where T : struct, IVertexType
    {
        if (_inPixelatedDraw)
        {
            return false;
        }

        var renderTargets = Draw.SpriteBatch.GraphicsDevice.GetRenderTargets();

        if (renderTargets == null || renderTargets.Length == 0)
        {
            return false;
        }

        if (!_largeTextures.Contains(renderTargets[0].RenderTarget))
        {
            return false;
        }

        // Refuse to draw more than 100 vertices at a time for performance
        // (this prevents the background in the badeline fight from getting
        // this treatment, which is unfortunately too slow)
        if (vertices is VertexPositionColor[] vpcVertices && vertexCount < 100)
        {
            _inPixelatedDraw = true;
            PixelatedRenderer.DrawPixelated(matrix, vpcVertices, vertexCount);
            _inPixelatedDraw = false;

            return true;
        }

        return false;
    }

    private void DrawVerticesILHook<T>(ILContext il) where T : struct, IVertexType
    {
        var cursor = new ILCursor(il);
        cursor.Index = 0;

        // Try the pixelated path first
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldarg_1);
        cursor.Emit(OpCodes.Ldarg_2);
        cursor.EmitDelegate<Func<Matrix, T[], int, bool>>(TryDrawPixelated);

        var continueLabel = cursor.DefineLabel();
        cursor.Emit(OpCodes.Brfalse_S, continueLabel);
        cursor.Emit(OpCodes.Ret);
        cursor.MarkLabel(continueLabel);

        // Fallback: scale matrix multiplication (non-VPC types, or non-large textures)
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate(GetScaleMatrixForDrawVertices);
        cursor.EmitDelegate(MultiplyMatrices);
        cursor.Emit(OpCodes.Starg_S, (byte)0);
    }

    private void DrawIndexedVerticesILHook<T>(ILContext il) where T : struct, IVertexType
    {
        var cursor = new ILCursor(il);
        cursor.Index = 0;

        // Try the pixelated path first
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldarg_1);
        cursor.Emit(OpCodes.Ldarg_2);
        cursor.EmitDelegate<Func<Matrix, T[], int, bool>>(TryDrawPixelated);

        var continueLabel = cursor.DefineLabel();
        cursor.Emit(OpCodes.Brfalse_S, continueLabel);
        cursor.Emit(OpCodes.Ret);
        cursor.MarkLabel(continueLabel);

        // Fallback: scale matrix multiplication (non-VPC types, or non-large textures)
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate(GetScaleMatrixForDrawVertices);
        cursor.EmitDelegate(MultiplyMatrices);
        cursor.Emit(OpCodes.Starg_S, (byte)0);
    }


    
    private delegate Vector2 orig_Floor(Vector2 self);

    private static Vector2 FloorHook(orig_Floor orig, Vector2 self)
    {
        switch (_disableFloorFunctions)
        {
            case DisableFloorFunctionsMode.Continuous:
                return self;

            case DisableFloorFunctionsMode.Rational:
                return new Vector2((float) Math.Floor(self.X * Scale), (float) Math.Floor(self.Y * Scale)) / Scale;

            case DisableFloorFunctionsMode.Integer:
                return orig(self);
        }

        return orig(self);
    }

    private delegate Vector2 orig_Ceiling(Vector2 self);

    private static Vector2 CeilingHook(orig_Ceiling orig, Vector2 self)
    {
        switch (_disableFloorFunctions)
        {
            case DisableFloorFunctionsMode.Continuous:
                return self;

            case DisableFloorFunctionsMode.Rational:
                return new Vector2((float) Math.Ceiling(self.X * Scale), (float) Math.Ceiling(self.Y * Scale)) / Scale;

            case DisableFloorFunctionsMode.Integer:
                return orig(self);
        }

        return orig(self);
    }

    private delegate Vector2 orig_Round(Vector2 self);

    private static Vector2 RoundHook(orig_Round orig, Vector2 self)
    {
        switch (_disableFloorFunctions)
        {
            case DisableFloorFunctionsMode.Continuous:
                return self;

            case DisableFloorFunctionsMode.Rational:
                return new Vector2((float) Math.Round(self.X * Scale), (float) Math.Round(self.Y * Scale)) / Scale;

            case DisableFloorFunctionsMode.Integer:
                return orig(self);
        }

        return orig(self);
    }



	private void HookUnmaintainedMods()
	{
        Version spirialisHelperVersion = new Version(1, 0, 8);

		EverestModuleMetadata spirialisHelper = new() {
			Name = "SpirialisHelper",
			Version = spirialisHelperVersion
		};

		if (Everest.Loader.TryGetDependency(spirialisHelper, out var spirialisHelperModule))
		{
            // No exact version check here because there was no public repo to take out a PR on
            AddSpirialisHelperHook();
		}

        

        Version vivHelperVersion = new Version(1, 14, 7);

		EverestModuleMetadata vivHelper = new() {
			Name = "VivHelper",
			Version = vivHelperVersion
		};

		if (Everest.Loader.TryGetDependency(vivHelper, out var vivHelperModule))
        {
            // No exact version check here because there was no public repo to take out a PR on
            AddVivHelperHook();
        }

        

        Version glyphVersion = new Version(2, 3, 3);

        EverestModuleMetadata glyph = new() {
			Name = "Glyph",
            Version = glyphVersion
		};

		if (Everest.Loader.TryGetDependency(glyph, out var glyphModule))
        {
            // No exact version check here because there was no public repo to take out a PR on
            AddGlyphHook();
        }


        
        Version extendedCameraDynamicsVersion = new Version(1, 1, 2);

		EverestModuleMetadata extendedCameraDynamics = new() {
			Name = "ExtendedCameraDynamics",
			Version = extendedCameraDynamicsVersion
		};

		// Check for exact version so we don't hook anything if the mod updates
		if (Everest.Loader.TryGetDependency(extendedCameraDynamics, out var extendedCameraDynamicsModule))
		{
			if (extendedCameraDynamicsModule.Metadata.Version.Equals(extendedCameraDynamicsVersion))
			{
				AddExtendedCameraDynamicsHook();
			}
		}



        Version zoomOutHelperPrototypeVersion = new Version(0, 2, 0);

		EverestModuleMetadata zoomOutHelperPrototype = new() {
			Name = "ZoomOutHelperPrototype",
			Version = zoomOutHelperPrototypeVersion
		};

		// Check for exact version so we don't hook anything if the mod updates
		if (Everest.Loader.TryGetDependency(zoomOutHelperPrototype, out var zoomOutHelperPrototypeModule))
		{
			if (zoomOutHelperPrototypeModule.Metadata.Version.Equals(zoomOutHelperPrototypeVersion))
			{
				AddZoomOutHelperPrototypeHook();
			}
		}

        

        Version flagLinesAndSuchVersion = new Version(1, 6, 60);

        EverestModuleMetadata flagLinesAndSuch = new() {
			Name = "FlaglinesAndSuch",
			Version = flagLinesAndSuchVersion
		};

		// Check for exact version so we don't hook anything if the mod updates
		if (Everest.Loader.TryGetDependency(flagLinesAndSuch, out var flagLinesAndSuchModule))
		{
			if (flagLinesAndSuchModule.Metadata.Version.Equals(flagLinesAndSuchVersion))
			{
				AddFlaglinesAndSuchHook();
			}
		}

        

        Version strawberryJamVersion = new Version(1, 0, 12);

        EverestModuleMetadata strawberryJam = new() {
			Name = "StrawberryJam2021",
			Version = strawberryJamVersion
		};

		// Check for exact version so we don't hook anything if the mod updates
		if (Everest.Loader.TryGetDependency(strawberryJam, out var strawberryJamModule))
		{
			if (strawberryJamModule.Metadata.Version.Equals(strawberryJamVersion))
			{
				AddStrawberryJamHook();
			}
		}
	}


	private delegate void orig_DrawTimeStopEntities(object self);

	// noinlining necessary to avoid crashes when the jit attempts inline this method while jitting methods that use this function
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddSpirialisHelperHook()
	{
		Type t_TimeController = Type.GetType("Celeste.Mod.Spirialis.TimeController, Spirialis");
		MethodInfo m_DrawTimeStopEntities = t_TimeController?.GetMethod(
			"DrawTimestopEntities",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_DrawTimeStopEntities != null)
		{
			AddHook(new Hook(m_DrawTimeStopEntities, DrawTimeStopEntitiesHook));
		}
	}

	private static void DrawTimeStopEntitiesHook(orig_DrawTimeStopEntities orig, object self)
	{
		_forceZoomDrawingToScreen = true;
		orig(self);
		_forceZoomDrawingToScreen = false;
	}



	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddVivHelperHook()
	{
		Type t_HoldableBarrierRenderer = Type.GetType("VivHelper.Entities.HoldableBarrierRenderer, VivHelper");
		MethodInfo m_OnRenderBloom = t_HoldableBarrierRenderer?.GetMethod(
			"OnRenderBloom",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_OnRenderBloom != null)
		{
			AddHook(new ILHook(m_OnRenderBloom, SeekerBarrierRendererRenderHook));
		}
	}



    [MethodImpl(MethodImplOptions.NoInlining)]
	private void AddGlyphHook()
	{
		Type t_InstantTeleporterRenderer = Type.GetType("Celeste.Mod.AcidHelper.Entities.InstantTeleporterRenderer, AcidHelper");
		MethodInfo m_OnRenderBloom = t_InstantTeleporterRenderer?.GetMethod(
			"OnRenderBloom",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_OnRenderBloom != null)
		{
			AddHook(new ILHook(m_OnRenderBloom, SeekerBarrierRendererRenderHook));
		}
	}



	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddExtendedCameraDynamicsHook()
	{
		Type t_CameraZoomHooks = Type.GetType("Celeste.Mod.ExCameraDynamics.Code.Hooks.CameraZoomHooks, ExCameraDynamics");
		
		MethodInfo m_ResizeVanillaBuffers = t_CameraZoomHooks?.GetMethod(
			"ResizeVanillaBuffers",
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_ResizeVanillaBuffers != null)
		{
			AddHook(new Hook(m_ResizeVanillaBuffers, ResizeVanillaBuffersHook));
		}

		MethodInfo m_ResizeBufferToZoom = t_CameraZoomHooks?.GetMethod(
			"ResizeBufferToZoom",
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_ResizeBufferToZoom != null)
		{
			AddHook(new Hook(m_ResizeBufferToZoom, ResizeBufferToZoomHook));
		}
	}

	private delegate void orig_ResizeVanillaBuffers(float zoomTarget);
	private static void ResizeVanillaBuffersHook(orig_ResizeVanillaBuffers orig, float zoomTarget)
	{
		orig(zoomTarget);
		InitializeLargeTextures();
	}

	private delegate void orig_ResizeBufferToZoom(VirtualRenderTarget target);
	private static void ResizeBufferToZoomHook(orig_ResizeBufferToZoom orig, VirtualRenderTarget target)
	{
		target = MotionSmoothingModule.GetResizableBuffer(target);
		orig(target);
	}



    [MethodImpl(MethodImplOptions.NoInlining)]
	private void AddZoomOutHelperPrototypeHook()
	{
		Type t_FunctionalZoomOutModule = Type.GetType("Celeste.Mod.FunctionalZoomOut.FunctionalZoomOutModule, FunctionalZoomOut");
		
		MethodInfo m_EnsureVanillaBuffers = t_FunctionalZoomOutModule?.GetMethod(
			"EnsureVanillaBuffers",
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_EnsureVanillaBuffers != null)
		{
			AddHook(new Hook(m_EnsureVanillaBuffers, EnsureVanillaBuffersHook));
		}

		MethodInfo m_EnsureBufferDimensions = t_FunctionalZoomOutModule?.GetMethod(
			"EnsureBufferDimensions",
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_EnsureBufferDimensions != null)
		{
			AddHook(new Hook(m_EnsureBufferDimensions, EnsureBufferDimensionsHook));
		}
	}

    private static bool _zoomOutHelperPrototypeNeedBufferInitialization = false;

    private delegate void orig_EnsureVanillaBuffers();
	private static void EnsureVanillaBuffersHook(orig_EnsureVanillaBuffers orig)
	{
        _zoomOutHelperPrototypeNeedBufferInitialization = false;

		orig();

        if (_zoomOutHelperPrototypeNeedBufferInitialization)
        {
            InitializeLargeTextures();
        }
	}

	private delegate void orig_EnsureBufferDimensionsHook(VirtualRenderTarget target, int padding);
	private static void EnsureBufferDimensionsHook(orig_EnsureBufferDimensionsHook orig, VirtualRenderTarget target, int padding)
	{
		target = MotionSmoothingModule.GetResizableBuffer(target);
        
        
        var oldWidth = target?.Target?.Width;
        var oldHeight = target?.Target?.Height;
        
        orig(target, padding);

        if (target?.Target?.Width != oldWidth || target?.Target?.Height != oldHeight)
        {
            _zoomOutHelperPrototypeNeedBufferInitialization = true;
        }
    }



    [MethodImpl(MethodImplOptions.NoInlining)]
	private void AddFlaglinesAndSuchHook()
	{
		Type t_CustomGodrays = Type.GetType("FlaglinesAndSuch.CustomGodrays, FlaglinesAndSuch");
		MethodInfo m_Update = t_CustomGodrays?.GetMethod(
			"Update",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_Update != null)
		{
			AddHook(new ILHook(m_Update, GodraysUpdateHook));
		}
	}



    [MethodImpl(MethodImplOptions.NoInlining)]
	private void AddStrawberryJamHook()
	{
		Type t_HexagonalGodray = Type.GetType("Celeste.Mod.StrawberryJam2021.Effects.HexagonalGodray, StrawberryJam2021");
		MethodInfo m_Update = t_HexagonalGodray?.GetMethod(
			"Update",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (m_Update != null)
		{
			AddHook(new ILHook(m_Update, GodraysUpdateHook));
		}
	}
}