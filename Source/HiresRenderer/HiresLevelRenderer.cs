using System;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.MotionSmoothing.HiresRenderer;

public class HiresLevelRenderer : ToggleableFeature<HiresLevelRenderer>
{
    public static VirtualRenderTarget HiresGameplay;
    public static VirtualRenderTarget HiresLevel;
    public static VirtualRenderTarget HiresDisplace;

    private const int HiresMultiplier = 6;
    private const int HiresWidth = 320 * HiresMultiplier;
    private const int HiresHeight = 180 * HiresMultiplier;

    public static Matrix ToHiresCamOffset { get; private set; }
    public static Matrix ToHires { get; private set; }
    public static Matrix ToLowresCamOffset { get; private set; }
    public static Matrix ToLowres { get; private set; }
    public static Matrix BackdropMatrix { get; private set; }

    private HiresFixes Fixes { get; } = new();

    public override void Enable()
    {
        base.Enable();
        Fixes.Enable();

        if (Engine.Scene is Level)
            CreateBuffers();
    }

    public override void Disable()
    {
        base.Disable();
        Fixes.Disable();
    }

    private static void CreateBuffers()
    {
        HiresGameplay = GameplayBuffers.Create(HiresWidth, HiresHeight);
        HiresLevel = GameplayBuffers.Create(HiresWidth, HiresHeight);
        HiresDisplace = GameplayBuffers.Create(HiresWidth, HiresHeight);
        GameplayBuffers.Create(HiresWidth, HiresHeight);
        GameplayBuffers.Create(HiresWidth, HiresHeight);
    }

    protected override void Hook()
    {
        base.Hook();
        On.Celeste.Level.Render += LevelRenderHook;
        On.Celeste.GameplayRenderer.Render += GameplayRendererRender;
        IL.Celeste.BloomRenderer.Apply += BloomRendererApplyHook;
        IL.Celeste.LightingRenderer.Render += LightingRendererRenderHook;
        IL.Celeste.Glitch.Apply += GlitchApplyHook;

        On.Celeste.GameplayBuffers.Create += GameplayBuffersCreateHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        On.Celeste.Level.Render -= LevelRenderHook;
        On.Celeste.GameplayRenderer.Render -= GameplayRendererRender;
        IL.Celeste.BloomRenderer.Apply -= BloomRendererApplyHook;
        IL.Celeste.LightingRenderer.Render -= LightingRendererRenderHook;
        IL.Celeste.Glitch.Apply -= GlitchApplyHook;

        On.Celeste.GameplayBuffers.Create -= GameplayBuffersCreateHook;
    }

    private static void GameplayBuffersCreateHook(On.Celeste.GameplayBuffers.orig_Create orig)
    {
        orig();
        CreateBuffers();
    }


    /// <summary>
    /// This could be an IL hook instead, but it would be a LOT of IL changes, that would probably end up breaking
    /// other mods that hook the render method anyway. And for now I just wanna see if this works.
    ///
    /// Where possible, things are still rendered to low res buffers, to try and keep as much pixelated as possible.
    /// 
    /// The camera offset is applied to the individual pieces, rather than at the end, because we do NOT want to
    /// offset the background or foreground. The background and foreground should be using the subpixel camera position
    /// themselves. This way we only have to fix Floor()s in the backdrops, and not every entity that might be
    /// flooring the camera position for rendering.
    /// </summary>
    private static void LevelRenderHook(On.Celeste.Level.orig_Render orig, Level self)
    {
        if (!Instance.Enabled)
        {
            orig(self);
            return;
        }

        // Update the matrices that are used for rendering
        ToHiresCamOffset = Matrix.CreateScale(6f) * UnlockedCameraSmoother.GetScreenCameraMatrix();
        ToHires = Matrix.CreateScale(6f);
        // ToLowresCamOffset = Matrix.Invert(ToHiresCamOffset) * Matrix.CreateTranslation(-1, -1, 0);
        ToLowresCamOffset = Matrix.Invert(ToHiresCamOffset);
        ToLowres = Matrix.Invert(ToHires);
        BackdropMatrix = ToHires;

        // Clear both gameplay buffers
        Engine.Instance.GraphicsDevice.SetRenderTarget(HiresGameplay);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

        // Render the game to the hires canvas with nice offsets
        self.GameplayRenderer.Render(self);

        // Render lighting
        // Our custom Gameplay Renderer leaves the render target on GameplayBuffers.Gameplay, so swap back to hires
        Engine.Instance.GraphicsDevice.SetRenderTarget(HiresGameplay);
        self.Lighting.Render(self);

        // Generate a hires displacement map for use later down the line
        Engine.Instance.GraphicsDevice.SetRenderTarget(HiresDisplace);
        Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
        DrawToHires(GameplayBuffers.Displacement);

        // Draw the level with effects
        Engine.Instance.GraphicsDevice.SetRenderTarget(HiresLevel);
        Engine.Instance.GraphicsDevice.Clear(self.BackgroundColor);

        // At this point, force the camera position to be the floating point smoothed pos for the upcoming backdrops
        SmoothCameraPosition(self);

        // Draw background to level
        var oldBackgroundMatrix = self.Background.Matrix;
        self.Background.Matrix = BackdropMatrix;
        self.Background.Render(self);
        self.Background.Matrix = oldBackgroundMatrix;

        // Draw gameplay with distortion
        Distort.Render(HiresGameplay, HiresDisplace, self.Displacement.HasDisplacement(self));

        // We can render the bloom at low resolution, so generate a low res level
        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);
        Engine.Instance.GraphicsDevice.Clear(self.BackgroundColor);
        DrawToLores(HiresLevel);
        self.Bloom.Apply(HiresLevel, self); // Our hook sets the RT back HiresLevel

        // Draw the foreground
        var oldForegroundMatrix = self.Foreground.Matrix;
        self.Foreground.Matrix = BackdropMatrix;
        self.Foreground.Render(self);
        self.Foreground.Matrix = oldForegroundMatrix;

        // Glitch
        Glitch.Apply(GameplayBuffers.Level, self.glitchTimer * 2f, self.glitchSeed, 6.2831855f);

        // Dash assist
        if (Engine.DashAssistFreeze)
        {
            var entity = self.Tracker.GetEntity<PlayerDashAssist>();
            if (entity != null)
            {
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.Default, RasterizerState.CullNone, null, self.Camera.Matrix * ToHiresCamOffset);
                entity.Render();
                Draw.SpriteBatch.End();
            }
        }

        // Flash
        if (self.flash > 0.0)
        {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.Default, RasterizerState.CullNone, null);
            Draw.Rect(-1f, -1f, HiresWidth + 2, HiresHeight + 2, self.flashColor * self.flash);
            Draw.SpriteBatch.End();
            if (self.flashDrawPlayer)
            {
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.Default, RasterizerState.CullNone, null,
                    self.Camera.Matrix * ToHiresCamOffset);
                var entity = self.Tracker.GetEntity<Player>();
                if (entity != null && entity.Visible)
                    entity.Render();
                Draw.SpriteBatch.End();
            }
        }

        // Finally, draw everything to the screen
        Engine.Instance.GraphicsDevice.SetRenderTarget(null);
        Engine.Instance.GraphicsDevice.Clear(Color.Black);
        Engine.Instance.GraphicsDevice.Viewport = Engine.Viewport;

        // Calculate offsets/scale
        var transformationMatrix = Matrix.CreateScale(6f) * Engine.ScreenMatrix;
        var lowRes = new Vector2(HiresWidth, HiresHeight);
        // var lowRes = new Vector2(320, 180);
        var zoom = lowRes / self.ZoomTarget;
        var origin = self.ZoomTarget != 1.0
            ? ((self.ZoomFocusPoint * HiresMultiplier) - zoom / 2f) / (lowRes - zoom) * lowRes
            : Vector2.Zero;
        var orDefault1 = GFX.ColorGrades.GetOrDefault(self.lastColorGrade, GFX.ColorGrades["none"]);
        var orDefault2 = GFX.ColorGrades.GetOrDefault(self.Session.ColorGrade, GFX.ColorGrades["none"]);
        if (self.colorGradeEase > 0.0 && orDefault1 != orDefault2)
            ColorGrade.Set(orDefault1, orDefault2, self.colorGradeEase);
        else
            ColorGrade.Set(orDefault2);
        var scale = self.Zoom * (float)((HiresWidth - self.ScreenPadding * 2.0) / HiresWidth) *
                    UnlockedCameraSmoother.GetCameraScale();
        var offset = new Vector2(self.ScreenPadding, self.ScreenPadding * (9f / 16f)) * HiresMultiplier;
        if (SaveData.Instance.Assists.MirrorMode)
        {
            offset.X = -offset.X;
            origin.X = HiresWidth / 2f - (origin.X - HiresWidth / 2f);
        }

        // Draw the hires level to the screen
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.Default, RasterizerState.CullNone, ColorGrade.Effect, Engine.ScreenMatrix);
        Draw.SpriteBatch.Draw(HiresLevel, origin + offset, HiresLevel.Bounds, Color.White, 0.0f,
            origin, scale,
            SaveData.Instance.Assists.MirrorMode ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0.0f);
        RenderHiresExtend(origin, offset, scale);
        Draw.SpriteBatch.End();

        // The rest isn't changed
        if (self.Pathfinder != null && self.Pathfinder.DebugRenderEnabled)
        {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.Default, RasterizerState.CullNone, null,
                self.Camera.Matrix * transformationMatrix);
            self.Pathfinder.Render();
            Draw.SpriteBatch.End();
        }

        self.SubHudRenderer.Render(self);
        if ((!self.Paused || !self.PauseMainMenuOpen) && self.wasPausedTimer >= 1.0 || !Input.MenuJournal.Check ||
            !self.AllowHudHide)
            self.HudRenderer.Render(self);
        if (self.Wipe != null)
            self.Wipe.Render(self);
        if (self.HiresSnow != null)
            self.HiresSnow.Render(self);

        UnlockedCameraSmoother.RenderBorder();
    }

    private static void RenderHiresExtend(Vector2 origin, Vector2 offset, float scale)
    {
        const int textureWidth = HiresWidth;
        const int textureHeight = HiresHeight;
        const int size = HiresMultiplier;

        if (MotionSmoothingModule.Settings.UnlockCameraMode != UnlockCameraMode.Extend) return;
        if (((Level)Engine.Scene).ScreenPadding > 0) return;

        var effect = SaveData.Instance.Assists.MirrorMode ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        var texture = HiresLevel;
        var camOffset = UnlockedCameraSmoother.GetCameraOffset() * HiresMultiplier
                        + new Vector2(UnlockedCameraSmoother.BorderOffset, UnlockedCameraSmoother.BorderOffset);

        var camOffsetX = (int)Math.Round(camOffset.X);
        var camOffsetY = (int)Math.Round(camOffset.Y);
        offset += new Vector2(camOffsetX, camOffsetY);

        var posX = (int)Math.Round(origin.X + offset.X);
        var posY = (int)Math.Round(origin.Y + offset.Y);

        Draw.SpriteBatch.Draw(texture,
            new Rectangle(posX, (int)Math.Round(posY + textureHeight * scale - size / 2f),
                textureWidth, (int)(size * scale)),
            new Rectangle(camOffsetX, camOffsetY + textureHeight - size / 2, textureWidth, 1), Color.White,
            0.0f, origin, effect, 0.0f);

        Draw.SpriteBatch.Draw(texture,
            new Rectangle((int)Math.Round(posX + textureWidth * scale - size / 2f), posY,
                (int)(size * scale), textureHeight),
            new Rectangle(camOffsetX + textureWidth - size / 2, camOffsetY, 1, textureHeight), Color.White,
            0.0f, origin, effect, 0.0f);

        Draw.SpriteBatch.Draw(texture,
            new Rectangle(posX, (int)Math.Round(posY - size / 2f),
                textureWidth, (int)(size * scale)),
            new Rectangle(camOffsetX, camOffsetY, textureWidth, 1), Color.White,
            0.0f, origin, effect, 0.0f);

        Draw.SpriteBatch.Draw(texture,
            new Rectangle((int)Math.Round(posX - size / 2f), posY,
                (int)(size * scale), textureHeight),
            new Rectangle(camOffsetX, camOffsetY, 1, textureHeight), Color.White,
            0.0f, origin, effect, 0.0f);
    }

    private static void DrawToHires(RenderTarget2D target)
    {
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.Default, RasterizerState.CullNone, null, ToHiresCamOffset);
        Draw.SpriteBatch.Draw(target, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
    }

    private static void DrawToLores(RenderTarget2D target)
    {
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.Default, RasterizerState.CullNone, null, ToLowresCamOffset);
        Draw.SpriteBatch.Draw(target, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
    }

    /// <summary>
    /// Same as before, could be an IL hook, but would be a pain.
    /// </summary>
    private static void GameplayRendererRender(On.Celeste.GameplayRenderer.orig_Render orig, GameplayRenderer self,
        Scene scene)
    {
        if (!Instance.Enabled)
        {
            orig(self, scene);
            return;
        }

        void RenderToHires(Vector2 offset)
        {
            GameplayRenderer.End();
            Engine.Instance.GraphicsDevice.SetRenderTarget(HiresGameplay);

            var matrix = Matrix.CreateTranslation(offset.X, offset.Y, 0) * ToHiresCamOffset;
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.Default, RasterizerState.CullNone, null, matrix);
            Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Gameplay);
            Engine.Instance.GraphicsDevice.Clear(Color.Transparent);

            GameplayRenderer.Begin();
        }

        GameplayRenderer.Begin();

        foreach (var entity in scene.Entities)
        {
            if (!entity.Visible || entity.TagCheck((int)Tags.HUD | (int)TagsExt.SubHUD)) continue;

            var state = MotionSmoothingHandler.Instance.GetState(entity) as IPositionSmoothingState;
            if (state == null)
            {
                entity.Render();
                continue;
            }

            var offset = state.SmoothedRealPosition - state.SmoothedRealPosition.Round();
            if (offset == Vector2.Zero)
            {
                entity.Render();
                continue;
            }

            // Make sure the player is locked to a pixel when not moving as to not give away the subpixel
            if (entity is Player player)
            {
                if (state.RealPositionHistory[0] == state.RealPositionHistory[1])
                {
                    player.Position = state.SmoothedRealPosition.Round();
                    entity.Render();
                    continue;
                }
            }

            // Render current buffer with no offset
            RenderToHires(Vector2.Zero);

            // Render entity to low res buffer
            entity.Render();

            // Render low res buffer to hires buffer with offset
            RenderToHires(offset);
        }

        RenderToHires(Vector2.Zero);

        GameplayRenderer.End();
    }

    private static void LightingRendererRenderHook(ILContext il)
    {
        var c = new ILCursor(il);

        c.GotoNext(MoveType.After, i => i.MatchCall<Matrix>("get_Identity"));
        c.EmitCall(typeof(HiresLevelRenderer).GetProperty(nameof(ToHiresCamOffset))!.GetGetMethod()!);
        c.EmitCall(typeof(Matrix).GetMethod("op_Multiply", new[] { typeof(Matrix), typeof(Matrix) })!);
    }

    private static void BloomRendererApplyHook(ILContext il)
    {
        var c = new ILCursor(il);

        // Force Apply to render to the hires level
        if (c.TryGotoNext(MoveType.After, i => i.MatchCallvirt<Game>("get_GraphicsDevice"),
                i => i.MatchLdarg1()))
        {
            c.EmitPop();
            c.Emit(OpCodes.Ldsfld, typeof(HiresLevelRenderer).GetField(nameof(HiresLevel))!);
        }

        // When it draws the bloom, scale it up
        c.GotoNext(MoveType.After,
            i => i.MatchLdsfld<BloomRenderer>(nameof(BloomRenderer.AdditiveMaskToScreen)),
            i => i.MatchCallvirt<SpriteBatch>(nameof(SpriteBatch.Begin)));
        {
            static void FixSpriteBatchBegin()
            {
                Draw.SpriteBatch.End();
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BloomRenderer.AdditiveMaskToScreen,
                    SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null,
                    ToHiresCamOffset);
            }

            c.EmitDelegate(FixSpriteBatchBegin);
        }
    }

    private static void GlitchApplyHook(ILContext il)
    {
        var c = new ILCursor(il);

        // Force Apply to render to the hires level
        c.GotoNext(MoveType.After, i => i.MatchCallvirt<Game>("get_GraphicsDevice"),
            i => i.MatchLdarg0());
        {
            c.EmitPop();
            c.Emit(OpCodes.Ldsfld, typeof(HiresLevelRenderer).GetField(nameof(HiresLevel))!);
        }


        // When it draws the glitch, scale it up
        c.GotoNext(MoveType.After,
            i => i.MatchCallvirt<SpriteBatch>(nameof(SpriteBatch.Begin)));
        {
            static void FixSpriteBatchBegin()
            {
                Draw.SpriteBatch.End();
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.Default, RasterizerState.CullNone, GFX.FxGlitch, ToHiresCamOffset);
            }

            c.EmitDelegate(FixSpriteBatchBegin);
        }
    }

    private static void SmoothCameraPosition(Level level)
    {
        // Camera's UpdateMatrices method ALSO floors the position, so manually create the matrix here, and set
        // the private fields instead of using the public properties.
        var cameraState = (MotionSmoothingHandler.Instance.GetState(level.Camera) as IPositionSmoothingState)!;
        var camera = level.Camera;
        camera.position = cameraState.SmoothedRealPosition;
        camera.matrix = Matrix.Identity *
                        Matrix.CreateTranslation(new Vector3(-new Vector2(camera.position.X, camera.position.Y),
                            0.0f)) *
                        Matrix.CreateRotationZ(camera.angle) * Matrix.CreateScale(new Vector3(camera.zoom, 1f)) *
                        Matrix.CreateTranslation(new Vector3(
                            new Vector2((int)Math.Floor(camera.origin.X), (int)Math.Floor(camera.origin.Y)), 0.0f));
        camera.inverse = Matrix.Invert(camera.matrix);
        camera.changed = false;
    }
}