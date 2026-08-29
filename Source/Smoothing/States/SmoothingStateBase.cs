using System;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.States;

public interface ISmoothingState
{
    public bool Changed { get; }

    public void UpdateHistory(object obj);
    public void SetSmoothed(object obj);
    public void SetOriginal(object obj);
    public void Smooth(object obj, double elapsedSeconds, SmoothingMode mode);

    // Whether calling Smooth again right now would provably reproduce the value it already holds,
    // so the caller can skip it. Only ever true *after* a Smooth for the current update tick has
    // already run; the first drawn frame after every tick always recomputes. See
    // PositionSmoothingState for the conditions.
    public bool SmoothIsRedundant(SmoothingMode mode);
}

public interface ISmoothingState<T> : ISmoothingState
{
    public T[] History { get; }
    public T Smoothed { get; set; }
    public T Original { get; set; }

    public T GetValue(object obj);
    public void SetValue(object obj, T value);
}

public abstract class SmoothingState<TObject, TValue> : ISmoothingState<TValue>
{
    public bool Changed => !History[0].Equals(History[1]);

    public TValue[] History { get; } = new TValue[2];
    public TValue Smoothed { get; set; }
    public TValue Original { get; set; }
    protected TValue PreSmoothed { get; set; }
    protected bool _initialized;

    public TValue GetValue(object obj) => GetValue((TObject)obj);
    public void SetValue(object obj, TValue value) => SetValue((TObject)obj, value);

    protected virtual SmoothingMode? OverrideSmoothingMode => null;
    protected virtual bool CancelSmoothing => false;

    protected abstract TValue GetValue(TObject obj);
    protected abstract void SetValue(TObject obj, TValue value);
    protected abstract TValue SmoothValue(TObject obj, double elapsedSeconds, SmoothingMode mode);

    protected virtual void SetSmoothed(TObject obj)
    {
        if (CancelSmoothing || !_initialized) return;
        PreSmoothed = GetValue(obj);
        SetValue(obj, Smoothed);
    }

    protected virtual void SetOriginal(TObject obj)
    {
        if (CancelSmoothing || !_initialized) return;
        SetValue(obj, PreSmoothed);
    }

    public void UpdateHistory(object obj)
    {
        if (!_initialized)
        {
            var value = GetValue((TObject)obj);
            History[0] = value;
            History[1] = value;
            Original = value;
            Smoothed = value;
            _initialized = true;
            return;
        }

        History[1] = History[0];
        History[0] = GetValue((TObject)obj);
        Original = History[0];
    }

    public void SetSmoothed(object obj) => SetSmoothed((TObject)obj);
    public void SetOriginal(object obj) => SetOriginal((TObject)obj);

    public void Smooth(object obj, double elapsedSeconds, SmoothingMode mode)
    {
        if (OverrideSmoothingMode.HasValue)
            mode = OverrideSmoothingMode.Value;

        // Fixes pause buffering
        if (MotionSmoothingHandler.Instance.WasPaused || Engine.Scene.Paused)
            Smoothed = Original;
        else
            Smoothed = SmoothValue((TObject)obj, elapsedSeconds, mode);
    }

    // There are only ever a handful of these (the camera zoom, a zip mover's percent, a screen
    // wipe), so there is nothing to gain from working out whether they could be skipped.
    public bool SmoothIsRedundant(SmoothingMode mode) => false;
}

// Positions get a fancier state object in order to deal with visibility, and draw vs exact positions
public interface IPositionSmoothingState : ISmoothingState
{
    public Vector2[] RealPositionHistory { get; }
    public Vector2[] DrawPositionHistory { get; }
    public Vector2 SmoothedRealPosition { get; }
    public Vector2 OriginalRealPosition { get; }
    public Vector2 OriginalDrawPosition { get; }
    public bool WasInvisible { get; set; }

    public bool GetVisible(object obj);

    public Vector2 GetLastDrawPosition(SmoothingMode mode);
    public Vector2 GetLastRealPosition(SmoothingMode mode);
    public Vector2 GetSmoothedOffset(SmoothingMode mode);

    public bool IgnoreSubpixelMotionX { get; set; }
    public bool IgnoreSubpixelMotionY { get; set; }
    public int XDeltaSignChanges { get; set; }
    public int PrevXDeltaSign { get; set; }
    public int YDeltaSignChanges { get; set; }
    public int PrevYDeltaSign { get; set; }

    // The object's StaticMover, if it has one, resolved once and then kept in step by
    // MotionSmoothingHandler's Tracker.ComponentAdded/ComponentRemoved hooks. PositionSmoother
    // consults it on every smoothed object; doing so through Entity.Get<StaticMover>() meant a
    // linear scan of the object's whole ComponentList, per object, per drawn frame.
    public StaticMover CachedStaticMover { get; }

    // Whether this object could be the private filler Entity a CrystalStaticSpinner adds to the
    // scene -- see CrystalSpinnerFillerTracker. Vanilla builds it as a plain `new Entity(...)`, so
    // anything of a more derived type can skip the filler lookup entirely.
    public bool MayBeSpinnerFiller { get; }

    // Called when a StaticMover is attached to or detached from the object.
    public void RefreshStaticMover(object obj);
}

public abstract class PositionSmoothingState<T> : IPositionSmoothingState
{
    public bool Changed => !RealPositionHistory[0].Equals(RealPositionHistory[1]);

    public Vector2[] RealPositionHistory { get; } = new Vector2[3];
    public Vector2[] DrawPositionHistory { get; } = new Vector2[3];
    public Vector2 SmoothedRealPosition { get; protected set; }
    public Vector2 OriginalRealPosition { get; private set; }
    public Vector2 OriginalDrawPosition { get; private set; }
    protected Vector2 PreSmoothedPosition { get; set; }
    public bool WasInvisible { get; set; }
    protected bool _initialized;

    public bool IgnoreSubpixelMotionX { get; set; }
    public bool IgnoreSubpixelMotionY { get; set; }
    public int XDeltaSignChanges { get; set; }
    public int PrevXDeltaSign { get; set; }
    public int YDeltaSignChanges { get; set; }
    public int PrevYDeltaSign { get; set; }

    public StaticMover CachedStaticMover { get; private set; }
    public bool MayBeSpinnerFiller { get; private set; }

    // --- Redundant-smooth elision -------------------------------------------------------------
    //
    // Smooth() is called on every smoothed object once per *drawn* frame, but everything it reads
    // only advances once per *update tick*: the two position histories, the pause state, and the
    // smoothing mode (settings are only ever changed from an entity's Update). The single input
    // that varies between the draws of one tick is elapsedSeconds -- and for an object whose
    // history has not moved, elapsedSeconds cancels out of every path:
    //
    //   * Interpolate lerps between two equal endpoints,
    //   * Extrapolate finds a zero delta and returns the current position unchanged,
    //   * every cancel/snap path (ShouldCancelSmoothing, the direction-change checks, the
    //     subpixel-oscillation ignores) returns a history value, and
    //   * the oscillation detector's own bookkeeping sees a zero delta, so it makes no change to
    //     its counters either -- there are no side effects to lose.
    //
    // So the second and later Smooth calls within a tick recompute a value they already have. In a
    // room full of crystal spinners that is thousands of objects' worth of arithmetic per frame,
    // for nothing. _tickStable records that the above holds; _smoothedThisTick records that the
    // one call that does the work has already happened.
    //
    // Objects whose smoothed position is derived from *another* object's are excluded outright
    // (a StaticMover's platform, a spinner filler's spinner, an Actor's pusher): theirs can change
    // while their own history sits still, which is the entire reason those branches exist in
    // PositionSmoother. Everything else the elision has to hold across -- the pause state, the
    // smoothing mode, SillyMode -- can only change from an entity's Update, i.e. at a tick
    // boundary, where UpdateHistory clears the flag again. (A settings change also runs
    // ApplySettings, which rebuilds every state from scratch; the recorded mode is belt and
    // braces for a framerate change flipping ObjectSmoothing without going through it.)
    private bool _tickStable;
    private bool _smoothedThisTick;
    private SmoothingMode _smoothedMode;

    // Overridden to false by states whose Smooth reads something outside the position history.
    protected virtual bool AllowRedundantSmoothElision => true;

    public bool SmoothIsRedundant(SmoothingMode mode) =>
        _tickStable && _smoothedThisTick && _smoothedMode == mode;

    // The part of the cross-object test that is decided by the object's type, worked out once
    // rather than on every tick: whether the object is an Actor (pusher offsets, plus the player
    // and held-holdable paths), a spinner filler (forwards its spinner's smoothed position), or a
    // Booster's sprite (the carve-out that reads the booster's live dash/respawn state).
    private bool _typeHasCrossObjectDependency;

    public void RefreshStaticMover(object obj)
    {
        CachedStaticMover = obj is Entity entity ? entity.Get<StaticMover>() : null;
    }

    public bool GetVisible(object obj) => GetVisible((T)obj);

    protected virtual SmoothingMode? OverrideSmoothingMode => null;
    protected virtual bool CancelSmoothing => false;

    protected abstract Vector2 GetRealPosition(T obj);
    protected virtual Vector2 GetDrawPosition(T obj) => GetRealPosition(obj);

    protected abstract void SetPosition(T obj, Vector2 position);
    protected abstract bool GetVisible(T obj);

    protected virtual void SetSmoothed(T obj)
    {
        if (CancelSmoothing || !_initialized) return;
        PreSmoothedPosition = GetDrawPosition(obj);
        // SillyMode: write the unrounded SmoothedRealPosition so the Player (which is routed
        // through ValueSmoother → SetPosition rather than through PushSpriteSmoother) renders
        // at 1/6-px precision under the 6x composite. SetOriginal restores PreSmoothedPosition
        // after the draw, so physics is unaffected.
        SetPosition(obj, MotionSmoothingModule.Settings.SillyMode
            ? SmoothedRealPosition
            : SmoothedRealPosition.Round());
    }

    protected virtual void SetOriginal(T obj)
    {
        if (CancelSmoothing || !_initialized) return;
        SetPosition(obj, PreSmoothedPosition);
    }

    protected virtual void Smooth(T obj, double elapsedSeconds, SmoothingMode mode)
    {
        if (OverrideSmoothingMode.HasValue)
            mode = OverrideSmoothingMode.Value;

        // Fixes pause buffering (otherwise the player could be extrapolated, and then snap back to the location they
        // were paused at the next update
        if (MotionSmoothingHandler.Instance.WasPaused || Engine.Scene.Paused)
            SmoothedRealPosition = OriginalDrawPosition;
        else
            SmoothedRealPosition = PositionSmoother.Smooth(this, obj, elapsedSeconds, mode);
    }

    public void UpdateHistory(object obj)
    {
        if (!_initialized)
        {
            var realPos = GetRealPosition((T)obj);
            RealPositionHistory[0] = realPos;
            RealPositionHistory[1] = realPos;
            RealPositionHistory[2] = realPos;
            OriginalRealPosition = realPos;

            var drawPos = GetDrawPosition((T)obj).Round();
            DrawPositionHistory[0] = drawPos;
            DrawPositionHistory[1] = drawPos;
            DrawPositionHistory[2] = drawPos;
            OriginalDrawPosition = drawPos;

            SmoothedRealPosition = realPos;

            if (!GetVisible((T)obj))
                WasInvisible = true;

            // Resolved here rather than at construction because the state is created from
            // Tracker.EntityAdded, which Monocle raises *before* Entity.Added(scene) -- so the
            // entity's components have not attached to the scene yet at that point. By the first
            // AfterUpdate they have, and every later attach/detach comes through
            // MotionSmoothingHandler's component hooks.
            RefreshStaticMover(obj);

            // Fixed for the object's lifetime.
            MayBeSpinnerFiller = obj.GetType() == typeof(Entity);
            _typeHasCrossObjectDependency = MayBeSpinnerFiller
                                            || obj is Actor
                                            || obj is Sprite { Entity: Booster };

            _initialized = true;
            _tickStable = false;
            _smoothedThisTick = false;
            return;
        }

        RealPositionHistory[2] = RealPositionHistory[1];
        RealPositionHistory[1] = RealPositionHistory[0];
        RealPositionHistory[0] = GetRealPosition((T)obj);
        OriginalRealPosition = RealPositionHistory[0];

        DrawPositionHistory[2] = DrawPositionHistory[1];
        DrawPositionHistory[1] = DrawPositionHistory[0];
        DrawPositionHistory[0] = GetDrawPosition((T)obj).Round();
        OriginalDrawPosition = DrawPositionHistory[0];

        if (!GetVisible((T)obj))
            WasInvisible = true;

        // A fresh tick invalidates whatever the last one computed, whether or not anything moved.
        // Cheap tests first: the great majority of objects are ruled in or out by the two bools.
        _smoothedThisTick = false;
        _tickStable = AllowRedundantSmoothElision
                      && !_typeHasCrossObjectDependency
                      // Rides a platform: PositionSmoother follows the platform's offset instead,
                      // which moves while this object's own history sits still.
                      && CachedStaticMover == null
                      && RealPositionHistory[0] == RealPositionHistory[1]
                      && RealPositionHistory[1] == RealPositionHistory[2]
                      && DrawPositionHistory[0] == DrawPositionHistory[1]
                      && DrawPositionHistory[1] == DrawPositionHistory[2];
    }

    public void SetSmoothed(object obj) => SetSmoothed((T)obj);
    public void SetOriginal(object obj) => SetOriginal((T)obj);

    public void Smooth(object obj, double elapsedSeconds, SmoothingMode mode)
    {
        Smooth((T)obj, elapsedSeconds, mode);

        _smoothedThisTick = true;
        _smoothedMode = mode;
    }

    public Vector2 GetLastDrawPosition(SmoothingMode mode)
    {
        return mode == SmoothingMode.Interpolate ? DrawPositionHistory[1] : DrawPositionHistory[0];
    }

    // Sibling of GetLastDrawPosition that returns the *unrounded* historical position.
    // Used in SillyMode where pusher math (ActorPushTracker) and GetSmoothedOffset must
    // stay subpixel — otherwise pusher-carried actors on diagonal moveblocks would still
    // snap to the integer grid even though everything else is rendered at subpixel.
    public Vector2 GetLastRealPosition(SmoothingMode mode)
    {
        return mode == SmoothingMode.Interpolate ? RealPositionHistory[1] : RealPositionHistory[0];
    }

    public Vector2 GetSmoothedOffset(SmoothingMode mode)
    {
        return SmoothedRealPosition - GetLastDrawPosition(mode);
    }
}

public abstract class FloatSmoothingState<T> : SmoothingState<T, float>
{
    protected override float SmoothValue(T obj, double elapsedSeconds, SmoothingMode mode) =>
        SmoothingMath.Smooth(History, elapsedSeconds, mode);
}

public abstract class PercentSmoothingState<T> : SmoothingState<T, float>
{
    protected override float SmoothValue(T obj, double elapsedSeconds, SmoothingMode mode) =>
        Math.Clamp(SmoothingMath.Smooth(History, elapsedSeconds, mode), 0f, 1f);
}

public abstract class AngleSmoothingState<T> : SmoothingState<T, float>
{
    protected override float SmoothValue(T obj, double elapsedSeconds, SmoothingMode mode) =>
        SmoothingMath.SmoothAngle(History, elapsedSeconds, mode);
}