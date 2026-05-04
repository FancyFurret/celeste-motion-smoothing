using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// CrystalStaticSpinner has a private `filler` Entity (the visual-only hole-filling
// background tiles between adjacent spinners). The filler is a plain Entity added
// to the Scene by the spinner; its only motion is `filler.Position = Position` set
// inside CrystalStaticSpinner.Update each frame. It does NOT have its own
// StaticMover.
//
// When the spinner is `attachToSolid` and rides a moving platform (e.g.
// FloatySpaceBlock), the spinner gets carried along in integer-pixel steps via its
// StaticMover, and motion smoothing renders it sub-pixel by routing through the
// platform's smoothed offset (see PositionSmoother.GetSmoothedPosition's StaticMover
// branch). The filler doesn't have a StaticMover, so it falls through to the
// generic SmoothingMath.Smooth path, which only knows about the filler's *own*
// position history. That history is integer-stepped (since it mirrors the
// spinner's integer-stepped Position) and lags the platform's true sub-pixel
// motion by up to one update tick — visually the filler hops 1 px relative to
// the spinner sprite that sits on top of it.
//
// This tracker registers `filler -> owning spinner` so the smoothing layer can
// borrow the spinner's StaticMover for the filler's offset math.
public class CrystalSpinnerFillerTracker : ToggleableFeature<CrystalSpinnerFillerTracker>
{
    private readonly ConditionalWeakTable<Entity, CrystalStaticSpinner> _fillerToSpinner = new();

    public CrystalStaticSpinner GetSpinnerForFiller(Entity entity)
    {
        if (entity == null) return null;
        return _fillerToSpinner.TryGetValue(entity, out var spinner) ? spinner : null;
    }

    public override void Enable()
    {
        base.Enable();

        // The AddSprite hook is installed during base.Enable() — but spinners that
        // already awoke in this scene (Awake → CreateSprites → AddSprite runs from
        // Scene.Begin's `orig(self)` *before* our SceneBeginHook reaches the
        // InLevel/Enable block) won't have fired the hook. Retroactively scan and
        // register so initial-level-load fillers are tracked.
        if (Engine.Scene is Level level)
        {
            foreach (CrystalStaticSpinner spinner in level.Tracker.GetEntities<CrystalStaticSpinner>())
            {
                if (spinner.filler != null)
                {
                    _fillerToSpinner.Remove(spinner.filler);
                    _fillerToSpinner.Add(spinner.filler, spinner);
                }
            }
        }
    }

    protected override void Hook()
    {
        base.Hook();
        On.Celeste.CrystalStaticSpinner.AddSprite += AddSpriteHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        On.Celeste.CrystalStaticSpinner.AddSprite -= AddSpriteHook;
    }

    private static void AddSpriteHook(On.Celeste.CrystalStaticSpinner.orig_AddSprite orig,
        CrystalStaticSpinner self, Vector2 offset)
    {
        orig(self, offset);

        // AddSprite lazily creates `filler` on first call; subsequent calls reuse it.
        // Register every time — ConditionalWeakTable lookups are cheap and this
        // keeps us robust if the spinner ever swaps out its filler (e.g. core-mode
        // re-instantiation, which calls ClearSprites / CreateSprites).
        if (self.filler != null)
        {
            Instance._fillerToSpinner.Remove(self.filler);
            Instance._fillerToSpinner.Add(self.filler, self);
        }
    }
}
