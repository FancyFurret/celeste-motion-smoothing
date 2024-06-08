using System;
using System.Threading;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.FrameUncap;

public class DecoupledGameTick : ToggleableFeature<DecoupledGameTick>, IFrameUncapStrategy
{
    public TimeSpan TargetUpdateElapsedTime { get; set; }
    public TimeSpan TargetDrawElapsedTime { get; set; }

    private readonly Game _game = Engine.Instance;

    private TimeSpan _accumulatedElapsedTime;
    private TimeSpan _accumulatedUpdateElapsedTime;
    private TimeSpan _accumulatedDrawElapsedTime;
    private long _previousTicks;

    protected override void Hook()
    {
        base.Hook();
        AddHook(new Hook(typeof(Game).GetMethod(nameof(Game.Tick))!, GameTickHook));
    }

    public void SetTargetFramerate(int updateFramerate, int drawFramerate)
    {
        TargetDrawElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / drawFramerate));
        TargetUpdateElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / updateFramerate));
        _previousTicks = _game.gameTimer?.Elapsed.Ticks ?? 0;
    }

    private void Tick()
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

            // Update
            _game.gameTime.ElapsedGameTime = TargetUpdateElapsedTime;
            var updates = 0;
            while (_accumulatedUpdateElapsedTime >= TargetUpdateElapsedTime)
            {
                _game.gameTime.TotalGameTime += TargetUpdateElapsedTime;
                _accumulatedUpdateElapsedTime -= TargetUpdateElapsedTime;
                ++updates;
                _game.AssertNotDisposed();
                _game.Update(_game.gameTime);
            }

            // Handle lag
            _game.updateFrameLag += Math.Max(0, updates - 1);
            if (_game.gameTime.IsRunningSlowly)
            {
                if (_game.updateFrameLag == 0)
                    _game.gameTime.IsRunningSlowly = false;
            }
            else if (_game.updateFrameLag >= 5)
                _game.gameTime.IsRunningSlowly = true;

            if (updates == 1 && _game.updateFrameLag > 0)
                --_game.updateFrameLag;

            _game.gameTime.ElapsedGameTime = TimeSpan.FromTicks(TargetUpdateElapsedTime.Ticks * updates);
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
                // Engine.FPS is calculated in Draw, and ends up being 120+, so this fixes that
                _game.gameTime.ElapsedGameTime = TargetUpdateElapsedTime;

                // Ensure DeltaTime is accurate for drawing
                Engine.RawDeltaTime = (float)_accumulatedDrawElapsedTime.TotalSeconds;
                Engine.DeltaTime = GameUtils.CalculateDeltaTime(Engine.RawDeltaTime);

                if (!_game.BeginDraw())
                    return;
                _game.Draw(_game.gameTime);
                _game.EndDraw();

                _accumulatedDrawElapsedTime = TimeSpan.Zero;
            }
        }
    }

    private TimeSpan AdvanceElapsedTime()
    {
        var ticks = _game.gameTimer.Elapsed.Ticks;
        var timeSpan = TimeSpan.FromTicks(ticks - _previousTicks);
        _accumulatedElapsedTime += timeSpan;
        _accumulatedUpdateElapsedTime += timeSpan;
        _accumulatedDrawElapsedTime += timeSpan;
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