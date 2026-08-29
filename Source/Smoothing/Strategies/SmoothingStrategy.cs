using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public abstract class SmoothingStrategy<T> : ToggleableFeature<T> where T : SmoothingStrategy<T>
{
    private readonly ConditionalWeakTable<object, ISmoothingState> _objectStates = new();

    // Iteration snapshot of _objectStates. Enumerating a ConditionalWeakTable is expensive out of
    // all proportion to its size: MoveNext takes the table's lock and resolves a DependentHandle
    // through the GC for *every* entry. The loops below walk every smoothed object once per update
    // tick and once per drawn frame, and a spinner-heavy room holds thousands of them -- each
    // crystal spinner permanently adds a filler and a border entity to the scene the first time it
    // scrolls into view, and neither is ever taken back out -- so that per-entry cost showed up
    // directly as lost framerate that got worse the more of the room had been scrolled past.
    //
    // The table stays the source of truth for lookups (and for keeping states weak); this list is
    // only what we walk. It holds strong references between rebuilds, so an object that vanishes
    // without SmoothObject/StopSmoothingObject being called is kept alive until the next rebuild --
    // in practice until the next ClearStates, which every scene change runs (see
    // MotionSmoothingHandler.SmoothAllObjects and MotionSmoothingModule.ApplySettings).
    private readonly List<KeyValuePair<object, ISmoothingState>> _statesSnapshot = new();
    private bool _snapshotDirty;

    // Rebuilds are cheap relative to the walk, but they are not free, so structural changes only
    // mark the snapshot dirty and the rebuild happens the next time anything iterates.
    protected List<KeyValuePair<object, ISmoothingState>> States()
    {
        if (_snapshotDirty)
        {
            _snapshotDirty = false;
            _statesSnapshot.Clear();
            foreach (var pair in _objectStates)
                _statesSnapshot.Add(pair);
        }

        return _statesSnapshot;
    }

    // How many of the tracked objects are Components rather than Entities. PushSpriteSmoother asks
    // for a component's own offset on every single sprite it draws, and components are only ever
    // registered in narrow circumstances (today: a Booster's graphics). When none are, that whole
    // lookup -- a weak-table probe, per sprite -- can be skipped outright.
    private int _componentStateCount;
    protected bool HasComponentStates => _componentStateCount > 0;

    public void ClearStates()
    {
        _objectStates.Clear();
        _statesSnapshot.Clear();
        _snapshotDirty = false;
        _componentStateCount = 0;
    }

    protected bool SmoothObject(object obj, ISmoothingState state)
    {
        if (_objectStates.TryGetValue(obj, out _))
            return false;

        _objectStates.Add(obj, state);
        _snapshotDirty = true;
        if (obj is Component) _componentStateCount++;
        return true;
    }

    public void StopSmoothingObject(object obj)
    {
        if (!_objectStates.Remove(obj))
            return;

        _snapshotDirty = true;
        if (obj is Component) _componentStateCount--;
    }

    public void UpdatePositions()
    {
        // Indexed loops throughout: the snapshot is never structurally modified from inside these
        // callbacks, and indexing keeps a hypothetical future one from throwing mid-frame the way
        // a List enumerator would.
        var states = States();
        for (var i = 0; i < states.Count; i++)
        {
            var (obj, state) = states[i];
            state.UpdateHistory(obj);
        }
    }

    public void CalculateSmoothedPositions(double elapsedSeconds, SmoothingMode mode)
    {
        // Ensure the player is smoothed first, so that other objects can use the player's smoothed position
        var player = MotionSmoothingHandler.Instance.Player;
        if (player != null)
        {
            var playerState = GetState(player);
            playerState?.Smooth(player, elapsedSeconds, mode);
        }

        var states = States();
        for (var i = 0; i < states.Count; i++)
        {
            var (obj, state) = states[i];
            if (obj == player)
                continue;

            // Most objects in a room never move, and their smoothed position is a function of a
            // position history that only advances once per update tick. Recomputing it on every
            // drawn frame in between produces the same number every time -- see
            // ISmoothingState.SmoothIsRedundant for exactly when that holds.
            if (state.SmoothIsRedundant(mode))
                continue;

            state.Smooth(obj, elapsedSeconds, mode);
        }
    }

    public ISmoothingState GetState(object obj)
    {
        if (obj == null) return null;
        return _objectStates.TryGetValue(obj, out var state) ? state : null;
    }

    public virtual void PreRender()
    {
    }

    public virtual void PostRender()
    {
    }
}
