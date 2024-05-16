using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public class MotionSmoothingHandler
{
    private static MotionSmoothingHandler Instance { get; set; }

    public bool Enabled { get; set; }

    private readonly PositionSmoother _positionSmoother;
    private readonly PushSpriteSmoother _pushSpriteSmoother;

    private readonly List<Hook> _hooks = new();

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
        _hooks.Add(new Hook(typeof(SpriteBatch).GetMethod("PushSprite", MotionSmoothingModule.AllFlags)!,
            PushSpriteHook));

        // These are really slow. So for now we're just hooking Entity instead.
        // HookSubtypes.HookAllMethods(typeof(Entity), "Render", EntityRender));
        // HookSubtypes.HookAllMethods(typeof(GraphicsComponent), "Render", ComponentRender);

        // These catch renders that might happen outside a ComponentList
        HookComponentRender<Component>();
        HookComponentRender<Sprite>();
        HookComponentRender<Image>();
        HookComponentRender<DustGraphic>(); // Components (that aren't GraphicsComponents) can be smoothed by looking at their Entity's position

        On.Monocle.Engine.Update += EngineUpdateHook;
        On.Monocle.Engine.Draw += EngineDrawHook;

        IL.Monocle.ComponentList.Render += ComponentListRenderHook;
        IL.Monocle.EntityList.Render += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnly += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnlyFullMatch += EntityListRenderHook;
        IL.Monocle.EntityList.RenderExcept += EntityListRenderHook;
    }

    public void Unhook()
    {
        foreach (var hook in _hooks) hook.Dispose();
        _hooks.Clear();

        On.Monocle.Engine.Update -= EngineUpdateHook;
        On.Monocle.Engine.Draw -= EngineDrawHook;
        
        IL.Monocle.ComponentList.Render -= ComponentListRenderHook;
        IL.Monocle.EntityList.Render -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnly -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnlyFullMatch -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderExcept -= EntityListRenderHook;
    }

    private static void ComponentListRenderHook(ILContext il)
    {
        var c = new ILCursor(il);
        while (c.TryGotoNext(MoveType.Before, i => i.MatchCallvirt<Component>("Render")))
        {
            c.Emit(OpCodes.Ldloc_1);
            c.EmitDelegate(PreObjectRender);
            c.Index++;
            c.EmitDelegate(PostObjectRender);
        }
    }
    
    private static void EntityListRenderHook(ILContext il)
    {
        var c = new ILCursor(il);
        while (c.TryGotoNext(MoveType.Before, i => i.MatchCallvirt<Entity>("Render")))
        {
            c.Emit(OpCodes.Ldloc_1);
            c.EmitDelegate(PreObjectRender);
            c.Index++;
            c.EmitDelegate(PostObjectRender);
        }
    }
    
    private static void PreObjectRender(object obj)
    {
        Instance._pushSpriteSmoother.PreObjectRender(obj);
    }
    
    private static void PostObjectRender()
    {
        Instance._pushSpriteSmoother.PostObjectRender();
    }

    private void HookComponentRender<T>() where T : Component
    {
        _hooks.Add(new Hook(typeof(T).GetMethod("Render")!, ComponentRender));
    }

    private void HookEntityRender<T>() where T : Entity
    {
        _hooks.Add(new Hook(typeof(T).GetMethod("Render")!, EntityRender));
    }

    private static void EntityCtorHook(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self, Vector2 position)
    {
        orig(self, position);
        Instance._pushSpriteSmoother.SmoothEntity(self);
    }

    private static void ComponentCtorHook(On.Monocle.Component.orig_ctor orig, Component self, bool active,
        bool visible)
    {
        orig(self, active, visible);
        if (self is GraphicsComponent graphicsComponent)
            Instance._pushSpriteSmoother.SmoothComponent(graphicsComponent);
    }
    
    private static void CameraCtorHook(On.Monocle.Camera.orig_ctor orig, Camera self)
    {
        orig(self);
        Instance._positionSmoother.SmoothCamera(self);
    }

    private static void CameraCtorIntIntHook(On.Monocle.Camera.orig_ctor_int_int orig, Camera self, int width, int height)
    {
        orig(self, width, height);
        Instance._positionSmoother.SmoothCamera(self);
    }

    private static void ScreenWipeCtorHook(On.Celeste.ScreenWipe.orig_ctor orig, ScreenWipe self, Scene scene,
        bool wipeIn, Action onComplete = null)
    {
        orig(self, scene, wipeIn, onComplete);
        Instance._positionSmoother.SmoothScreenWipe(self);
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        orig(self, gameTime);
        Instance._positionSmoother.UpdatePositions();
        Instance._pushSpriteSmoother.UpdatePositions();
    }

    private static void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime)
    {
        Instance._positionSmoother.CalculateSmoothedPositions();
        Instance._pushSpriteSmoother.CalculateSmoothedPositions();

        Instance._positionSmoother.SetPositions();
        orig(self, gameTime);
        Instance._positionSmoother.ResetPositions();
    }

    private static void EntityRender(On.Monocle.Entity.orig_Render orig, Entity self)
    {
        Instance._pushSpriteSmoother.PreObjectRender(self);
        orig(self);
        Instance._pushSpriteSmoother.PostObjectRender();
    }

    private static void ComponentRender(On.Monocle.Component.orig_Render orig, Component self)
    {
        Instance._pushSpriteSmoother.PreObjectRender(self);
        orig(self);
        Instance._pushSpriteSmoother.PostObjectRender();
    }

    // ReSharper disable once InconsistentNaming
    private delegate void orig_PushSprite(SpriteBatch self, Texture2D texture, float sourceX, float sourceY,
        float sourceW, float sourceH, float destinationX, float destinationY, float destinationW, float destinationH,
        Color color, float originX, float originY, float rotationSin, float rotationCos, float depth, byte effects);

    private static void PushSpriteHook(orig_PushSprite orig, SpriteBatch self, Texture2D texture, float sourceX,
        float sourceY, float sourceW, float sourceH, float destinationX, float destinationY, float destinationW,
        float destinationH, Color color, float originX, float originY, float rotationSin, float rotationCos,
        float depth, byte effects)
    {
        var pos = new Vector2(destinationX, destinationY);
        if (Instance.Enabled)
            pos = Instance._pushSpriteSmoother.GetSpritePosition(pos);
        orig(self, texture, sourceX, sourceY, sourceW, sourceH, pos.X, pos.Y, destinationW, destinationH, color,
            originX, originY, rotationSin, rotationCos, depth, effects);
    }
}