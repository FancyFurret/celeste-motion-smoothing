using System;
using Celeste.Mod.MotionSmoothing.Interop;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public class MotionSmoothingHandler
{
    private static MotionSmoothingHandler Instance { get; set; }

    public bool Enabled
    {
        set
        {
            _positionSmoother.Enabled = value;
            _pushSpriteSmoother.Enabled = value;
        }
    }

    private readonly PositionSmoother _positionSmoother;
    private readonly PushSpriteSmoother _pushSpriteSmoother;

    public MotionSmoothingHandler()
    {
        Instance = this;

        _positionSmoother = new PositionSmoother();
        _pushSpriteSmoother = new PushSpriteSmoother();
    }

    public void Load()
    {
        On.Monocle.Entity.ctor_Vector2 += EntityCtorHook;
        On.Monocle.Component.ctor += ComponentCtorHook;
        On.Monocle.Camera.ctor += CameraCtorHook;
        On.Monocle.Camera.ctor_int_int += CameraCtorIntIntHook;
        On.Celeste.ScreenWipe.ctor += ScreenWipeCtorHook;

        SpeedrunToolImports.RegisterSaveLoadAction?.Invoke(null, (_, _) => SmoothAllObjects(), null,
            null, null, null);
    }

    public void Unload()
    {
        On.Monocle.Entity.ctor_Vector2 -= EntityCtorHook;
        On.Monocle.Component.ctor -= ComponentCtorHook;
        On.Monocle.Camera.ctor -= CameraCtorHook;
        On.Monocle.Camera.ctor_int_int -= CameraCtorIntIntHook;
        On.Celeste.ScreenWipe.ctor -= ScreenWipeCtorHook;
    }

    public void Hook()
    {
        _positionSmoother.Hook();
        _pushSpriteSmoother.Hook();
    }

    public void Unhook()
    {
        _positionSmoother.Unhook();
        _pushSpriteSmoother.Unhook();
    }

    private static void EntityCtorHook(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self, Vector2 position)
    {
        orig(self, position);
        Instance.SmoothEntity(self);
    }

    private static void ComponentCtorHook(On.Monocle.Component.orig_ctor orig, Component self, bool active,
        bool visible)
    {
        orig(self, active, visible);
        if (self is GraphicsComponent graphicsComponent)
            Instance.SmoothComponent(graphicsComponent);
    }
    
    private static void CameraCtorHook(On.Monocle.Camera.orig_ctor orig, Camera self)
    {
        orig(self);
        Instance.SmoothCamera(self);
    }

    private static void CameraCtorIntIntHook(On.Monocle.Camera.orig_ctor_int_int orig, Camera self, int width, int height)
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
        _positionSmoother.SmoothObject(screenWipe);
    }

    private void SmoothCamera(Camera camera)
    {
        _positionSmoother.SmoothObject(camera);
    }

    private void SmoothEntity(Entity entity)
    {
        _pushSpriteSmoother.SmoothObject(entity);
    }

    private void SmoothComponent(GraphicsComponent component)
    {
        _pushSpriteSmoother.SmoothObject(component);
    }
}