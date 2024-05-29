using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.States;

public class EntitySmoothingState : PositionSmoothingState<Entity>
{
    protected override Vector2 GetRealPosition(Entity obj) => obj.Position;
    protected override void SetPosition(Entity obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Entity obj) => obj.Visible;
}

public class ZipMoverPercentSmoothingState : FloatSmoothingState<ZipMover>
{
    protected override float GetValue(ZipMover obj) => obj.percent;

    protected override void SetValue(ZipMover obj, float value)
    {
        obj.percent = value;
        obj.Position = GetPositionAtPercent(obj, value);
    }

    public Vector2 GetPositionAtPercent(ZipMover obj, float percent, bool round = true)
    {
        return round ? Vector2.Lerp(obj.start, obj.target, percent).Round() :
            Vector2.Lerp(obj.start, obj.target, percent);
    }
}

public class EyeballsSmoothingState : PositionSmoothingState<DustGraphic.Eyeballs>
{
    protected override Vector2 GetRealPosition(DustGraphic.Eyeballs obj) => obj.Dust.RenderPosition;

    protected override void SetPosition(DustGraphic.Eyeballs obj, Vector2 position) =>
        throw new System.NotSupportedException();

    protected override bool GetVisible(DustGraphic.Eyeballs obj) => true;
}

public class ComponentSmoothingState : PositionSmoothingState<GraphicsComponent>
{
    protected override Vector2 GetRealPosition(GraphicsComponent obj) => obj.Position;
    protected override void SetPosition(GraphicsComponent obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(GraphicsComponent obj) => obj.Visible;
}

public class ScreenWipeSmoothingState : FloatSmoothingState<ScreenWipe>
{
    protected override float GetValue(ScreenWipe obj) => obj.Percent;
    protected override void SetValue(ScreenWipe obj, float value) => obj.Percent = value;
}

public class FinalBossBeamSmoothingState : AngleSmoothingState<FinalBossBeam>
{
    protected override float GetValue(FinalBossBeam obj) => obj.angle;
    protected override void SetValue(FinalBossBeam obj, float value) => obj.angle = value;
}

public class CameraSmoothingState : PositionSmoothingState<Camera>
{
    protected override Vector2 GetRealPosition(Camera obj) => obj.Position;
    protected override void SetPosition(Camera obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Camera obj) => true;
}

public class PlatformSmoothingState : PositionSmoothingState<Platform>
{
    protected override Vector2 GetRealPosition(Platform obj) => obj.ExactPosition;
    protected override Vector2 GetDrawPosition(Platform obj) => obj.Position;
    protected override void SetPosition(Platform obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Platform obj) => obj.Visible;
}

public class ActorSmoothingState : PositionSmoothingState<Actor>
{
    protected override Vector2 GetRealPosition(Actor obj) => obj.ExactPosition;
    protected override Vector2 GetDrawPosition(Actor obj) => obj.Position;
    protected override void SetPosition(Actor obj, Vector2 position) => obj.Position = position;
    protected override bool GetVisible(Actor obj) => obj.Visible;
}