using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public abstract class SmoothingState
{
    public Vector2[] PositionHistory { get; } = new Vector2[2];
    public Vector2 SmoothedPosition { get; set; }
    public Vector2 OriginalPosition => PositionHistory[0];
    public bool WasInvisible { get; set; }

    public abstract Vector2 GetPosition(object obj);
    public abstract void SetPosition(object obj, Vector2 position);
    public abstract bool GetVisible(object obj);
    public virtual bool IsAngle => false;
}

public class EntitySmoothingState : SmoothingState
{
    public override Vector2 GetPosition(object obj) => ((Entity)obj).Position;
    public override void SetPosition(object obj, Vector2 position) => ((Entity)obj).Position = position;
    public override bool GetVisible(object obj) => ((Entity)obj).Visible;
}

public class ZipMoverSmoothingState : SmoothingState
{
    public override Vector2 GetPosition(object obj) => ((ZipMover.ZipMoverPathRenderer)obj).ZipMover.Position;

    public override void SetPosition(object obj, Vector2 position) =>
        ((ZipMover.ZipMoverPathRenderer)obj).ZipMover.Position = position;

    public override bool GetVisible(object obj) => true;
}

public class EyeballsSmoothingState : SmoothingState
{
    public override Vector2 GetPosition(object obj) => ((DustGraphic.Eyeballs)obj).Dust.RenderPosition;
    public override void SetPosition(object obj, Vector2 position) => throw new System.NotSupportedException();
    public override bool GetVisible(object obj) => true;
}

public class ComponentSmoothingState : SmoothingState
{
    public override Vector2 GetPosition(object obj) => ((GraphicsComponent)obj).Position;
    public override void SetPosition(object obj, Vector2 position) => ((GraphicsComponent)obj).Position = position;
    public override bool GetVisible(object obj) => ((GraphicsComponent)obj).Visible;
}

public class CameraSmoothingState : SmoothingState
{
    public override Vector2 GetPosition(object obj) => ((Camera)obj).Position;
    public override void SetPosition(object obj, Vector2 position) => ((Camera)obj).Position = position;
    public override bool GetVisible(object obj) => true;
}

public class ScreenWipeSmoothingState : SmoothingState
{
    public override Vector2 GetPosition(object obj) => new(((ScreenWipe)obj).Percent, 0f);
    public override void SetPosition(object obj, Vector2 position) => ((ScreenWipe)obj).Percent = position.X;
    public override bool GetVisible(object obj) => true;
}

public class FinalBossBeamSmoothingState : SmoothingState
{
    public override Vector2 GetPosition(object obj) => new(((FinalBossBeam)obj).angle, 0f);
    public override void SetPosition(object obj, Vector2 position) => ((FinalBossBeam)obj).angle = position.X;
    public override bool GetVisible(object obj) => ((FinalBossBeam)obj).Visible;
    public override bool IsAngle => true;
}

public abstract class MotionSmoother<T> where T : MotionSmoother<T>
{
    private const float MaxLerpDistance = 50f;

    public bool Enabled { get; set; } = true;

    protected static T Instance { get; private set; }
    protected List<Hook> Hooks { get; } = new();

    private readonly ConditionalWeakTable<object, SmoothingState> _objectStates = new();
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private long _lastTicks;

    protected MotionSmoother()
    {
        Instance = (T)this;
    }

    public virtual void Hook()
    {
        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Monocle.Engine.Draw += EngineDrawHook;
    }

    public virtual void Unhook()
    {
        foreach (var hook in Hooks)
            hook.Dispose();
        Hooks.Clear();

        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Monocle.Engine.Draw -= EngineDrawHook;
    }

    protected IEnumerable<KeyValuePair<object, SmoothingState>> States()
    {
        return _objectStates;
    }

    public void ClearStates()
    {
        _objectStates.Clear();
    }

    public void SmoothObject(object obj)
    {
        if (_objectStates.TryGetValue(obj, out _))
            return;

        SmoothingState state = obj switch
        {
            ZipMover.ZipMoverPathRenderer => new ZipMoverSmoothingState(),
            FinalBossBeam => new FinalBossBeamSmoothingState(),
            DustGraphic.Eyeballs => new EyeballsSmoothingState(),
            Camera => new CameraSmoothingState(),
            ScreenWipe => new ScreenWipeSmoothingState(),
            Entity => new EntitySmoothingState(),
            GraphicsComponent => new ComponentSmoothingState(),
            _ => throw new System.NotSupportedException($"Unsupported object type: {obj.GetType()}")
        };

        _objectStates.Add(obj, state);
    }

    public void UnsmoothObject(object obj)
    {
        _objectStates.Remove(obj);
    }

    private void UpdatePositions()
    {
        _lastTicks = _timer.ElapsedTicks;

        foreach (var (obj, state) in States())
        {
            state.PositionHistory[1] = state.PositionHistory[0];
            state.PositionHistory[0] = state.GetPosition(obj);

            if (!state.GetVisible(obj))
                state.WasInvisible = true;
        }
    }

    private void CalculateSmoothedPositions()
    {
        var elapsedTicks = _timer.ElapsedTicks - _lastTicks;
        var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
        var player = Engine.Scene?.Tracker.GetEntity<Player>();

        foreach (var (obj, state) in States())
        {
            state.SmoothedPosition = state.GetPosition(obj);

            // If the position is moving to zero, just snap to the current position
            if (state.PositionHistory[0] == Vector2.Zero || state.PositionHistory[1] == Vector2.Zero)
                continue;

            // If the entity was invisible but is now visible, snap to the current position
            if (state.WasInvisible && state.GetVisible(obj))
            {
                state.WasInvisible = false;
                continue;
            }

            // Manually fix boosters, can't figure out a better way of doing this
            // Boosters do not set the sprite to invisible, and if a player is entering a booster as it respawns,
            // it does not set the position to zero
            if (obj is Sprite { Entity: Booster booster })
                if (!booster.dashRoutine.Active && booster.respawnTimer <= 0)
                    continue;

            // If the distance is too large, just snap to the current position
            if (Vector2.DistanceSquared(state.PositionHistory[0], state.PositionHistory[1]) >
                MaxLerpDistance * MaxLerpDistance)
                continue;

            if (state is EntitySmoothingState entityState && player != null &&
                (obj == player || obj == player.Holding?.Entity))
            {
                switch (MotionSmoothingModule.Settings.PlayerSmoothing)
                {
                    case MotionSmoothingSettings.PlayerSmoothingMode.Interpolate:
                        state.SmoothedPosition = MotionSmoothingMath.Interpolate(state.PositionHistory, elapsedSeconds);
                        continue;
                    case MotionSmoothingSettings.PlayerSmoothingMode.Extrapolate:
                        state.SmoothedPosition =
                            PlayerPositionExtrapolator.ExtrapolatePosition(player, entityState, elapsedSeconds);
                        continue;
                    case MotionSmoothingSettings.PlayerSmoothingMode.None:
                    default:
                        continue;
                }
            }

            // Doesn't seem to help much
            // if (state is CameraSmoothingState && MotionSmoothingModule.Settings.PlayerSmoothing ==
            //     MotionSmoothingSettings.PlayerSmoothingMode.Extrapolate)
            // {
            //     state.SmoothedPosition = Extrapolate(state.PositionHistory, elapsedSeconds);
            //     continue;
            // }

            state.SmoothedPosition = state.IsAngle
                ? MotionSmoothingMath.InterpolateAngle(state.PositionHistory, elapsedSeconds)
                : MotionSmoothingMath.Interpolate(state.PositionHistory, elapsedSeconds);
        }
    }


    protected Vector2 GetOffset(object obj)
    {
        if (obj == null) return Vector2.Zero;

        if (_objectStates.TryGetValue(obj, out var state))
            return state!.SmoothedPosition - state.OriginalPosition;

        return Vector2.Zero;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        orig(self, gameTime);
        if (Instance.Enabled)
            Instance.UpdatePositions();
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        if (Instance.Enabled)
            Instance.PreRender();
        orig(self, gameTime);
        if (Instance.Enabled)
            Instance.PostRender();
    }

    protected virtual void PreRender()
    {
        Instance.CalculateSmoothedPositions();
    }

    protected virtual void PostRender()
    {
    }
}