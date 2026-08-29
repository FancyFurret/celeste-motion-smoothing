using Celeste.Mod.MotionSmoothing.Smoothing.States;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public class ValueSmoother : SmoothingStrategy<ValueSmoother>
{
    public new void SmoothObject(object obj, ISmoothingState state)
    {
        base.SmoothObject(obj, state);
    }

    public void SetPositions()
    {
        var states = States();
        for (var i = 0; i < states.Count; i++)
        {
            var (obj, state) = states[i];
            if (!NoInterpolate.IsDisabled(obj))
                state.SetSmoothed(obj);
        }
    }

    public void ResetPositions()
    {
        var states = States();
        for (var i = 0; i < states.Count; i++)
        {
            var (obj, state) = states[i];
            if (!NoInterpolate.IsDisabled(obj))
                state.SetOriginal(obj);
        }
    }

    public override void PreRender()
    {
        base.PreRender();
        SetPositions();
    }

    public override void PostRender()
    {
        ResetPositions();
        base.PostRender();
    }
}
