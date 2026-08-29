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

    // Vanilla builds the filler as a plain `new Entity(...)`, which lets PositionSmoother skip the
    // table probe for anything of a more derived type -- worth doing, since it is otherwise paid
    // for every smoothed entity on every frame. If a filler that isn't a plain Entity ever turns
    // up (some mod rewriting AddSprite), this latches and the gate opens back up for everything.
    public static bool HasNonPlainFillers { get; private set; }

    public CrystalStaticSpinner GetSpinnerForFiller(Entity entity)
    {
        if (entity == null) return null;
        return _fillerToSpinner.TryGetValue(entity, out var spinner) ? spinner : null;
    }

    private void Register(Entity filler, CrystalStaticSpinner spinner)
    {
        if (filler.GetType() != typeof(Entity))
            HasNonPlainFillers = true;

        _fillerToSpinner.Remove(filler);
        _fillerToSpinner.Add(filler, spinner);
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
                    Register(spinner.filler, spinner);
            }
        }
    }

    public override void Disable()
    {
        // Hand the fillers and borders back before the hook that maintains them goes away, or a
        // room left mid-cull keeps them hidden with nothing left to turn them back on.
        RestoreVanillaVisibility();
        base.Disable();
    }

    protected override void Hook()
    {
        base.Hook();
        MotionSmoothingModule.DisableInlining(typeof(CrystalStaticSpinner), "AddSprite");
        MotionSmoothingModule.DisableInlining(typeof(Level), "Update");
        On.Celeste.CrystalStaticSpinner.AddSprite += AddSpriteHook;
        On.Celeste.Level.Update += LevelUpdateHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        On.Celeste.CrystalStaticSpinner.AddSprite -= AddSpriteHook;
        On.Celeste.Level.Update -= LevelUpdateHook;
    }

    // --- Off-camera filler and border culling ---------------------------------------------------
    //
    // A crystal spinner hides itself when it leaves the camera, but the two entities it adds to the
    // scene the first time it expands never go away and never stop drawing:
    //
    //   * `filler` (the tiles that bridge adjacent spinners) keeps Visible = true forever, so every
    //     one of its images is pushed through SpriteBatch on every frame for the rest of the room,
    //     hundreds of screens off camera included.
    //   * `border` keeps Visible = true too. Its Render is a no-op while the spinner is hidden, but
    //     the entity list still walks it, and under Motion Smoothing that walk carries the
    //     push/pop and (in Fancy) the subpixel-interception delegates.
    //
    // Nothing ever un-expands, so the cost ratchets up as more of the room is scrolled past and
    // only drops at a room transition, when the entities are finally removed. Vanilla absorbs it at
    // 60fps. Motion Smoothing puts roughly five stacked detours on every sprite drawn, so the same
    // content costs about three times as much -- which is what turns a room full of spinners from
    // playable into half framerate.
    //
    // Both are made to follow what is actually on screen. Neither changes a pixel:
    //
    //   * The border exactly mirrors `spinner.Visible`, which is the test its own Render already
    //     makes before drawing anything.
    //   * The filler is hidden only when the spinner is outside a box far larger than the one the
    //     spinner itself uses (FillerMargin vs. vanilla InView's 16px), and never while the spinner
    //     is visible. A filler image sits at most 12px from the spinner -- half the 24px radius
    //     AddSprite pairs them within -- so the margin clears the furthest drawn pixel twice over.

    private const float FillerMargin = 64f;

    private static void LevelUpdateHook(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);

        // After orig, so the spinners have already decided their own visibility this tick. The
        // camera rectangle is from the previous tick's Scene.AfterUpdate, which at a few pixels of
        // camera movement is nowhere near the margin above.
        if (Instance.Enabled && OffscreenCulling.Active)
            CullOffscreenDecorations(self);
    }

    private static void CullOffscreenDecorations(Level level)
    {
        foreach (CrystalStaticSpinner spinner in level.Tracker.GetEntities<CrystalStaticSpinner>())
        {
            if (spinner.border != null)
                spinner.border.Visible = spinner.Visible;

            if (spinner.filler == null)
                continue;

            // `|| spinner.Visible` rather than the box alone: a spinner only re-checks its own view
            // on a 0.25s interval, so it can still be Visible a little after leaving the box on a
            // fast camera. This keeps the filler from disappearing out from under a spinner that is
            // still drawing.
            spinner.filler.Visible = spinner.Visible
                                     || OffscreenCulling.IsWithin(spinner.Position, FillerMargin);
        }
    }

    private static void RestoreVanillaVisibility()
    {
        if (Engine.Scene is not Level level) return;

        foreach (CrystalStaticSpinner spinner in level.Tracker.GetEntities<CrystalStaticSpinner>())
        {
            if (spinner.border != null) spinner.border.Visible = true;
            if (spinner.filler != null) spinner.filler.Visible = true;
        }
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
            Instance.Register(self.filler, self);
    }
}
