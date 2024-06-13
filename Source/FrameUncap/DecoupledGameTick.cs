using System;
using System.Collections.Generic;
using System.Threading;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.FrameUncap;

public class DecoupledGameTick : ToggleableFeature<DecoupledGameTick>, IFrameUncapStrategy
{
    // This is how fast Update should be called
    public TimeSpan TargetUpdateElapsedTime { get; set; } = GameUtils.UpdateElapsedTime;

    // This is how fast Draw should be called
    public TimeSpan TargetDrawElapsedTime { get; set; } = GameUtils.UpdateElapsedTime;

    // This is what will be passed to Update, that gets used to calculate DeltaTime
    public TimeSpan TargetUpdateDeltaTime { get; set; } = GameUtils.UpdateElapsedTime;

    private readonly Game _game = Engine.Instance;

    private TimeSpan _accumulatedElapsedTime;
    private TimeSpan _accumulatedUpdateElapsedTime;
    private TimeSpan _accumulatedDrawElapsedTime;
    private long _previousTicks;

    protected override void Hook()
    {
        base.Hook();

        AddHook(new Hook(typeof(Game).GetMethod(nameof(Game.Tick))!, GameTickHook));

        MainThreadHelper.Schedule(() =>
        {
            using (new DetourConfigContext(new DetourConfig(
                       "MotionSmoothingModule.DecoupledGameTick.LevelUpdateHook",
                       after: new List<string> { "SpeedMod" }
                   )).Use())
            {
                IL.Celeste.Level.UpdateTime += LevelUpdateTimeHook;
            }
        });
    }

    protected override void Unhook()
    {
        base.Unhook();

        MainThreadHelper.Schedule(() => { IL.Celeste.Level.UpdateTime -= LevelUpdateTimeHook; });
    }

    public void SetTargetFramerate(double updateFramerate, double drawFramerate)
    {
        TargetDrawElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / drawFramerate));
        TargetUpdateElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / updateFramerate));
        TargetUpdateDeltaTime = TargetUpdateElapsedTime;
        _previousTicks = _game.gameTimer?.Elapsed.Ticks ?? 0;
    }

    public void SetTargetDeltaTime(double deltaTime)
    {
        TargetUpdateDeltaTime = new TimeSpan((long)Math.Round(10_000_000.0 / deltaTime));
    }

    private static void LevelUpdateTimeHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCall<Engine>("get_RawDeltaTime")))
        {
            static double GetDeltaTime(float oldDt)
            {
                if (MotionSmoothingModule.Settings.GameSpeedModified)
                    return (float)Instance.TargetUpdateDeltaTime.TotalSeconds * 60f /
                           MotionSmoothingModule.Settings.GameSpeed;
                return oldDt;
            }

            cursor.EmitDelegate(GetDeltaTime);
        }
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
            // Make sure to use the TargetUpdateDeltaTime for this, since this is how DeltaTime is calculated
            _game.gameTime.ElapsedGameTime = TargetUpdateDeltaTime;
            var updates = 0;
            while (_accumulatedUpdateElapsedTime >= TargetUpdateElapsedTime)
            {
                _game.gameTime.TotalGameTime += TargetUpdateDeltaTime;
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

            _game.gameTime.ElapsedGameTime = TimeSpan.FromTicks(TargetUpdateDeltaTime.Ticks * updates);
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
                _game.gameTime.ElapsedGameTime = TargetUpdateDeltaTime;

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