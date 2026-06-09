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
        foreach (var (obj, state) in States())
            if (!NoInterpolate.IsDisabled(obj))
                state.SetSmoothed(obj);
    }

    public void ResetPositions()
    {
        foreach (var (obj, state) in States())
            if (!NoInterpolate.IsDisabled(obj))
                state.SetOriginal(obj);
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