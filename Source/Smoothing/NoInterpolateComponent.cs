using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

// Marker component added to an entity (via the MotionSmoothing interop's
// DisableInterpolation) to opt it out of all object smoothing. Inactive and
// invisible — it does nothing on its own; the smoothing strategies check for
// its presence and skip the entity.
internal class NoInterpolateComponent : Component
{
    // Live count of NoInterpolateComponents attached to entities that are currently in
    // a scene. IsDisabled runs once per PushSprite (every glyph/sprite drawn), so when
    // nothing has opted out — the common case, since this feature is opt-in via interop —
    // we use this to skip the per-call entity/component lookup entirely.
    internal static int Count { get; private set; }

    // Whether this instance is currently contributing to Count. Counting must track
    // "attached to an in-scene entity," but the relevant transitions arrive through five
    // different callbacks (component added to / removed from an entity, entity added to /
    // removed from a scene, and scene end on a level transition — see Monocle's
    // Entity/Scene source). A monotonic Count++/Count-- in Added/Removed alone leaks
    // upward, because an entity removed from the scene fires EntityRemoved/SceneEnd, not
    // Removed — permanently defeating the fast path. This bool makes every transition
    // idempotent so the count stays accurate no matter the order or which path fires.
    private bool _counted;

    public NoInterpolateComponent() : base(false, false)
    {
    }

    private void SetCounted(bool value)
    {
        if (value == _counted) return;
        _counted = value;
        Count += value ? 1 : -1;
    }

    // Added to an entity: count immediately if that entity is already in a scene
    // (DisableObjectSmoothing is normally called on a live gameplay entity).
    public override void Added(Entity entity)
    {
        base.Added(entity);
        SetCounted(Scene != null);
    }

    // Removed from the entity (e.g. ReenableObjectSmoothing). base.Removed nulls Entity.
    public override void Removed(Entity entity)
    {
        base.Removed(entity);
        SetCounted(false);
    }

    // The owning entity entered a scene while this component was already attached.
    public override void EntityAdded(Scene scene)
    {
        base.EntityAdded(scene);
        SetCounted(true);
    }

    // The owning entity was removed from its scene.
    public override void EntityRemoved(Scene scene)
    {
        base.EntityRemoved(scene);
        SetCounted(false);
    }

    // The scene ended (level transition): entities are not individually Removed, so this
    // is the only signal that the component is no longer live.
    public override void SceneEnd(Scene scene)
    {
        base.SceneEnd(scene);
        SetCounted(false);
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
