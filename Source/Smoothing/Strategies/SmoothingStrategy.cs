using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public abstract class SmoothingStrategy<T> : ToggleableFeature<T> where T : SmoothingStrategy<T>
{
    private readonly ConditionalWeakTable<object, ISmoothingState> _objectStates = new();

    protected IEnumerable<KeyValuePair<object, ISmoothingState>> States()
    {
        return _objectStates;
    }

    public void ClearStates()
    {
        _objectStates.Clear();
    }

    protected void SmoothObject(object obj, ISmoothingState state)
    {
        if (_objectStates.TryGetValue(obj, out _))
            return;

        _objectStates.Add(obj, state);
    }

    public void StopSmoothingObject(object obj)
    {
        _objectStates.Remove(obj);
    }

    public void UpdatePositions()
    {
        foreach (var (obj, state) in States())
            state.UpdateHistory(obj);
    }

    public void CalculateSmoothedPositions(double elapsedSeconds, SmoothingMode mode)
    {
        // Ensure the player is smoothed first, so that other objects can use the player's smoothed position
        var player = MotionSmoothingHandler.Instance.Player;
        if (player != null)
        {
            var state = GetState(player);
            state?.Smooth(player, elapsedSeconds, mode);
        }

        foreach (var (obj, state) in States())
            if (obj != player)
                state.Smooth(obj, elapsedSeconds, mode);
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