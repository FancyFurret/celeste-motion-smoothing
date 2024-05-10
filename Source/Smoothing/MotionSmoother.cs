using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public abstract class SmoothingState
{
    public Vector2[] PositionHistory { get; } = new Vector2[2];
    public Vector2 SmoothedPosition { get; set; }
    public Vector2 OriginalPosition => PositionHistory[0];
    public bool WasInvisible { get; set; }

    public abstract object Object { get; }
    public abstract Vector2 Position { get; set; }
    public abstract bool IsVisible { get; }
}

public class EntitySmoothingState : SmoothingState
{
    public Entity Entity { get; }

    public override object Object => Entity;

    public override Vector2 Position
    {
        get => Entity.Position;
        set => Entity.Position = value;
    }

    public override bool IsVisible => Entity.Visible;

    public EntitySmoothingState(Entity entity)
    {
        Entity = entity;
    }
}

public class ComponentSmoothingState : SmoothingState
{
    public GraphicsComponent Component { get; }

    public override object Object => Component;

    public override Vector2 Position
    {
        get => Component.Position;
        set => Component.Position = value;
    }

    public override bool IsVisible => Component.Visible;

    public ComponentSmoothingState(GraphicsComponent component)
    {
        Component = component;
    }
}

public class CameraSmoothingState : SmoothingState
{
    public Camera Camera { get; }

    public override object Object => Camera;

    public override Vector2 Position
    {
        get => Camera.Position;
        set => Camera.Position = value;
    }

    public override bool IsVisible => true;

    public CameraSmoothingState(Camera camera)
    {
        Camera = camera;
    }
}

public class ScreenWipeSmoothingState : SmoothingState
{
    public ScreenWipe ScreenWipe { get; }

    public override object Object => ScreenWipe;

    public override Vector2 Position
    {
        get => new(ScreenWipe.Percent, 0f);
        set => ScreenWipe.Percent = value.X;
    }

    public override bool IsVisible => true;

    public ScreenWipeSmoothingState(ScreenWipe screenWipe)
    {
        ScreenWipe = screenWipe;
    }
}

public abstract class MotionSmoother
{
    private const float MaxLerpDistance = 50f;

    private readonly ConditionalWeakTable<object, SmoothingState> _objectStates = new();

    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private long _lastTicks;

    protected IEnumerable<SmoothingState> States()
    {
        return _objectStates.Select(state => state.Value);
    }

    protected void SmoothObject(SmoothingState state)
    {
        _objectStates.Add(state.Object, state);
    }

    public void UpdatePositions()
    {
        _lastTicks = _timer.ElapsedTicks;

        foreach (var state in States())
        {
            state.PositionHistory[1] = state.PositionHistory[0];
            state.PositionHistory[0] = state.Position;

            if (!state.IsVisible)
                state.WasInvisible = true;
        }
    }

    public void CalculateSmoothedPositions()
    {
        var elapsedTicks = _timer.ElapsedTicks - _lastTicks;
        var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
        var player = Engine.Scene?.Tracker.GetEntity<Player>();

        foreach (var state in States())
        {
            state.SmoothedPosition = state.Position;

            // If the position is moving to zero, just snap to the current position
            if (state.PositionHistory[0] == Vector2.Zero)
                continue;

            // If the entity was invisible but is now visible, snap to the current position
            if (state.WasInvisible && state.IsVisible)
            {
                state.WasInvisible = false;
                continue;
            }

            // If the distance is too large, just snap to the current position
            if (Vector2.DistanceSquared(state.PositionHistory[0], state.PositionHistory[1]) >
                MaxLerpDistance * MaxLerpDistance)
                continue;

            if (state is EntitySmoothingState entity && player != null &&
                (entity.Entity == player || entity.Entity == player.Holding?.Entity))
            {
                switch (MotionSmoothingModule.Settings.PlayerSmoothing)
                {
                    case MotionSmoothingSettings.PlayerSmoothingMode.Interpolate:
                        state.SmoothedPosition = Interpolate(state.PositionHistory, elapsedSeconds);
                        continue;
                    case MotionSmoothingSettings.PlayerSmoothingMode.Extrapolate:
                        // Disable during screen transitions or pause
                        if (Engine.Scene is Level { Transitioning: true } or { Paused: true } || Engine.FreezeTimer > 0)
                            continue;

                        state.SmoothedPosition = Extrapolate(state.PositionHistory, player.Speed, elapsedSeconds);
                        continue;
                    case MotionSmoothingSettings.PlayerSmoothingMode.None:
                    default:
                        continue;
                }
            }

            if (state is CameraSmoothingState && MotionSmoothingModule.Settings.PlayerSmoothing ==
                MotionSmoothingSettings.PlayerSmoothingMode.Extrapolate)
            {
                state.SmoothedPosition = Extrapolate(state.PositionHistory, elapsedSeconds);
                continue;
            }

            state.SmoothedPosition = Interpolate(state.PositionHistory, elapsedSeconds);
        }
    }

    private Vector2 Interpolate(Vector2[] positionHistory, double elapsedSeconds)
    {
        var t = (float)(elapsedSeconds / Engine.Instance.TargetElapsedTime.TotalSeconds);
        return new Vector2
        {
            X = MathHelper.Lerp(positionHistory[1].X, positionHistory[0].X, t),
            Y = MathHelper.Lerp(positionHistory[1].Y, positionHistory[0].Y, t)
        };
    }

    private Vector2 Extrapolate(Vector2[] positionHistory, double elapsedSeconds)
    {
        var speed = (positionHistory[0] - positionHistory[1]) /
                    (float)Engine.Instance.TargetElapsedTime.TotalSeconds;
        return positionHistory[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds;
    }

    private Vector2 Extrapolate(Vector2[] positionHistory, Vector2 speed, double elapsedSeconds)
    {
        return positionHistory[0] + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsedSeconds;
    }

    protected Vector2 GetOffset(object obj)
    {
        if (obj == null) return Vector2.Zero;

        if (_objectStates.TryGetValue(obj, out var state))
            return state!.SmoothedPosition - state.OriginalPosition;

        return Vector2.Zero;
    }
}