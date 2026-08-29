using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// What the camera currently shows, in world coordinates, refreshed once per update tick from
// Scene.AfterUpdate. Zoom is accounted for here so callers don't each have to.
//
// Used by CrystalSpinnerFillerTracker to stop the filler and border entities a crystal spinner
// leaves behind from drawing once they are off camera. Deliberately not used to decide what to
// smooth: an entity's Position is where it *is*, not necessarily where it *draws*, so a rectangle
// test on it is only safe where the caller knows what the object puts on screen.
internal static class OffscreenCulling
{
    // False outside a level, where there is no camera to cull against and nothing to gain.
    public static bool Active { get; private set; }

    // The camera rectangle, expanded for zoom but with no entity margin applied -- callers add
    // their own, so the zoom arithmetic lives in one place.
    private static float _left, _top, _right, _bottom;

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

        Active = true;
    }

    // Whether a point is within `margin` of what the camera shows. The caller supplies the margin,
    // because only it knows how far from that point its content actually reaches.
    public static bool IsWithin(Vector2 position, float margin) =>
        position.X > _left - margin && position.X < _right + margin
        && position.Y > _top - margin && position.Y < _bottom + margin;
}
