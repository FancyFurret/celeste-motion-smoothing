using System;
using System.Diagnostics;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Smoothing.Strategies;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public class MotionSmoothingHandler : ToggleableFeature<MotionSmoothingHandler>
{
    public Player Player => _playerReference?.TryGetTarget(out var player) == true ? player : null;
    private WeakReference<Player> _playerReference;

    public AtDrawInputHandler AtDrawInputHandler { get; } = new();

    public bool WasPaused => _pauseCounter > 0;
    private int _pauseCounter;

    public ValueSmoother ValueSmoother { get; } = new();
    public PushSpriteSmoother PushSpriteSmoother { get; } = new();

    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private long _lastTicks;
    private bool _positionsWereUpdated;

    public override void Load()
    {
        base.Load();
        SpeedrunToolImports.RegisterSaveLoadAction?.Invoke(null, (_, _) => SmoothAllObjects(), null,
            null, null, null);
    }

    public override void Enable()
    {
        base.Enable();
        ValueSmoother.Enable();
        PushSpriteSmoother.Enable();

        SmoothAllObjects();
    }

    public override void Disable()
    {
        base.Disable();
        ValueSmoother.Disable();
        PushSpriteSmoother.Disable();

        ValueSmoother.ClearStates();
        PushSpriteSmoother.ClearStates();
    }

    protected override void Hook()
    {
        base.Hook();

        On.Monocle.Scene.AfterUpdate += SceneAfterUpdateHook;
        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Monocle.Engine.Draw += EngineDrawHook;

        On.Monocle.Tracker.EntityAdded += TrackerEntityAddedHook;
        On.Monocle.Tracker.EntityRemoved += TrackerEntityRemovedHook;
        On.Monocle.Tracker.ComponentAdded += TrackerComponentAddedHook;
        On.Monocle.Tracker.ComponentRemoved += TrackerComponentRemovedHook;

        On.Celeste.Level.ctor += LevelCtorHook;
        On.Monocle.Camera.ctor += CameraCtorHook;
        On.Monocle.Camera.ctor_int_int += CameraCtorIntIntHook;
        On.Celeste.ScreenWipe.ctor += ScreenWipeCtorHook;
    }

    protected override void Unhook()
    {
        base.Unhook();

        On.Monocle.Scene.AfterUpdate -= SceneAfterUpdateHook;
        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Monocle.Engine.Draw -= EngineDrawHook;

        On.Monocle.Tracker.EntityAdded -= TrackerEntityAddedHook;
        On.Monocle.Tracker.EntityRemoved -= TrackerEntityRemovedHook;
        On.Monocle.Tracker.ComponentAdded -= TrackerComponentAddedHook;
        On.Monocle.Tracker.ComponentRemoved -= TrackerComponentRemovedHook;

        On.Celeste.Level.ctor -= LevelCtorHook;
        On.Monocle.Camera.ctor -= CameraCtorHook;
        On.Monocle.Camera.ctor_int_int -= CameraCtorIntIntHook;
        On.Celeste.ScreenWipe.ctor -= ScreenWipeCtorHook;
    }

    public ISmoothingState GetState(object obj)
    {
        return ValueSmoother.GetState(obj) ?? PushSpriteSmoother.GetState(obj);
    }

    private static void SceneAfterUpdateHook(On.Monocle.Scene.orig_AfterUpdate orig, Scene self)
    {
        orig(self);

        // Updating positions in Scene.AfterUpdate (instead of Engine.Update) ensures that we only update the positions
        // if the game is not frozen and if the scene *actually* had a chance to update its positions
        if (Instance.Enabled)
        {
            Instance.ValueSmoother.UpdatePositions();
            Instance.PushSpriteSmoother.UpdatePositions();
            Instance._positionsWereUpdated = true;
            Instance._lastTicks = Instance._timer.ElapsedTicks;
            if (Instance._pauseCounter > 0)
                Instance._pauseCounter--;
        }
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        Instance._positionsWereUpdated = false;
        orig(self, gameTime);
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        if (Instance.Enabled)
        {
            var mode = MotionSmoothingModule.Settings.SmoothingMode;
            var elapsedTicks = Instance._timer.ElapsedTicks - Instance._lastTicks;
            var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;

            // To keep physics consistent, input is still only updated at 60FPS, but we want to check if there is input
            // during smoothing. So temporarily update the input to the current frame.
            Instance.AtDrawInputHandler.UpdateInput();

            if (Instance.AtDrawInputHandler.PressedThisUpdate(Input.Pause))
                Instance._pauseCounter = 5;

            if (Instance._positionsWereUpdated)
            {
                Instance.ValueSmoother.CalculateSmoothedPositions(elapsedSeconds, mode);
                Instance.PushSpriteSmoother.CalculateSmoothedPositions(elapsedSeconds, mode);
            }

            Instance.ValueSmoother.PreRender();
            Instance.PushSpriteSmoother.PreRender();

            // Reset the input back so that physics is still consistent
            Instance.AtDrawInputHandler.ResetInput();
        }

        orig(self, gameTime);

        if (Instance.Enabled)
        {
            Instance.ValueSmoother.PostRender();
            Instance.PushSpriteSmoother.PostRender();
        }
    }

    private static void TrackerEntityAddedHook(On.Monocle.Tracker.orig_EntityAdded orig, Tracker self, Entity entity)
    {
        orig(self, entity);
        Instance.SmoothEntity(entity);
    }

    private static void TrackerEntityRemovedHook(On.Monocle.Tracker.orig_EntityRemoved orig, Tracker self,
        Entity entity)
    {
        orig(self, entity);
        Instance.StopSmoothingObject(entity);
    }

    private static void TrackerComponentAddedHook(On.Monocle.Tracker.orig_ComponentAdded orig, Tracker self,
        Component component)
    {
        orig(self, component);
        if (component is GraphicsComponent graphicsComponent)
            Instance.SmoothComponent(graphicsComponent);
    }

    private static void TrackerComponentRemovedHook(On.Monocle.Tracker.orig_ComponentRemoved orig, Tracker self,
        Component component)
    {
        orig(self, component);
        Instance.StopSmoothingObject(component);
    }

    private static void LevelCtorHook(On.Celeste.Level.orig_ctor orig, Level self)
    {
        orig(self);
        Instance.SmoothLevel(self);
    }

    private static void CameraCtorHook(On.Monocle.Camera.orig_ctor orig, Camera self)
    {
        orig(self);
        Instance.SmoothCamera(self);
    }

    private static void CameraCtorIntIntHook(On.Monocle.Camera.orig_ctor_int_int orig, Camera self, int width,
        int height)
    {
        orig(self, width, height);
        Instance.SmoothCamera(self);
    }

    private static void ScreenWipeCtorHook(On.Celeste.ScreenWipe.orig_ctor orig, ScreenWipe self, Scene scene,
        bool wipeIn, Action onComplete = null)
    {
        orig(self, scene, wipeIn, onComplete);
        Instance.SmoothScreenWipe(self);
    }

    private void SmoothAllObjects()
    {
        if (Engine.Scene is not Level level) return;

        ValueSmoother.ClearStates();
        PushSpriteSmoother.ClearStates();

        foreach (var entity in level.Entities)
        {
            SmoothEntity(entity);
            foreach (var component in entity.Components)
                if (component is GraphicsComponent graphicsComponent)
                    SmoothComponent(graphicsComponent);
        }

        SmoothLevel(level);
        SmoothCamera(level.Camera);
    }

    private void SmoothScreenWipe(ScreenWipe screenWipe)
    {
        ValueSmoother.SmoothObject(screenWipe, new ScreenWipeSmoothingState());
    }

    private void SmoothCamera(Camera camera)
    {
        ValueSmoother.SmoothObject(camera, new CameraSmoothingState());
    }

    private void SmoothLevel(Level level)
    {
        _valueSmoother.SmoothObject(level, new LevelZoomSmoothingState());
    }

    private void SmoothEntity(Entity entity)
    {
        if (entity is Player player)
            Instance._playerReference = new WeakReference<Player>(player);

        var state = GetPositionSmootherState(entity);
        if (state != null)
            ValueSmoother.SmoothObject(entity, state);

        var pushSpriteState = GetPushSpriteSmootherState(entity);
        if (pushSpriteState != null)
            PushSpriteSmoother.SmoothObject(entity, pushSpriteState);
    }

    private void SmoothComponent(GraphicsComponent component)
    {
        // This used to smooth *all* components, but that was mostly unnecessary and lowered FPS quite a bit
        // This really only needs to be done for components that move separately from their entity

        // FinalBossBeam components should *not* be smoothed
        // This check is currently not necessary but leaving here for future reference
        // if (component.Entity is FinalBossBeam)
        //     return;

        if (component.Entity is Booster)
            PushSpriteSmoother.SmoothObject(component, GetPushSpriteSmootherState(component));
    }

    private void StopSmoothingObject(object obj)
    {
        ValueSmoother.StopSmoothingObject(obj);
        PushSpriteSmoother.StopSmoothingObject(obj);
    }

    private ISmoothingState GetPositionSmootherState(object obj)
    {
        return obj switch
        {
            ZipMover => new ZipMoverPercentSmoothingState(),
            FinalBossBeam => new FinalBossBeamSmoothingState(),
            _ => null
        };
    }

    private IPositionSmoothingState GetPushSpriteSmootherState(object obj)
    {
        return obj switch
        {
            DustGraphic.Eyeballs => new EyeballsSmoothingState(),

            // Specifically *don't* want to push sprite these
            ZipMover => null,
            ZipMover.ZipMoverPathRenderer => null,
            Trigger => null,

            // These should be last so that more specific types are handled first
            Platform => new PlatformSmoothingState(),
            Actor => new ActorSmoothingState(),
            Entity => new EntitySmoothingState(),
            GraphicsComponent => new ComponentSmoothingState(),
            _ => null
        };
    }
}