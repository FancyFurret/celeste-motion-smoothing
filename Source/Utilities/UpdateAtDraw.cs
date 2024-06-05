using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class UpdateAtDraw : ToggleableFeature<UpdateAtDraw>
{
    private HashSet<Type> RendererTypesToUpdate { get; } = new()
    {
        typeof(HiresSnow),
        typeof(ParticleRenderer),
        typeof(MountainRenderer),
        typeof(BackdropRenderer),
        typeof(DisplacementRenderer)
    };

    private HashSet<Type> EntityTypesToUpdate { get; } = new()
    {
        typeof(ParticleSystem),
        typeof(Snow3D),
    };

    private readonly List<Renderer> _renderersToUpdate = new();
    private readonly List<Entity> _entitiesToUpdate = new();

    private bool _recording;

    protected override void Hook()
    {
        base.Hook();
        foreach (var rendererType in RendererTypesToUpdate)
            AddHook(new Hook(rendererType.GetMethod("Update", MotionSmoothingModule.AllFlags)!, RendererUpdateHook));
        foreach (var entityType in EntityTypesToUpdate)
            AddHook(new Hook(entityType.GetMethod("Update", MotionSmoothingModule.AllFlags)!, EntityUpdateHook));

        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Monocle.Engine.Draw += EngineDrawHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Monocle.Engine.Draw -= EngineDrawHook;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        Instance._renderersToUpdate.Clear();
        Instance._entitiesToUpdate.Clear();

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

    private void Update(Scene scene)
    {
        foreach (var renderer in _renderersToUpdate)
        {
            if (renderer is BackdropRenderer && scene is not Level) continue;
            renderer.Update(scene);
        }

        foreach (var entity in _entitiesToUpdate)
            entity.Update();
    }
}