using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// Keeps the edges of Madeline's hair from flickering while it should be holding still -- falling
// while holding a direction, say. Nothing to do with the framerate: the hair is simulated once per
// physics frame, in Level.AfterUpdate, and the flicker is already there by the time anything draws.
//
// PlayerHair.AfterUpdate anchors the chain to Sprite.RenderPosition, which reads Entity.Position --
// always whole pixels for an Actor, the subpixels living in movementCounter. At a steady speed she
// advances in uneven whole-pixel steps (1, 2, 1, 2 px at 90 px/s), and since each node chases the one
// before it at a fixed rate and is clamped to 3 px from it, the links never settle: every node's
// offset from the head wobbles by up to ~0.3 px on a short cycle. The nodes past the head are drawn
// scaled down, point-sampled and at fractional positions, so that wobble is enough to flip which edge
// pixels of each circle fill in.
//
// Simulated from her exact position instead, the offsets hold constant at a constant velocity. The
// simulation only ever looks at nodes relative to the head, so rather than moving the head to her
// exact position, the other nodes are moved by the change in her remainder since last frame. That
// keeps the head exactly what vanilla computes -- Render floors it, so even a float round trip through
// her remainder could put it a pixel off -- and keeps Nodes in vanilla's frame for everything else
// that reads it (TrailManager, mirrors, other mods). Drawing is left entirely to vanilla.
public class HairStabilizer : ToggleableFeature<HairStabilizer>
{
    // The remainder of the hair's owner as of each hair's last AfterUpdate.
    private static ConditionalWeakTable<PlayerHair, StrongBox<Vector2>> _lastRemainders = new();

    protected override void Hook()
    {
        base.Hook();

        // Remainders recorded before the feature was last turned off no longer describe the nodes.
        _lastRemainders = new ConditionalWeakTable<PlayerHair, StrongBox<Vector2>>();

        MotionSmoothingModule.DisableInlining(typeof(PlayerHair), "AfterUpdate");
        On.Celeste.PlayerHair.AfterUpdate += PlayerHairAfterUpdateHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        On.Celeste.PlayerHair.AfterUpdate -= PlayerHairAfterUpdateHook;
    }

    private static void PlayerHairAfterUpdateHook(On.Celeste.PlayerHair.orig_AfterUpdate orig, PlayerHair self)
    {
        var lastRemainder = _lastRemainders.GetValue(self, _ => new StrongBox<Vector2>());

        // Anything that isn't an Actor keeps its subpixels in Position already.
        var remainder = self.Sprite?.Entity is Actor actor ? actor.PositionRemainder : Vector2.Zero;

        // Where the nodes sit relative to her exact position, expressed against this frame's whole-pixel
        // head. The head itself is recomputed by orig.
        var shift = lastRemainder.Value - remainder;
        lastRemainder.Value = remainder;

        if (shift != Vector2.Zero)
        {
            var nodes = self.Nodes;
            for (int i = 1; i < nodes.Count; i++)
                nodes[i] += shift;
        }

        orig(self);
    }
}
