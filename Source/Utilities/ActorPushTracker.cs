using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class ActorPushTracker : ToggleableFeature<ActorPushTracker>
{
    private readonly ConditionalWeakTable<Actor, HashSet<Solid>> _pushers = new();

    protected override void Hook()
    {
        base.Hook();
        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Celeste.Actor.MoveHExact += ActorMoveHExactHook;
        On.Celeste.Actor.MoveVExact += ActorMoveVExactHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Celeste.Actor.MoveHExact -= ActorMoveHExactHook;
        On.Celeste.Actor.MoveVExact -= ActorMoveVExactHook;
    }

    public bool ApplyPusherOffset(Actor actor, double elapsedSeconds, SmoothingMode mode, out Vector2 pushed)
    {
        return ApplyPusherOffset(actor, elapsedSeconds, mode, out pushed, out _);
    }

    /// <summary>
    /// Applies the pusher offset to the actor's position and returns the pusher's position history.
    /// </summary>
    /// <param name="pusherPositionHistory">
    /// A 2-element array containing [currentPosition, previousPosition] of the pusher.
    /// Used to compute relative velocity between actor and pusher without numerical instability.
    /// </param>
    public bool ApplyPusherOffset(Actor actor, double elapsedSeconds, SmoothingMode mode, out Vector2 pushed,
        out Vector2[] pusherPositionHistory)
    {
        pushed = Vector2.Zero;
        pusherPositionHistory = null;

        var state = MotionSmoothingHandler.Instance.GetState(actor);
        if (state is not IPositionSmoothingState posState)
            return false;

        if (!GetPusherOffset(actor, elapsedSeconds, out var offset, out pusherPositionHistory))
            return false;

        pushed = posState.GetLastDrawPosition(mode) + offset;
        return true;
    }

    public bool GetPusherOffset(Actor actor, double elapsedSeconds, out Vector2 offset)
    {
        return GetPusherOffset(actor, elapsedSeconds, out offset, out _);
    }

    public bool GetPusherOffset(Actor actor, double elapsedSeconds, out Vector2 offset, out Vector2[] pusherPositionHistory)
    {
        var pushed = false;
        offset = Vector2.Zero;
        pusherPositionHistory = null;

        if (!_pushers.TryGetValue(actor, out var pushers) || pushers == null)
            return false;

        // Track combined position history from all pushers
        Vector2 combinedPosCurrent = Vector2.Zero;
        Vector2 combinedPosPrev = Vector2.Zero;

        foreach (var pusher in pushers)
        {
            var state = MotionSmoothingHandler.Instance.GetState(pusher);
            if (state is not { Changed: true })
                continue;

            pushed = true;
            offset += GetSolidOffset(state, pusher, elapsedSeconds, out var posHistory);
            if (posHistory != null)
            {
                combinedPosCurrent += posHistory[0];
                combinedPosPrev += posHistory[1];
            }
        }

        if (pushed)
            pusherPositionHistory = new[] { combinedPosCurrent, combinedPosPrev };

        return pushed;
    }

    public Vector2 GetSolidOffset(ISmoothingState state, object obj, double elapsedSeconds)
    {
        return GetSolidOffset(state, obj, elapsedSeconds, out _);
    }

    /// <summary>
    /// Gets the smoothed offset for a solid and returns its position history.
    /// </summary>
    /// <param name="positionHistory">
    /// A 2-element array containing [currentPosition, previousPosition] of the solid.
    /// </param>
    public Vector2 GetSolidOffset(ISmoothingState state, object obj, double elapsedSeconds, out Vector2[] positionHistory)
    {
        var mode = MotionSmoothingModule.Settings.SmoothingMode;
        var interp = mode == SmoothingMode.Interpolate;
        positionHistory = null;

        if (state is IPositionSmoothingState posState)
        {
            posState.Smooth(obj, elapsedSeconds, mode);
            positionHistory = new[] { posState.RealPositionHistory[0], posState.RealPositionHistory[1] };
            return posState.GetSmoothedOffset(mode);
        }

        if (state is ZipMoverPercentSmoothingState zipMoverState)
        {
            var smoothedPercent = SmoothingMath.Smooth(zipMoverState.History, elapsedSeconds, mode);
            var originalPercent = interp ? zipMoverState.History[1] : zipMoverState.History[0];
            var smoothed = zipMoverState.GetPositionAtPercent((ZipMover)obj, smoothedPercent);
            var original = zipMoverState.GetPositionAtPercent((ZipMover)obj, originalPercent);
            // For ZipMovers, compute position from percent values
            var posAtCurrent = zipMoverState.GetPositionAtPercent((ZipMover)obj, zipMoverState.History[0]);
            var posAtPrev = zipMoverState.GetPositionAtPercent((ZipMover)obj, zipMoverState.History[1]);
            positionHistory = new[] { posAtCurrent, posAtPrev };
            return smoothed - original;
        }

        return Vector2.Zero;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        foreach (var kv in Instance._pushers)
            kv.Value.Clear();

        if (self.scene is Level)
        {
            foreach (var entity in self.scene.Tracker.GetEntities<Solid>())
            {
                var solid = (entity as Solid)!;
                var state = MotionSmoothingHandler.Instance.GetState(solid);
                if (state is not { Changed: true })
                    continue;

                solid.GetRiders();
                foreach (var rider in Solid.riders)
                    Instance._pushers.GetOrCreateValue(rider)!.Add(solid);
                Solid.riders.Clear();
            }
        }

        orig(self, gameTime);
    }

    private static bool ActorMoveHExactHook(On.Celeste.Actor.orig_MoveHExact orig, Actor self, int moveH,
        Collision onCollide, Solid pusher)
    {
        Instance._pushers.GetOrCreateValue(self)!.Add(pusher);
        return orig(self, moveH, onCollide, pusher);
    }

    private static bool ActorMoveVExactHook(On.Celeste.Actor.orig_MoveVExact orig, Actor self, int moveV,
        Collision onCollide, Solid pusher)
    {
        Instance._pushers.GetOrCreateValue(self)!.Add(pusher);
        return orig(self, moveV, onCollide, pusher);
    }
}