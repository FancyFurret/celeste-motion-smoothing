using System;
using System.Linq;
using Celeste.Mod.Entities;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.FrameUncap;

public class UpdateEveryNTicks : ToggleableFeature<UpdateEveryNTicks>, IFrameUncapStrategy
{
    public TimeSpan TargetUpdateElapsedTime { get; set; }
    public TimeSpan TargetDrawElapsedTime { get; set; }

    private readonly Game _game = Engine.Instance;

    private int _drawsPerUpdate = 1;
    private int _drawsUntilUpdate;

    // FNA's Game.Tick keeps its own elapsed-time accumulator, and DecoupledGameTick's hook bypasses
    // Tick entirely -- so on the way back from Dynamic mode it sees the whole stretch we were away
    // as one lump (clamped to MaxElapsedTime) and burns it off as back-to-back catch-up updates,
    // each of which re-reads held menu input. Hand it a fresh start instead.
    public override void Enable()
    {
        var wasEnabled = Enabled;

        base.Enable();

        // After base.Enable, so that the cost of installing the hooks (the first time round) isn't
        // itself counted as elapsed time.
        if (wasEnabled) return;

        // Game.Run starts that clock, and we can be here before it does: Everest deserializes our
        // settings while the game is still booting, and those setters land on ApplySettings.
        // Nothing to reset that early -- no frame has been ticked yet.
        if (_game?.gameTimer == null) return;

        _game.previousTicks = _game.gameTimer.Elapsed.Ticks;
        _game.accumulatedElapsedTime = TimeSpan.Zero;
    }

    protected override void Hook()
    {
        // Make sure our hook runs first, so that when we block the original update, other mods' hooks won't run either.
        MainThreadHelper.Schedule(() =>
        {
            using (new DetourConfigContext(new DetourConfig(
                       "MotionSmoothingModule.DecoupledGameTick.EngineUpdateHook",
                       int.MaxValue
                   )).Use())
            {
                MotionSmoothingModule.DisableInlining(typeof(Engine), "Update");
                MotionSmoothingModule.DisableInlining(typeof(Engine), "Draw");
                MotionSmoothingModule.DisableInlining(typeof(Input), "UpdateGrab");

                On.Monocle.Engine.Update += EngineUpdateHook;
                On.Monocle.Engine.Draw += EngineDrawHook;

                On.Celeste.Input.UpdateGrab += Input_UpdateGrab;
            }
        });

        base.Hook();
    }

    protected override void Unhook()
    {
        MainThreadHelper.Schedule(() =>
        {
            On.Monocle.Engine.Update -= EngineUpdateHook;
            On.Monocle.Engine.Draw -= EngineDrawHook;

            On.Celeste.Input.UpdateGrab -= Input_UpdateGrab;
        });

        base.Unhook();
    }

    public void SetTargetFramerate(double updateFramerate, double drawFramerate)
    {
        updateFramerate = Math.Floor(updateFramerate);
        drawFramerate = Math.Floor(drawFramerate);

        if (drawFramerate % updateFramerate != 0)
        {
            Logger.Log(LogLevel.Warn, "MotionSmoothingModule",
                "Draw framerate must be a multiple of update framerate.");
            drawFramerate = (int)(Math.Ceiling(drawFramerate / updateFramerate) * updateFramerate);
        }

        TargetDrawElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / drawFramerate));
        TargetUpdateElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / updateFramerate));

        _drawsPerUpdate = (int)drawFramerate / (int)updateFramerate;
        _drawsUntilUpdate = _drawsPerUpdate;
        _game.TargetElapsedTime = TargetDrawElapsedTime;
    }

    // The hooks below stay installed while this strategy is switched off (see
    // ToggleableFeature.Deactivate), so each one has to hand back to the original when it isn't the
    // strategy in force.
    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        if (!Instance.Enabled)
        {
            orig(self, gameTime);
            return;
        }

        if (Instance._drawsUntilUpdate == 0)
        {
            orig(self,
                new GameTime(gameTime.TotalGameTime, Instance.TargetUpdateElapsedTime, gameTime.IsRunningSlowly));
            Instance._drawsUntilUpdate = Instance._drawsPerUpdate;
        }

        Instance._drawsUntilUpdate--;
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        if (!Instance.Enabled)
        {
            orig(self, gameTime);
            return;
        }

        Engine.RawDeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Engine.DeltaTime = GameUtils.CalculateDeltaTime(Engine.RawDeltaTime);

        // Engine.FPS is calculated in Draw, and ends up being 120+, so this fixes that
        orig(self, new GameTime(gameTime.TotalGameTime, Instance.TargetUpdateElapsedTime, gameTime.IsRunningSlowly));
    }

    public static void Input_UpdateGrab(On.Celeste.Input.orig_UpdateGrab orig)
    {
        if (!Instance.Enabled || Instance._drawsUntilUpdate == 0)
        {
            orig();
        }
    }
}