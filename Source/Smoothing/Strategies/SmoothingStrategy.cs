using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public abstract class SmoothingStrategy<T> where T : SmoothingStrategy<T>
{
    public bool Enabled { get; set; } = true;

    protected static T Instance { get; private set; }
    protected List<Hook> Hooks { get; } = new();

    private readonly ConditionalWeakTable<object, ISmoothingState> _objectStates = new();
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private long _lastTicks;

    protected SmoothingStrategy()
    {
        Instance = (T)this;
    }

    public virtual void Hook()
    {
        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Monocle.Engine.Draw += EngineDrawHook;
    }

    public virtual void Unhook()
    {
        foreach (var hook in Hooks)
            hook.Dispose();
        Hooks.Clear();

        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Monocle.Engine.Draw -= EngineDrawHook;
    }

    protected IEnumerable<KeyValuePair<object, ISmoothingState>> States()
    {
        return _objectStates;
    }

    public void ClearStates()
    {
        _objectStates.Clear();
    }

    public void SmoothObject(object obj)
    {
        if (_objectStates.TryGetValue(obj, out _))
            return;

        ISmoothingState state = obj switch
        {
            ZipMover.ZipMoverPathRenderer => new ZipMoverSmoothingState(),
            FinalBossBeam => new FinalBossBeamSmoothingState(),
            DustGraphic.Eyeballs => new EyeballsSmoothingState(),

            Camera => new CameraSmoothingState(),
            ScreenWipe => new ScreenWipeSmoothingState(),

            // These should be last so that more specific types are handled first
            Entity => new EntitySmoothingState(),
            GraphicsComponent => new ComponentSmoothingState(),
            _ => throw new System.NotSupportedException($"Unsupported object type: {obj.GetType()}")
        };

        _objectStates.Add(obj, state);
    }

    public void StopSmoothingObject(object obj)
    {
        _objectStates.Remove(obj);
    }

    private void UpdatePositions()
    {
        _lastTicks = _timer.ElapsedTicks;

        foreach (var (obj, state) in States())
        {
            state.PositionHistory[1] = state.PositionHistory[0];
            state.PositionHistory[0] = state.GetPosition(obj);
            state.OriginalPosition = state.PositionHistory[0];

            if (!state.GetVisible(obj))
                state.WasInvisible = true;
        }
    }

    private void CalculateSmoothedPositions()
    {
        var elapsedTicks = _timer.ElapsedTicks - _lastTicks;
        var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
        var player = Engine.Scene?.Tracker.GetEntity<Player>();

        foreach (var (obj, state) in States())
            state.SmoothedPosition = ObjectSmoother.CalculateSmoothedPosition(state, obj, player, elapsedSeconds);
    }

    protected ISmoothingState GetState(object obj)
    {
        return _objectStates.TryGetValue(obj, out var state) ? state : null;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        orig(self, gameTime);
        if (Instance.Enabled)
            Instance.UpdatePositions();
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        if (Instance.Enabled)
            Instance.PreRender();
        orig(self, gameTime);
        if (Instance.Enabled)
            Instance.PostRender();
    }

    protected virtual void PreRender()
    {
        Instance.CalculateSmoothedPositions();
    }

    protected virtual void PostRender()
    {
    }
}