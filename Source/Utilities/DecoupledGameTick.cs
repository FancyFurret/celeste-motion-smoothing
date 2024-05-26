using System;
using System.Linq;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class DecoupledGameTick
{
    public static DecoupledGameTick Instance { get; private set; }
    public TimeSpan TargetUpdateElapsedTime { get; private set; } = TimeSpan.FromSeconds(1 / 60.0);

    private readonly Game _game = Engine.Instance;

    private int _drawsPerUpdate = 1;
    private int _drawsUntilUpdate;

    public DecoupledGameTick()
    {
        Instance = this;
    }

    public void Hook()
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
            }
        });
    }

    public void Unhook()
    {
        MainThreadHelper.Schedule(() => { On.Monocle.Engine.Update -= EngineUpdateHook; });
    }

    public void SetTargetFramerate(int updateFramerate, int drawFramerate)
    {
        if (drawFramerate % updateFramerate != 0)
            throw new ArgumentException("Update framerate must be divisible by draw framerate");

        _drawsPerUpdate = drawFramerate / updateFramerate;
        _drawsUntilUpdate = _drawsPerUpdate;
        _game.TargetElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / drawFramerate));
        TargetUpdateElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / updateFramerate));
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        if (Instance._drawsUntilUpdate == 0)
        {
            orig(self, new GameTime(gameTime.TotalGameTime, Instance.TargetUpdateElapsedTime, gameTime.IsRunningSlowly));
            Instance._drawsUntilUpdate = Instance._drawsPerUpdate;
        }

        Engine.RawDeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Engine.DeltaTime = Engine.RawDeltaTime * Engine.TimeRate * Engine.TimeRateB *
                           GetTimeRateComponentMultiplier(Engine.Instance.scene);

        Instance._drawsUntilUpdate--;
    }

    private static float GetTimeRateComponentMultiplier(Scene scene)
    {
        return scene == null
            ? 1f
            : scene.Tracker.GetComponents<TimeRateModifier>().Cast<TimeRateModifier>()
                .Where((Func<TimeRateModifier, bool>)(trm => trm.Enabled))
                .Select((Func<TimeRateModifier, float>)(trm => trm.Multiplier))
                .Aggregate(1f, (Func<float, float, float>)((acc, val) => acc * val));
    }
}