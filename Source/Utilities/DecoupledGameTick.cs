using System;
using System.Linq;
using System.Threading;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class DecoupledGameTick
{
    private static DecoupledGameTick Instance { get; set; }

    private TimeSpan TargetUpdateElapsedTime => _game.TargetElapsedTime;
    private TimeSpan TargetDrawElapsedTime { get; set; } = TimeSpan.FromSeconds(1 / 120.0);

    private readonly Game _game = Engine.Instance;
    private GameTime GameTime => _game.gameTime;

    private long _previousTicks;

    private int _drawsPerUpdate = 1;
    private int _drawsUntilUpdate;
    
    private int _suppressForFrames;

    private TimeSpan _accumulatedElapsedTime = TimeSpan.Zero;

    private Hook _tickHook;

    public DecoupledGameTick()
    {
        Instance = this;
    }

    public void Hook()
    {
        _tickHook = new Hook(typeof(Game).GetMethod("Tick", MotionSmoothingModule.AllFlags)!, GameTickHook);
    }

    public void Unhook()
    {
        _tickHook?.Dispose();
    }

    public void SetTargetFramerate(int updateFramerate, int drawFramerate)
    {
        if (drawFramerate % updateFramerate != 0)
            throw new ArgumentException("Update framerate must be divisible by draw framerate");

        _drawsPerUpdate = drawFramerate / updateFramerate;
        _drawsUntilUpdate = _drawsPerUpdate;
        TargetDrawElapsedTime = TimeSpan.FromSeconds(1.0 / drawFramerate);
        _game.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / updateFramerate);
    }

    private void Tick()
    {
        AdvanceElapsedTime();

        while (_accumulatedElapsedTime + _game.worstCaseSleepPrecision < TargetDrawElapsedTime)
        {
            Thread.Sleep(1);
            _game.UpdateEstimatedSleepPrecision(AdvanceElapsedTime());
        }

        while (_accumulatedElapsedTime < TargetDrawElapsedTime)
        {
            Thread.SpinWait(1);
            AdvanceElapsedTime();
        }

        // Poll events
        FNAPlatform.PollEvents(_game, ref _game.currentAdapter, _game.textInputControlDown,
            ref _game.textInputSuppress);

        // Cap the accumulated time
        if (_accumulatedElapsedTime > Game.MaxElapsedTime)
            _accumulatedElapsedTime = Game.MaxElapsedTime;

        // Set elapsed time to the target UPDATE time, since that is what Update expects
        GameTime.ElapsedGameTime = TargetUpdateElapsedTime;
        
        // Lower accumulated time
        var updates = 0;
        while (_accumulatedElapsedTime >= TargetDrawElapsedTime)
        {
            GameTime.TotalGameTime += TargetDrawElapsedTime;
            _accumulatedElapsedTime -= TargetDrawElapsedTime;
            ++updates;

            // Only update if ready
            if (_drawsUntilUpdate == 0)
            {
                _game.AssertNotDisposed();
                _game.Update(GameTime);
                _drawsUntilUpdate = _drawsPerUpdate;
            }

            --_drawsUntilUpdate;
        }

        // Check for lag
        _game.updateFrameLag += Math.Max(0, updates - 1);
        if (GameTime.IsRunningSlowly)
        {
            if (_game.updateFrameLag == 0)
                GameTime.IsRunningSlowly = false;
        }
        else if (_game.updateFrameLag >= 5)
            GameTime.IsRunningSlowly = true;

        if (updates == 1 && _game.updateFrameLag > 0)
            --_game.updateFrameLag;

        GameTime.ElapsedGameTime = TimeSpan.FromTicks(TargetDrawElapsedTime.Ticks * updates);

        // Draw
        if (_game.suppressDraw)
        {
            _suppressForFrames = _drawsPerUpdate;
            _game.suppressDraw = false;
            
        }
        else if (_suppressForFrames > 0)
        {
            _suppressForFrames--;
        }
        else
        {
            var oldDt = Engine.DeltaTime;
            var oldRawDt = Engine.RawDeltaTime;

            // For the draw phase, update the delta time
            Engine.RawDeltaTime = (float)GameTime.ElapsedGameTime.TotalSeconds;
            Engine.DeltaTime = Engine.RawDeltaTime * Engine.TimeRate * Engine.TimeRateB *
                               GetTimeRateComponentMultiplier(Engine.Instance.scene);

            if (!_game.BeginDraw())
                return;
            _game.Draw(GameTime);
            _game.EndDraw();

            Engine.DeltaTime = oldDt;
            Engine.RawDeltaTime = oldRawDt;
        }
    }

    /*
    // Old, this version could tick at any draw interval. The downside to that is that draws and updates could desync
    // a bit, which would mean a draw may not happen *immediately* after an update. This adds slightly higher latency
    // and slight inconsistency in smoothing.
    private void TickAnyDrawInterval()
    {
        AdvanceElapsedTime();

        // Figure out how much time we need to wait until the next draw and/or update
        var updateTimeLeft = TargetUpdateElapsedTime - _accumulatedUpdateElapsedTime;
        var drawTimeLeft = TargetDrawElapsedTime - _accumulatedDrawElapsedTime;
        var targetElapsedTime = TimeSpan.FromTicks(Math.Min(updateTimeLeft.Ticks, drawTimeLeft.Ticks));

        // Wait for that amount of time
        while (_accumulatedElapsedTime + _game.worstCaseSleepPrecision < targetElapsedTime)
        {
            Thread.Sleep(1);
            _game.UpdateEstimatedSleepPrecision(AdvanceElapsedTime());
        }

        while (_accumulatedElapsedTime < targetElapsedTime)
        {
            Thread.SpinWait(1);
            AdvanceElapsedTime();
        }

        // Cap the accumulated time
        if (_accumulatedElapsedTime >= Game.MaxElapsedTime)
            _accumulatedElapsedTime = Game.MaxElapsedTime;

        // Update if ready
        if (_accumulatedUpdateElapsedTime >= TargetUpdateElapsedTime)
        {
            // Poll events
            FNAPlatform.PollEvents(_game, ref _game.currentAdapter, _game.textInputControlDown,
                ref _game.textInputSuppress);

            GameTime.ElapsedGameTime = TargetUpdateElapsedTime;
            var updates = 0;
            while (_accumulatedUpdateElapsedTime >= TargetUpdateElapsedTime)
            {
                GameTime.TotalGameTime += TargetUpdateElapsedTime;
                _accumulatedUpdateElapsedTime -= TargetUpdateElapsedTime;
                ++updates;
                _game.AssertNotDisposed();
                _game.Update(GameTime);
            }

            _game.updateFrameLag += Math.Max(0, updates - 1);
            if (GameTime.IsRunningSlowly)
            {
                if (_game.updateFrameLag == 0)
                    GameTime.IsRunningSlowly = false;
            }
            else if (_game.updateFrameLag >= 5)
                GameTime.IsRunningSlowly = true;

            if (updates == 1 && _game.updateFrameLag > 0)
                --_game.updateFrameLag;

            GameTime.ElapsedGameTime = TimeSpan.FromTicks(TargetUpdateElapsedTime.Ticks * updates);
        }

        // Draw if ready
        if (_accumulatedDrawElapsedTime >= TargetDrawElapsedTime)
        {
            // Drawing doesn't need to be as accurate as updating, so we can just draw whenever we're ready
            if (_game.suppressDraw)
            {
                _game.suppressDraw = false;
            }
            else
            {
                GameTime.ElapsedGameTime = _accumulatedDrawElapsedTime;

                var oldDt = Engine.DeltaTime;
                var oldRawDt = Engine.RawDeltaTime;

                Engine.RawDeltaTime = (float)GameTime.ElapsedGameTime.TotalSeconds;
                Engine.DeltaTime = Engine.RawDeltaTime * Engine.TimeRate * Engine.TimeRateB *
                                   GetTimeRateComponentMultiplier(Engine.Instance.scene);

                if (!_game.BeginDraw())
                    return;
                _game.Draw(GameTime);
                _game.EndDraw();

                Engine.DeltaTime = oldDt;
                Engine.RawDeltaTime = oldRawDt;

                _accumulatedDrawElapsedTime = TimeSpan.Zero;
            }
        }
    }
    */

    private static float GetTimeRateComponentMultiplier(Scene scene)
    {
        return scene == null
            ? 1f
            : scene.Tracker.GetComponents<TimeRateModifier>().Cast<TimeRateModifier>()
                .Where((Func<TimeRateModifier, bool>)(trm => trm.Enabled))
                .Select((Func<TimeRateModifier, float>)(trm => trm.Multiplier))
                .Aggregate(1f, (Func<float, float, float>)((acc, val) => acc * val));
    }

    private TimeSpan AdvanceElapsedTime()
    {
        var ticks = _game.gameTimer.Elapsed.Ticks;
        var timeSpan = TimeSpan.FromTicks(ticks - _previousTicks);
        _accumulatedElapsedTime += timeSpan;
        _previousTicks = ticks;
        return timeSpan;
    }

    // ReSharper disable once InconsistentNaming
    private delegate void orig_Tick(Game self);

#pragma warning disable CL0003
    private static void GameTickHook(orig_Tick orig, Game self)
    {
        Instance.Tick();
    }
#pragma warning restore CL0003
}