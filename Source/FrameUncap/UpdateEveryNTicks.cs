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

    protected override void Hook()
    {
        // Make sure our hook runs first, so that when we block the original update, other mods hooks won't run either.
        MainThreadHelper.Schedule(() =>
        {
            using (new DetourConfigContext(new DetourConfig(
                       "MotionSmoothingModule.DecoupledGameTick.EngineUpdateHook",
                       int.MaxValue
                   )).Use())
            {
                On.Monocle.Engine.Update += EngineUpdateHook;
                On.Monocle.Engine.Draw += EngineDrawHook;
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
        });

        base.Unhook();
    }

    public void SetTargetFramerate(int updateFramerate, int drawFramerate)
    {
        if (drawFramerate % updateFramerate != 0)
        {
            Logger.Log(LogLevel.Warn, "MotionSmoothingModule",
                "Draw framerate must be a multiple of update framerate.");
            drawFramerate = (int)(Math.Ceiling(drawFramerate / (float)updateFramerate) * updateFramerate);
        }

        TargetDrawElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / drawFramerate));
        TargetUpdateElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / updateFramerate));

        _drawsPerUpdate = drawFramerate / updateFramerate;
        _drawsUntilUpdate = _drawsPerUpdate;
        _game.TargetElapsedTime = TargetDrawElapsedTime;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
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
        Engine.RawDeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Engine.DeltaTime = GameUtils.CalculateDeltaTime(Engine.RawDeltaTime);

        // Engine.FPS is calculated in Draw, and ends up being 120+, so this fixes that
        orig(self, new GameTime(gameTime.TotalGameTime, Instance.TargetUpdateElapsedTime, gameTime.IsRunningSlowly));
    }
}