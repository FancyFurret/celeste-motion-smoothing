using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public interface ISmoothingState
{
    public Vector2[] PositionHistory { get; }
    public Vector2 SmoothedPosition { get; set; }
    public Vector2 OriginalPosition { get; set; }
    public bool WasInvisible { get; set; }
    public bool IsAngle { get; }

    public Vector2 GetPosition(object obj);
    public void SetPosition(object obj, Vector2 position);
    public bool GetVisible(object obj);
}

public abstract class SmoothingState<T> : ISmoothingState
{
    public Vector2[] PositionHistory { get; } = new Vector2[2];
    public Vector2 SmoothedPosition { get; set; }
    public Vector2 OriginalPosition { get; set; }
    public bool WasInvisible { get; set; }
    public virtual bool IsAngle => false;

    public Vector2 GetPosition(object obj) => GetPosition((T)obj);
    public void SetPosition(object obj, Vector2 position) => SetPosition((T)obj, position);
    public bool GetVisible(object obj) => GetVisible((T)obj);

    protected abstract Vector2 GetPosition(T obj);
    protected abstract void SetPosition(T obj, Vector2 position);
    protected abstract bool GetVisible(T obj);
}

public abstract class SmoothingState1D<T> : SmoothingState<T>
{
    protected override Vector2 GetPosition(T obj) => new(GetValue(obj), 0f);
    protected override void SetPosition(T obj, Vector2 position) => SetValue(obj, position.X);

    protected abstract float GetValue(T obj);
    protected abstract void SetValue(T obj, float value);
}

public abstract class AngleSmoothingState<T> : SmoothingState1D<T>
{
    public override bool IsAngle => true;
}

public class EntitySmoothingState : SmoothingState<Entity>
{
    protected override Vector2 GetPosition(Entity obj) => obj.Position;
    protected override void SetPosition(Entity obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Entity obj) => obj.Visible;
}

public class ZipMoverSmoothingState : SmoothingState<ZipMover.ZipMoverPathRenderer>
{
    protected override Vector2 GetPosition(ZipMover.ZipMoverPathRenderer obj) => obj.ZipMover.Position;

    protected override void SetPosition(ZipMover.ZipMoverPathRenderer obj, Vector2 position) =>
        obj.ZipMover.Position = position;

    protected override bool GetVisible(ZipMover.ZipMoverPathRenderer obj) => true;
}

public class EyeballsSmoothingState : SmoothingState<DustGraphic.Eyeballs>
{
    protected override Vector2 GetPosition(DustGraphic.Eyeballs obj) => obj.Dust.RenderPosition;

    protected override void SetPosition(DustGraphic.Eyeballs obj, Vector2 position) =>
        throw new System.NotSupportedException();

    protected override bool GetVisible(DustGraphic.Eyeballs obj) => true;
}

public class ComponentSmoothingState : SmoothingState<GraphicsComponent>
{
    protected override Vector2 GetPosition(GraphicsComponent obj) => obj.Position;
    protected override void SetPosition(GraphicsComponent obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(GraphicsComponent obj) => obj.Visible;
}

public class ScreenWipeSmoothingState : SmoothingState1D<ScreenWipe>
{
    protected override float GetValue(ScreenWipe obj) => obj.Percent;
    protected override void SetValue(ScreenWipe obj, float value) => obj.Percent = value;
    protected override bool GetVisible(ScreenWipe obj) => true;
}

public class FinalBossBeamSmoothingState : AngleSmoothingState<FinalBossBeam>
{
    protected override float GetValue(FinalBossBeam obj) => obj.angle;
    protected override void SetValue(FinalBossBeam obj, float value) => obj.angle = value;
    protected override bool GetVisible(FinalBossBeam obj) => obj.Visible;
}

public class CameraSmoothingState : SmoothingState<Camera>
{
    protected override Vector2 GetPosition(Camera obj) => obj.Position;
    protected override void SetPosition(Camera obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Camera obj) => true;
}