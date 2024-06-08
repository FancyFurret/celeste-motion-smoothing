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

    public Vector2 GetPositionAtPercent(ZipMover obj, float percent)
    {
        return Vector2.Lerp(obj.start, obj.target, percent).Round();
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
    protected override Vector2 GetRealPosition(GraphicsComponent obj)
    {
        if (obj.Entity is Booster booster)
        {
            // Boosters use the player's draw position, not real position, so make sure we smooth with the player's 
            // real position instead
            var player = MotionSmoothingHandler.Instance.Player;
            if ((player.StateMachine.State == 2 || player.StateMachine.state == 5) && booster.BoostingPlayer)
            {
                var playerRealCenterX = player.ExactPosition.X + (player.Collider?.Center.X ?? 0);
                var playerRealCenterY = player.ExactPosition.Y + (player.Collider?.Center.Y ?? 0);
                return new Vector2(playerRealCenterX, playerRealCenterY) + Booster.playerOffset - booster.Position;
            }
        }

        return obj.Position;
    }

    protected override Vector2 GetDrawPosition(GraphicsComponent obj)
    {
        // Boosters floor the position
        if (obj.Entity is Booster)
            return obj.Position.Floor();

        return obj.Position;
    }

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

    protected override void SetSmoothed(Camera obj)
    {
        OriginalRealPosition = obj.Position;
        RealPositionHistory[0] = obj.Position;
        OriginalDrawPosition = obj.Position;
        DrawPositionHistory[0] = obj.Position;
        obj.Position = SmoothedRealPosition.Floor();
    }

    protected override void SetOriginal(Camera obj)
    {
        obj.Position = OriginalRealPosition;
    }
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

public class LevelZoomSmoothingState : FloatSmoothingState<Level>
{
    protected override float GetValue(Level obj) => obj.Zoom;
    protected override void SetValue(Level obj, float value) => obj.Zoom = value;
}