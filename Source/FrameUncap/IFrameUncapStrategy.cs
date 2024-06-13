using System;

namespace Celeste.Mod.MotionSmoothing.FrameUncap;

public interface IFrameUncapStrategy
{
    public TimeSpan TargetUpdateElapsedTime { get; protected set; }
    public TimeSpan TargetDrawElapsedTime { get; protected set; }

    public void SetTargetFramerate(double updateFramerate, double drawFramerate);
}