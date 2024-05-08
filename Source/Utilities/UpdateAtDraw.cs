using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class UpdateAtDraw
{
    private static UpdateAtDraw Instance { get; set; }

    private HashSet<Type> RendererTypesToUpdate { get; } = new()
    {
        typeof(HiresSnow),
        typeof(ParticleRenderer),
        typeof(MountainRenderer),
    };

    private HashSet<Type> EntityTypesToUpdate { get; } = new()
    {
        typeof(ParticleSystem),
        typeof(Snow3D),
    };

    private readonly List<Renderer> _renderersToUpdate = new();
    private readonly List<Entity> _entitiesToUpdate = new();
    private readonly List<Backdrop> _backdropsToUpdate = new();
    
    private readonly List<Hook> _hooks = new();

    private bool _recording;

    public UpdateAtDraw()
    {
        Instance = this;
    }

    public void Hook()
    {
        foreach (var rendererType in RendererTypesToUpdate)
            _hooks.Add(new Hook(rendererType.GetMethod("Update", MotionSmoothingModule.AllFlags)!, RendererUpdateHook));
        foreach (var entityType in EntityTypesToUpdate)
            _hooks.Add(new Hook(entityType.GetMethod("Update", MotionSmoothingModule.AllFlags)!, EntityUpdateHook));

        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Monocle.Engine.Draw += EngineDrawHook;
    }

    public void Unhook()
    {
        foreach (var hook in _hooks)
            hook.Dispose();

        _hooks.Clear();

        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Monocle.Engine.Draw -= EngineDrawHook;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        Instance._renderersToUpdate.Clear();
        Instance._entitiesToUpdate.Clear();
        Instance._backdropsToUpdate.Clear();
        
        Instance._recording = true;
        orig(self, gameTime);
        Instance._recording = false;
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        Instance.Update(Engine.Instance.scene);
        orig(self, gameTime);
    }

    private static void RendererUpdateHook(On.Monocle.Renderer.orig_Update orig, Renderer self, Scene scene)
    {
        if (Instance._recording)
            Instance._renderersToUpdate.Add(self);
        else
            orig(self, scene);
    }

    private static void EntityUpdateHook(On.Monocle.Entity.orig_Update orig, Entity self)
    {
        if (Instance._recording)
            Instance._entitiesToUpdate.Add(self);
        else
            orig(self);
    }

    private static void BackdropUpdateHook(On.Celeste.Backdrop.orig_Update orig, Backdrop self, Scene scene)
    {
        if (Instance._recording)
            Instance._backdropsToUpdate.Add(self);
        else
            orig(self, scene);
    }

    private void Update(Scene scene)
    {
        foreach (var renderer in _renderersToUpdate)
            renderer.Update(scene);

        foreach (var entity in _entitiesToUpdate)
            entity.Update();

        foreach (var backdrop in _backdropsToUpdate)
            backdrop.Update(scene);
    }
}