using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.MotionSmoothing.Smoothing.States;

public interface ISmoothingState
{
    public bool Changed { get; }

    public void UpdateHistory(object obj);
    public void SetSmoothed(object obj);
    public void SetOriginal(object obj);
    public void Smooth(object obj, double elapsedSeconds, SmoothingMode mode);
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

    public TValue GetValue(object obj) => GetValue((TObject)obj);
    public void SetValue(object obj, TValue value) => SetValue((TObject)obj, value);

    protected abstract TValue GetValue(TObject obj);
    protected abstract void SetValue(TObject obj, TValue value);
    protected abstract TValue SmoothValue(TObject obj, double elapsedSeconds, SmoothingMode mode);

    public void UpdateHistory(object obj)
    {
        History[1] = History[0];
        History[0] = GetValue((TObject)obj);
        Original = History[0];
    }

    public void SetSmoothed(object obj) => SetValue((TObject)obj, Smoothed);
    public void SetOriginal(object obj) => SetValue((TObject)obj, Original);

    public void Smooth(object obj, double elapsedSeconds, SmoothingMode mode) =>
        Smoothed = SmoothValue((TObject)obj, elapsedSeconds, mode);
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
}

public abstract class PositionSmoothingState<T> : IPositionSmoothingState
{
    public bool Changed => !RealPositionHistory[0].Equals(RealPositionHistory[1]);

    public Vector2[] RealPositionHistory { get; } = new Vector2[2];
    public Vector2[] DrawPositionHistory { get; } = new Vector2[2];
    public Vector2 SmoothedRealPosition { get; private set; }
    public Vector2 OriginalRealPosition { get; private set; }
    public Vector2 OriginalDrawPosition { get; private set; }
    public bool WasInvisible { get; set; }

    public bool GetVisible(object obj) => GetVisible((T)obj);

    protected abstract Vector2 GetRealPosition(T obj);
    protected virtual Vector2 GetDrawPosition(T obj) => GetRealPosition(obj);

    protected abstract void SetPosition(T obj, Vector2 position);
    protected abstract bool GetVisible(T obj);

    public void UpdateHistory(object obj)
    {
        RealPositionHistory[1] = RealPositionHistory[0];
        RealPositionHistory[0] = GetRealPosition((T)obj);
        OriginalRealPosition = RealPositionHistory[0];

        DrawPositionHistory[1] = DrawPositionHistory[0];
        DrawPositionHistory[0] = GetDrawPosition((T)obj);
        OriginalDrawPosition = DrawPositionHistory[0];

        if (!GetVisible((T)obj))
            WasInvisible = true;
    }

    public void SetSmoothed(object obj) => SetPosition((T)obj, SmoothedRealPosition);
    public void SetOriginal(object obj) => SetPosition((T)obj, OriginalDrawPosition);

    public void Smooth(object obj, double elapsedSeconds, SmoothingMode mode) =>
        SmoothedRealPosition = PositionSmoother.Smooth(this, obj, elapsedSeconds, mode);
}

public abstract class FloatSmoothingState<T> : SmoothingState<T, float>
{
    protected override float SmoothValue(T obj, double elapsedSeconds, SmoothingMode mode) =>
        SmoothingMath.Smooth(History, elapsedSeconds, mode);
}

public abstract class AngleSmoothingState<T> : SmoothingState<T, float>
{
    protected override float SmoothValue(T obj, double elapsedSeconds, SmoothingMode mode) =>
        SmoothingMath.SmoothAngle(History, elapsedSeconds, mode);
}