﻿using System;
using System.Diagnostics;
using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Smoothing.Strategies;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public class MotionSmoothingHandler
{
    public static MotionSmoothingHandler Instance { get; private set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _valueSmoother.Enabled = value;
            _pushSpriteSmoother.Enabled = value;
        }
    }
    private bool _enabled = true;

    public Player Player => _playerReference?.TryGetTarget(out var player) == true ? player : null;
    private WeakReference<Player> _playerReference;

    private readonly ValueSmoother _valueSmoother;
    private readonly PushSpriteSmoother _pushSpriteSmoother;

    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private long _lastTicks;

    public MotionSmoothingHandler()
    {
        Instance = this;

        _valueSmoother = new ValueSmoother();
        _pushSpriteSmoother = new PushSpriteSmoother();
    }

    public void Load()
    {
        On.Monocle.Tracker.EntityAdded += TrackerEntityAddedHook;
        On.Monocle.Tracker.EntityRemoved += TrackerEntityRemovedHook;
        On.Monocle.Tracker.ComponentAdded += TrackerComponentAddedHook;
        On.Monocle.Tracker.ComponentRemoved += TrackerComponentRemovedHook;

        On.Monocle.Camera.ctor += CameraCtorHook;
        On.Monocle.Camera.ctor_int_int += CameraCtorIntIntHook;
        On.Celeste.ScreenWipe.ctor += ScreenWipeCtorHook;

        SpeedrunToolImports.RegisterSaveLoadAction?.Invoke(null, (_, _) => SmoothAllObjects(), null,
            null, null, null);
    }

    public void Unload()
    {
        On.Monocle.Tracker.EntityAdded -= TrackerEntityAddedHook;
        On.Monocle.Tracker.EntityRemoved -= TrackerEntityRemovedHook;
        On.Monocle.Tracker.ComponentAdded -= TrackerComponentAddedHook;
        On.Monocle.Tracker.ComponentRemoved -= TrackerComponentRemovedHook;
        
        On.Monocle.Camera.ctor -= CameraCtorHook;
        On.Monocle.Camera.ctor_int_int -= CameraCtorIntIntHook;
        On.Celeste.ScreenWipe.ctor -= ScreenWipeCtorHook;
    }

    public void Hook()
    {
        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Monocle.Engine.Draw += EngineDrawHook;
        _valueSmoother.Hook();
        _pushSpriteSmoother.Hook();
    }

    public void Unhook()
    {
        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Monocle.Engine.Draw -= EngineDrawHook;
        _valueSmoother.Unhook();
        _pushSpriteSmoother.Unhook();
    }

    public ISmoothingState GetState(object obj)
    {
        return _valueSmoother.GetState(obj) ?? _pushSpriteSmoother.GetState(obj);
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        orig(self, gameTime);

        if (Instance.Enabled)
        {
            Instance._lastTicks = Instance._timer.ElapsedTicks;
            Instance._valueSmoother.UpdatePositions();
            Instance._pushSpriteSmoother.UpdatePositions();
        }
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        if (Instance.Enabled)
        {
            var mode = MotionSmoothingModule.Settings.Smoothing;
            var elapsedTicks = Instance._timer.ElapsedTicks - Instance._lastTicks;
            var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;

            Instance._valueSmoother.CalculateSmoothedPositions(elapsedSeconds, mode);
            Instance._pushSpriteSmoother.CalculateSmoothedPositions(elapsedSeconds, mode);
            Instance._valueSmoother.PreRender();
            Instance._pushSpriteSmoother.PreRender();
        }

        orig(self, gameTime);

        if (Instance.Enabled)
        {
            Instance._valueSmoother.PostRender();
            Instance._pushSpriteSmoother.PostRender();
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

        // This will miss components that are created by entities but not added to their component lists, but
        // it should be good enough when loading a SpeedrunTool state

        _valueSmoother.ClearStates();
        _pushSpriteSmoother.ClearStates();

        foreach (var entity in level.Entities)
        {
            SmoothEntity(entity);
            foreach (var component in entity.Components)
                if (component is GraphicsComponent graphicsComponent)
                    SmoothComponent(graphicsComponent);
        }

        SmoothCamera(level.Camera);
    }

    private void SmoothScreenWipe(ScreenWipe screenWipe)
    {
        _valueSmoother.SmoothObject(screenWipe, GetPositionSmootherState(screenWipe));
    }

    private void SmoothCamera(Camera camera)
    {
        _valueSmoother.SmoothObject(camera, GetPositionSmootherState(camera));
    }

    private void SmoothEntity(Entity entity)
    {
        if (entity is Player player)
            Instance._playerReference = new WeakReference<Player>(player);

        var state = GetPositionSmootherState(entity);
        if (state != null)
            _valueSmoother.SmoothObject(entity, state);

        var pushSpriteState = GetPushSpriteSmootherState(entity);
        if (pushSpriteState != null)
            _pushSpriteSmoother.SmoothObject(entity, pushSpriteState);
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
            _pushSpriteSmoother.SmoothObject(component, GetPushSpriteSmootherState(component));
    }

    private void StopSmoothingObject(object obj)
    {
        _valueSmoother.StopSmoothingObject(obj);
        _pushSpriteSmoother.StopSmoothingObject(obj);
    }

    private ISmoothingState GetPositionSmootherState(object obj)
    {
        return obj switch
        {
            ZipMover => new ZipMoverPercentSmoothingState(),
            FinalBossBeam => new FinalBossBeamSmoothingState(),
            Camera => new CameraSmoothingState(),
            ScreenWipe => new ScreenWipeSmoothingState(),
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

            // These should be last so that more specific types are handled first
            Platform => new PlatformSmoothingState(),
            Actor => new ActorSmoothingState(),
            Entity => new EntitySmoothingState(),
            GraphicsComponent => new ComponentSmoothingState(),
            _ => null
        };
    }
}