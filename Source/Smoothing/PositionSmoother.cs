using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public class PositionSmoother : MotionSmoother
{
    public void SmoothCamera(Camera camera) => SmoothObject(new CameraSmoothingState(camera));

    public void SetPositions()
    {
        foreach (var state in States())
        {
            // Save the current position to the history
            // This fixes issues with the camera (which is the only thing this class smooths ATM)
            // but could potentially break other things, since it's updating the history in draw
            state.PositionHistory[0] = state.Position;
            state.Position = state.SmoothedPosition;
        }
    }

    public void ResetPositions()
    {
        foreach (var state in States())
            state.Position = state.OriginalPosition;
    }
}