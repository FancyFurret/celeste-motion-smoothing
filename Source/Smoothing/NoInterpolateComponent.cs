using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

// Marker component added to an entity (via the MotionSmoothing interop's
// DisableInterpolation) to opt it out of all object smoothing. Inactive and
// invisible — it does nothing on its own; the smoothing strategies check for
// its presence and skip the entity.
internal class NoInterpolateComponent() : Component(false, false);

internal static class NoInterpolate
{
    // Resolves the owning entity for a smoothed object and reports whether
    // interpolation has been disabled for it. Components are mapped to their
    // entity, so smoothing a component (e.g. a Booster's sprite) is also
    // suppressed when its entity opts out. Non-entity smoothing targets
    // (Camera, Level, ScreenWipe) are never disabled.
    public static bool IsDisabled(object obj)
    {
        var entity = obj switch
        {
            Component component => component.Entity,
            Entity e => e,
            _ => null
        };

        return entity?.Get<NoInterpolateComponent>() != null;
    }
}
