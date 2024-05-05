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
        var t = (float)(elapsedSeconds / Engine.Instance.TargetElapsedTime.TotalSeconds);

        foreach (var state in States())
        {
            // If the position is moving to zero, just snap to the current position
            if (state.PositionHistory[0] == Vector2.Zero)
            {
                state.SmoothedPosition = state.Position;
                continue;
            }

            // If the entity was invisible but is now visible, snap to the current position
            if (state.WasInvisible && state.IsVisible)
            {
                state.SmoothedPosition = state.Position;
                state.WasInvisible = false;
                continue;
            }

            // If the distance is too large, just snap to the current position
            if (Vector2.DistanceSquared(state.PositionHistory[0], state.PositionHistory[1]) >
                MaxLerpDistance * MaxLerpDistance)
            {
                state.SmoothedPosition = state.Position;
                continue;
            }

            // Interpolate
            var interpolatedPosition = new Vector2
            {
                X = MathHelper.Lerp(state.PositionHistory[1].X, state.PositionHistory[0].X, t),
                Y = MathHelper.Lerp(state.PositionHistory[1].Y, state.PositionHistory[0].Y, t)
            };

            if (state is EntitySmoothingState { Entity: Player player })
            {
                switch (MotionSmoothingModule.Settings.PlayerSmoothing)
                {
                    case MotionSmoothingSettings.PlayerSmoothingMode.Interpolate:
                        state.SmoothedPosition = interpolatedPosition;
                        continue;
                    case MotionSmoothingSettings.PlayerSmoothingMode.Extrapolate:
                        // Disable during screen transitions or pause
                        if (Engine.Scene is Level { Transitioning: true } or { Paused: true })
                        {
                            state.SmoothedPosition = state.Position;
                            continue;
                        }

                        state.SmoothedPosition = state.PositionHistory[0] + player.Speed * (float)elapsedSeconds;
                        continue;
                    case MotionSmoothingSettings.PlayerSmoothingMode.None:
                    default:
                        state.SmoothedPosition = state.Position;
                        continue;
                }
            }

            state.SmoothedPosition = interpolatedPosition;
        }
    }

    protected Vector2 GetOffset(object obj)
    {
        if (obj == null) return Vector2.Zero;
        
        if (_objectStates.TryGetValue(obj, out var state))
            return state!.SmoothedPosition - state.OriginalPosition;

        return Vector2.Zero;
    }
}