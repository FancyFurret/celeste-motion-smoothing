using System;

namespace Celeste.Mod.MotionSmoothing.FrameUncap;

public interface IFrameUncapStrategy
{
    public TimeSpan TargetUpdateElapsedTime { get; protected set; }
    public TimeSpan TargetDrawElapsedTime { get; protected set; }

    public void SetTargetFramerate(int updateFramerate, int drawFramerate);
}