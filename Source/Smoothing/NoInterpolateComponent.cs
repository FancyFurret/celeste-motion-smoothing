using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

// Marker component added to an entity (via the MotionSmoothing interop's
// DisableInterpolation) to opt it out of all object smoothing. Inactive and
// invisible — it does nothing on its own; the smoothing strategies check for
// its presence and skip the entity.
internal class NoInterpolateComponent : Component
{
    // Live count of NoInterpolateComponents currently attached to entities.
    // IsDisabled runs once per PushSprite (every glyph/sprite drawn), so when
    // nothing has opted out — the common case, since this feature is opt-in via
    // interop — we use this to skip the per-call entity/component lookup entirely.
    internal static int Count { get; private set; }

    public NoInterpolateComponent() : base(false, false)
    {
    }

    public override void Added(Entity entity)
    {
        base.Added(entity);
        Count++;
    }

    public override void Removed(Entity entity)
    {
        base.Removed(entity);
        Count--;
    }
}

internal static class NoInterpolate
{
    // Resolves the owning entity for a smoothed object and reports whether
    // interpolation has been disabled for it. Components are mapped to their
    // entity, so smoothing a component (e.g. a Booster's sprite) is also
    // suppressed when its entity opts out. Non-entity smoothing targets
    // (Camera, Level, ScreenWipe) are never disabled.
    public static bool IsDisabled(object obj)
    {
        // Fast path: no entity has opted out, so skip the type-switch and the
        // per-entity component scan. This call is on the PushSprite hot path,
        // and text-heavy screens (the mod options menu with many mods loaded)
        // issue thousands of PushSprite calls per frame.
        if (NoInterpolateComponent.Count == 0)
            return false;

        // Even when a mod has opted entities out, skip the lookup while paused.
        // Gameplay frames are still drawn behind menus (e.g. the mod options
        // menu), but no entity updates while paused, so the smoothing offset
        // collapses to zero and honoring the opt-out has no visible effect —
        // there's no reason to pay the per-glyph cost on a text-heavy menu.
        if (Engine.Scene?.Paused == true)
            return false;

        var entity = obj switch
        {
            Component component => component.Entity,
            Entity e => e,
            _ => null
        };

        return entity?.Get<NoInterpolateComponent>() != null;
    }
}
