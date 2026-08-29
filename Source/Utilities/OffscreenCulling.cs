using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// The camera rectangle that decides whether an object is worth smoothing at all, refreshed once per
// update tick from Scene.AfterUpdate.
//
// Every smoothed object costs work twice over: a position-history sample each update tick, and a
// smoothing pass each drawn frame. Both are O(objects in the room), not O(objects on screen, and a
// room that has had thousands of crystal spinners scrolled into view keeps every one of them --
// plus the filler and border entity each spinner permanently adds to the scene -- on that walk
// forever. None of it can be seen. This is the gate that drops them.
internal static class OffscreenCulling
{
    // How far beyond the camera an arbitrary entity still counts as on screen.
    //
    // An entity's Position (or collider) is where it *is*, not necessarily where it *draws*: a
    // sprite can be anchored well away from its origin. The margin is deliberately several times
    // the largest such offset in vanilla, because the cost of being too generous is a few more
    // objects on the walk, while the cost of being too tight is an object on screen that stops
    // interpolating. Callers that know exactly what an object draws can ask for a tighter one
    // through IsWithin.
    public const float EntityMargin = 128f;

    // False outside a level, where there is no camera to cull against and nothing to gain.
    public static bool Active { get; private set; }

    // The camera rectangle, expanded for zoom but with no entity margin applied -- callers add
    // their own, so the zoom arithmetic lives in one place.
    private static float _left, _top, _right, _bottom;

    // HUD and SubHUD entities are positioned in screen space, not world space -- GameplayRenderer
    // draws everything *except* these two tags, and HudRenderer/SubHudRenderer draw them in their
    // own coordinate systems. Testing their Position against a world-space camera rectangle is
    // meaningless, and would cull things like the speedrun timer or an easing on-screen message
    // that genuinely want smoothing. Resolved per tick because Everest fills SubHUD in at load.
    private static int _screenSpaceTags;

    public static void Refresh()
    {
        Active = false;

        if (Engine.Scene is not Level level || level.Camera is not { } camera)
            return;

        float viewWidth = camera.Viewport.Width;
        float viewHeight = camera.Viewport.Height;

        // Zoom below 1 shows more of the level than the viewport covers, spread around
        // ZoomFocusPoint. Rather than work out where the focus point puts it, expand by the whole
        // difference on every side -- over-estimating costs nothing here.
        float shownWidth = viewWidth;
        float shownHeight = viewHeight;
        if (level.Zoom > 0f && level.Zoom < 1f)
        {
            shownWidth = viewWidth / level.Zoom;
            shownHeight = viewHeight / level.Zoom;
        }

        float padX = shownWidth - viewWidth;
        float padY = shownHeight - viewHeight;

        _left = camera.X - padX;
        _top = camera.Y - padY;
        _right = camera.X + shownWidth + padX;
        _bottom = camera.Y + shownHeight + padY;

        var subHud = TagsExt.SubHUD;
        _screenSpaceTags = (int)Tags.HUD | (subHud != null ? (int)subHud : 0);

        Active = true;
    }

    public static bool IsOnScreen(Entity entity)
    {
        // Drawn in screen space rather than world space; the rectangle below does not apply to it.
        if (entity.TagCheck(_screenSpaceTags))
            return true;

        var position = entity.Position;

        // Entity.Left/Right/Top/Bottom would read this four times over, through properties Everest
        // marks NoInlining. The collider matters because a large platform's Position is its
        // top-left, so it can sit outside the view while still covering it.
        var collider = entity.Collider;
        if (collider == null)
            return IsWithin(position, EntityMargin);

        return position.X + collider.Right > _left - EntityMargin
               && position.X + collider.Left < _right + EntityMargin
               && position.Y + collider.Bottom > _top - EntityMargin
               && position.Y + collider.Top < _bottom + EntityMargin;
    }

    // For callers that know how far from a point their content actually reaches, and so can justify
    // a tighter margin than EntityMargin's blanket one.
    public static bool IsWithin(Vector2 position, float margin) =>
        position.X > _left - margin && position.X < _right + margin
        && position.Y > _top - margin && position.Y < _bottom + margin;
}
