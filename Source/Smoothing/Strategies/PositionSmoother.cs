namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public class PositionSmoother : SmoothingStrategy<PositionSmoother>
{
    private void SetPositions()
    {
        foreach (var (obj, state) in States())
        {
            // Save the current position to the history
            // This fixes issues with the camera but could potentially break other things, since it's updating the
            // history in draw
            state.PositionHistory[0] = state.GetPosition(obj);
            state.SetPosition(obj, state.SmoothedPosition);
        }
    }

    private void ResetPositions()
    {
        foreach (var (obj, state) in States())
            state.SetPosition(obj, state.OriginalPosition);
    }

    protected override void PreRender()
    {
        base.PreRender();
        SetPositions();
    }

    protected override void PostRender()
    {
        ResetPositions();
        base.PostRender();
    }
}