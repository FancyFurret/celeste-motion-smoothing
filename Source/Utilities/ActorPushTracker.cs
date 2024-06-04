using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public static class ActorPushTracker
{
    private static readonly ConditionalWeakTable<Actor, HashSet<Solid>> Pushers = new();

    public static void Hook()
    {
        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Celeste.Actor.MoveHExact += ActorMoveHExactHook;
        On.Celeste.Actor.MoveVExact += ActorMoveVExactHook;
    }

    public static void Unhook()
    {
        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Celeste.Actor.MoveHExact -= ActorMoveHExactHook;
        On.Celeste.Actor.MoveVExact -= ActorMoveVExactHook;
    }

    public static bool ApplyPusherOffset(Actor actor, double elapsedSeconds, SmoothingMode mode, out Vector2 pushed)
    {
        pushed = Vector2.Zero;

        var state = MotionSmoothingHandler.Instance.GetState(actor);
        if (state is not IPositionSmoothingState posState)
            return false;

        if (!GetPusherOffset(actor, elapsedSeconds, out var offset))
            return false;

        pushed = posState.GetLastDrawPosition(mode) + offset;
        return true;
    }

    public static bool GetPusherOffset(Actor actor, double elapsedSeconds, out Vector2 offset)
    {
        var pushed = false;
        offset = Vector2.Zero;

        if (!Pushers.TryGetValue(actor, out var pushers) || pushers == null)
            return false;

        foreach (var pusher in pushers)
        {
            var state = MotionSmoothingHandler.Instance.GetState(pusher);
            if (state is not { Changed: true })
                continue;

            pushed = true;
            offset += GetSolidOffset(state, pusher, elapsedSeconds);
        }

        return pushed;
    }

    public static Vector2 GetSolidOffset(ISmoothingState state, object obj, double elapsedSeconds)
    {
        var mode = MotionSmoothingModule.Settings.Smoothing;
        var interp = mode == SmoothingMode.Interpolate;

        var smoothed = Vector2.Zero;
        var original = Vector2.Zero;

        if (state is IPositionSmoothingState posState)
        {
            posState.Smooth(obj, elapsedSeconds, mode);
            return posState.GetSmoothedOffset(mode);
        }

        if (state is ZipMoverPercentSmoothingState zipMoverState)
        {
            smoothed = zipMoverState.GetPositionAtPercent((ZipMover)obj,
                SmoothingMath.Smooth(zipMoverState.History, elapsedSeconds, mode));
            original = zipMoverState.GetPositionAtPercent((ZipMover)obj,
                interp ? zipMoverState.History[1] : zipMoverState.History[0]);
        }

        return smoothed - original;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        foreach (var kv in Pushers)
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
                    Pushers.GetOrCreateValue(rider)!.Add(solid);
                Solid.riders.Clear();
            }
        }

        orig(self, gameTime);
    }

    private static bool ActorMoveHExactHook(On.Celeste.Actor.orig_MoveHExact orig, Actor self, int moveH,
        Collision onCollide, Solid pusher)
    {
        Pushers.GetOrCreateValue(self)!.Add(pusher);
        return orig(self, moveH, onCollide, pusher);
    }

    private static bool ActorMoveVExactHook(On.Celeste.Actor.orig_MoveVExact orig, Actor self, int moveV,
        Collision onCollide, Solid pusher)
    {
        Pushers.GetOrCreateValue(self)!.Add(pusher);
        return orig(self, moveV, onCollide, pusher);
    }
}