using System;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.States;

public class EntitySmoothingState : PositionSmoothingState<Entity>
{
    protected override Vector2 GetRealPosition(Entity obj) => obj.Position;
    protected override void SetPosition(Entity obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Entity obj) => obj.Visible;
}

public class ZipMoverPercentSmoothingState : FloatSmoothingState<ZipMover>
{
    private Vector2 _originalPosition;

    protected override float GetValue(ZipMover obj) => obj.percent;

    protected override void SetValue(ZipMover obj, float value)
    {
        obj.percent = value;
        obj.Position = GetPositionAtPercent(obj, value);
    }

    protected override void SetSmoothed(ZipMover obj)
    {
        _originalPosition = obj.Position;
        base.SetSmoothed(obj);
    }

    protected override void SetOriginal(ZipMover obj)
    {
        base.SetOriginal(obj);
        obj.Position = _originalPosition;
    }

    public Vector2 GetPositionAtPercent(ZipMover obj, float percent)
    {
        return Vector2.Lerp(obj.start, obj.target, percent).Round();
    }
}

public class EyeballsSmoothingState : PositionSmoothingState<DustGraphic.Eyeballs>
{
    protected override Vector2 GetRealPosition(DustGraphic.Eyeballs obj) => obj.Dust.RenderPosition;

    protected override void SetPosition(DustGraphic.Eyeballs obj, Vector2 position) =>
        throw new System.NotSupportedException();

    protected override bool GetVisible(DustGraphic.Eyeballs obj) => true;
}

public class ComponentSmoothingState : PositionSmoothingState<GraphicsComponent>
{
    protected override Vector2 GetRealPosition(GraphicsComponent obj)
    {
        if (obj.Entity is Booster booster)
        {
            // Boosters use the player's draw position, not real position, so make sure we smooth with the player's 
            // real position instead
            var player = MotionSmoothingHandler.Instance?.Player;

            // This addresses a crash when dying with a golden around bubbles.
            if (player is not Player)
            {
                return obj.Position;
            }

            if ((player.StateMachine.State == 2 || player.StateMachine.state == 5) && booster.BoostingPlayer)
            {
                var playerRealCenterX = player.ExactPosition.X + (player.Collider?.Center.X ?? 0);
                var playerRealCenterY = player.ExactPosition.Y + (player.Collider?.Center.Y ?? 0);
                return new Vector2(playerRealCenterX, playerRealCenterY) + Booster.playerOffset - booster.Position;
            }
        }

        return obj.Position;
    }

    protected override Vector2 GetDrawPosition(GraphicsComponent obj)
    {
        // Boosters floor the position
        if (obj.Entity is Booster)
            return obj.Position.Floor();

        return obj.Position;
    }

    protected override void SetPosition(GraphicsComponent obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(GraphicsComponent obj) => obj.Visible;
}

public class ScreenWipeSmoothingState : PercentSmoothingState<ScreenWipe>
{
    protected override float GetValue(ScreenWipe obj) => obj.Percent;
    protected override void SetValue(ScreenWipe obj, float value) => obj.Percent = value;
}

public class FinalBossBeamSmoothingState : AngleSmoothingState<FinalBossBeam>
{
    protected override float GetValue(FinalBossBeam obj) => obj.angle;
    protected override void SetValue(FinalBossBeam obj, float value) => obj.angle = value;
}

public class CameraSmoothingState : PositionSmoothingState<Camera>
{
    private Vector2 _lastSmoothedBeforePause;
    private bool _hasLastSmoothedBeforePause;
    private UnlockCameraStrategy _lastSmoothedStrategy;

    protected override bool CancelSmoothing => CelesteTasInterop.CenterCamera;
    protected override Vector2 GetRealPosition(Camera obj) => obj.Position;
    protected override void SetPosition(Camera obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Camera obj) => true;

    protected override void SetSmoothed(Camera obj)
    {
        if (CancelSmoothing || !_initialized) return;
        PreSmoothedPosition = obj.Position;
        obj.Position = SmoothedRealPosition.Floor();
    }

    // Override the pause-snap so that the camera does not jump to the rounded
    // OriginalDrawPosition: that snap can push floor(SmoothedRealPosition) over
    // an integer boundary and visibly shift the entire scene by one pixel at the
    // moment of pause. Holding the last pre-pause smoothed position keeps the
    // rendered camera pinned where it was — for every camera-smoothing mode.
    //
    // The cache is invalidated if the camera-smoothing strategy changes, because
    // Fancy mode leaves camera.position fractional while Fast/Off floor it, so a
    // cached value from one mode is not safe to reuse in another (a stale value
    // routed through Fast's per-pixel offset path would shift the level visibly).
    protected override void Smooth(Camera obj, double elapsedSeconds, SmoothingMode mode)
    {
        if (OverrideSmoothingMode.HasValue)
            mode = OverrideSmoothingMode.Value;

        // Carveout: the camera still needs fractional offsets even when global
        // smoothing is Off, otherwise the entire scene jitters on integer steps.
        if (mode == SmoothingMode.Off)
            mode = SmoothingMode.Extrapolate;

        var currentStrategy = MotionSmoothingModule.Settings.UnlockCameraStrategy;
        if (_hasLastSmoothedBeforePause && _lastSmoothedStrategy != currentStrategy)
            _hasLastSmoothedBeforePause = false;

        if (MotionSmoothingHandler.Instance.WasPaused || Engine.Scene.Paused)
        {
            SmoothedRealPosition = _hasLastSmoothedBeforePause
                ? _lastSmoothedBeforePause
                : OriginalDrawPosition;
        }
        else
        {
            SmoothedRealPosition = PositionSmoother.Smooth(this, obj, elapsedSeconds, mode);

            // Stabilize the camera onto integer pixels while Madeline is offscreen (see below).
            // Done before caching so a pause taken while offscreen holds the snapped position.
            ApplyOffscreenSnap(obj);

            _lastSmoothedBeforePause = SmoothedRealPosition;
            _lastSmoothedStrategy = currentStrategy;
            _hasLastSmoothedBeforePause = true;
        }
    }

    // Disables camera smoothing when Madeline is completely offscreen to prevent
	// setups that use very short taps from being possible with Motion Smoothing
	// that otherwise wouldn't be

    private void ApplyOffscreenSnap(Camera camera)
    {
        var player = MotionSmoothingHandler.Instance?.Player;
        if (player == null)
            return;

        if (IsPlayerOffscreen(camera, RealPositionHistory[0], player))
            SmoothedRealPosition = Calc.Round(SmoothedRealPosition);
    }

    // Madeline counts as offscreen only when her drawn sprite's bounding box lies entirely outside
    // the camera view. The box is reconstructed the exact way MTexture.Draw lays the sprite down:
    // the trimmed ClipRect, placed at DrawOffset inside the logical frame, anchored at Origin and
    // multiplied by the (possibly flipped) Scale. Using the logical Width/Height instead would leave
    // the frame's transparent padding in the box, keeping her "onscreen" several pixels too long
    // (most visibly above her head, where the player frame has the most empty space).
    private static bool IsPlayerOffscreen(Camera camera, Vector2 cameraPosition, Player player)
    {
        var sprite = player.Sprite;
        if (sprite?.Texture is not { } texture)
            return false;

        var renderPos = player.Position + sprite.Position;
        var scale = sprite.Scale;
        var origin = sprite.Origin;

        // Drawn pixels in sprite-local space (before scale): the ClipRect, shifted by DrawOffset and
        // anchored at Origin — matching SpriteBatch's `origin - DrawOffset` inside MTexture.Draw.
        float localLeft = texture.DrawOffset.X - origin.X;
        float localTop = texture.DrawOffset.Y - origin.Y;
        float w = texture.ClipRect.Width;
        float h = texture.ClipRect.Height;

        // min/max over the two opposite corners handles a negative (flipped) scale.
        float cornerAx = localLeft * scale.X;
        float cornerBx = (localLeft + w) * scale.X;
        float cornerAy = localTop * scale.Y;
        float cornerBy = (localTop + h) * scale.Y;

        float left = renderPos.X + Math.Min(cornerAx, cornerBx);
        float right = renderPos.X + Math.Max(cornerAx, cornerBx);
        float top = renderPos.Y + Math.Min(cornerAy, cornerBy);
        float bottom = renderPos.Y + Math.Max(cornerAy, cornerBy);

        float viewLeft = cameraPosition.X;
        float viewTop = cameraPosition.Y;
        float viewRight = cameraPosition.X + camera.Viewport.Width;
        float viewBottom = cameraPosition.Y + camera.Viewport.Height;

        return right < viewLeft || left > viewRight || bottom < viewTop || top > viewBottom;;
    }
}

public class PlatformSmoothingState : PositionSmoothingState<Platform>
{
    protected override Vector2 GetRealPosition(Platform obj) => obj.ExactPosition;
    protected override Vector2 GetDrawPosition(Platform obj) => obj.Position;
    protected override void SetPosition(Platform obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Platform obj) => obj.Visible;
}

public class ActorSmoothingState : PositionSmoothingState<Actor>
{
    protected override Vector2 GetRealPosition(Actor obj) => obj.ExactPosition;
    protected override Vector2 GetDrawPosition(Actor obj) => obj.Position;
    protected override void SetPosition(Actor obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Actor obj) => obj.Visible;
}

public class LevelZoomSmoothingState : FloatSmoothingState<Level>
{
    protected override SmoothingMode? OverrideSmoothingMode => SmoothingMode.Extrapolate;
    protected override bool CancelSmoothing => CelesteTasInterop.CenterCamera;
    protected override float GetValue(Level obj) => Math.Max(obj.Zoom, 1f / 6f);
    protected override void SetValue(Level obj, float value)
	{
		// Extrapolating the zoom means that it can overshoot which looks
		// like garbage; we make it wait an extra frame when crossing 1
		// to be sure.
		if (value < 1 && History[1] >= 1 || value > 1 && History[1] <= 1)
		{
			return;
		}
		
		obj.Zoom = Math.Max(value, 1f / 6f);
	}
}