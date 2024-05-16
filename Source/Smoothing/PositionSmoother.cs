using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public class PositionSmoother : MotionSmoother
{
    public void SmoothCamera(Camera camera) => SmoothObject(camera, new CameraSmoothingState());
    public void SmoothScreenWipe(ScreenWipe wipe) => SmoothObject(wipe, new ScreenWipeSmoothingState());

    public void SetPositions()
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

    public void ResetPositions()
    {
        foreach (var (obj, state) in States())
            state.SetPosition(obj, state.OriginalPosition);
    }
}