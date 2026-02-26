using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class ActorPushTracker : ToggleableFeature<ActorPushTracker>
{
    private readonly ConditionalWeakTable<Actor, HashSet<Entity>> _pushers = new();
    private bool _isPlayerRidingSolid;
    private bool _isPlayerRidingSteerableMoveBlock;
    private bool _isPlayerRidingJumpThru;

    public bool IsPlayerRidingSolid => _isPlayerRidingSolid;
    public bool IsPlayerRidingSteerableMoveBlock => _isPlayerRidingSteerableMoveBlock;
    public bool IsPlayerRidingJumpThru => _isPlayerRidingJumpThru;

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

    public bool ApplyPusherOffset(Actor actor, double elapsedSeconds, SmoothingMode mode, out Vector2 pushed,
        out Vector2 pusherVelocity)
    {
        pushed = Vector2.Zero;
        pusherVelocity = Vector2.Zero;

        var state = MotionSmoothingHandler.Instance.GetState(actor);
        if (state is not IPositionSmoothingState posState)
            return false;

        if (!GetPusherOffset(actor, elapsedSeconds, out var offset, out pusherVelocity))
            return false;

        pushed = posState.GetLastDrawPosition(mode) + offset;
        return true;
    }

    public bool GetPusherOffset(Actor actor, double elapsedSeconds, out Vector2 offset)
    {
        return GetPusherOffset(actor, elapsedSeconds, out offset, out _);
    }

    public bool GetPusherOffset(Actor actor, double elapsedSeconds, out Vector2 offset, out Vector2 pusherVelocity)
    {
        var pushed = false;
        offset = Vector2.Zero;
        pusherVelocity = Vector2.Zero;

        if (!_pushers.TryGetValue(actor, out var pushers) || pushers == null)
            return false;

        foreach (var pusher in pushers)
        {
            var state = MotionSmoothingHandler.Instance.GetState(pusher);
            if (state is not { Changed: true })
                continue;

            pushed = true;
            offset += GetSolidOffset(state, pusher, elapsedSeconds, out var velocity);
            pusherVelocity += velocity;
        }

        return pushed;
    }

    public Vector2 GetSolidOffset(ISmoothingState state, object obj, double elapsedSeconds)
    {
        return GetSolidOffset(state, obj, elapsedSeconds, out _);
    }

    public Vector2 GetSolidOffset(ISmoothingState state, object obj, double elapsedSeconds, out Vector2 velocity)
    {
        var mode = MotionSmoothingModule.Settings.SmoothingMode;
        var interp = mode == SmoothingMode.Interpolate;
        velocity = Vector2.Zero;

        if (state is IPositionSmoothingState posState)
        {
            posState.Smooth(obj, elapsedSeconds, mode);
            // Calculate velocity from position history
            velocity = (posState.RealPositionHistory[0] - posState.RealPositionHistory[1]) / SmoothingMath.SecondsPerUpdate;
            return posState.GetSmoothedOffset(mode);
        }

        if (state is ZipMoverPercentSmoothingState zipMoverState)
        {
            var smoothedPercent = SmoothingMath.Smooth(zipMoverState.History, elapsedSeconds, mode);
            var originalPercent = interp ? zipMoverState.History[1] : zipMoverState.History[0];
            var smoothed = zipMoverState.GetPositionAtPercent((ZipMover)obj, smoothedPercent);
            var original = zipMoverState.GetPositionAtPercent((ZipMover)obj, originalPercent);
            // Calculate velocity from position at current and previous percent values
            var posAtCurrent = zipMoverState.GetPositionAtPercent((ZipMover)obj, zipMoverState.History[0]);
            var posAtPrev = zipMoverState.GetPositionAtPercent((ZipMover)obj, zipMoverState.History[1]);
            velocity = (posAtCurrent - posAtPrev) / SmoothingMath.SecondsPerUpdate;
            return smoothed - original;
        }

        return Vector2.Zero;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        foreach (var kv in Instance._pushers)
            kv.Value.Clear();

        Instance._isPlayerRidingSolid = false;
        Instance._isPlayerRidingSteerableMoveBlock = false;
        Instance._isPlayerRidingJumpThru = false;

        var actors = self.scene.Tracker.GetEntities<Actor>();

        if (self.scene is Level)
        {
            foreach (Solid solid in self.scene.Tracker.GetEntities<Solid>())
            {
                foreach (Actor actor in actors)
                {
                    if (actor.IsRiding(solid))
                    {
                        Instance._pushers.GetOrCreateValue(actor)!.Add(solid);
                        if (actor is Player)
                        {
                            Instance._isPlayerRidingSolid = true;

                            if (solid is MoveBlock { canSteer: true })
                            {
                                Instance._isPlayerRidingSteerableMoveBlock = true;
                            }
                        }
                    }
                }
            }

            foreach (JumpThru jumpThru in self.scene.Tracker.GetEntities<JumpThru>())
            {
                foreach (Actor actor in actors)
                {
                    if (actor.IsRiding(jumpThru))
                    {
                        Instance._pushers.GetOrCreateValue(actor)!.Add(jumpThru);
                        if (actor is Player)
                            Instance._isPlayerRidingJumpThru = true;
                    }
                }
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