using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// Moves these updates from Update to Draw, so that they run once per drawn frame and stay smooth
// above 60fps. At 60fps and below there is no frame in between two updates for them to fill, and
// running them at draw time instead only changes what each frame shows -- a particle emitted during
// an update is drawn one step along rather than where it started -- so MotionSmoothingModule
// deactivates it there and they run in Update, as in vanilla. Deactivated rather than disabled:
// that happens on a menu keypress, and re-detouring Engine.Update is felt as a lag spike.
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
            AddHook(rendererType.GetMethod("Update", MotionSmoothingModule.AllFlags)!, RendererUpdateHook);
        foreach (var entityType in EntityTypesToUpdate)
            AddHook(entityType.GetMethod("Update", MotionSmoothingModule.AllFlags)!, EntityUpdateHook);

        MotionSmoothingModule.DisableInlining(typeof(Engine), "Update");
        MotionSmoothingModule.DisableInlining(typeof(Engine), "Draw");
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

        Instance._recording = Instance.Enabled;
        orig(self, gameTime);
        Instance._recording = false;
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        Instance.Update(Engine.Instance.scene);

        // Deactivated during the update just run (from the settings menu, say): what it held back
        // has had its update now, and the lists would otherwise run it again on every frame drawn
        // until the next update clears them.
        if (!Instance.Enabled)
        {
            Instance._renderersToUpdate.Clear();
            Instance._entitiesToUpdate.Clear();
        }

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