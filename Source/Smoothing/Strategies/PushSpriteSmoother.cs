using System.Collections.Generic;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public class PushSpriteSmoother : SmoothingStrategy<PushSpriteSmoother>
{
    private readonly Stack<object> _currentObjects = new();

    public void SmoothObject(object obj, IPositionSmoothingState state)
    {
        base.SmoothObject(obj, state);
    }

    public override void Hook()
    {
        base.Hook();

        Hooks.Add(new Hook(typeof(SpriteBatch).GetMethod("PushSprite", MotionSmoothingModule.AllFlags)!,
            PushSpriteHook));

        // These catch renders that might happen outside a ComponentList
        HookComponentRender<Component>();
        HookComponentRender<Sprite>();
        HookComponentRender<Image>();
        HookComponentRender<DustGraphic>(); // Components (that aren't GraphicsComponents) can be smoothed by looking at their Entity's position

        IL.Monocle.ComponentList.Render += ComponentListRenderHook;
        IL.Monocle.EntityList.Render += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnly += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnlyFullMatch += EntityListRenderHook;
        IL.Monocle.EntityList.RenderExcept += EntityListRenderHook;
    }

    public override void Unhook()
    {
        base.Unhook();

        IL.Monocle.ComponentList.Render -= ComponentListRenderHook;
        IL.Monocle.EntityList.Render -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnly -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnlyFullMatch -= EntityListRenderHook;
        IL.Monocle.EntityList.RenderExcept -= EntityListRenderHook;
    }

    private static void PreObjectRender(object obj)
    {
        Instance._currentObjects.Push(obj);
    }

    private static void PostObjectRender()
    {
        Instance._currentObjects.Pop();
    }

    private Vector2 GetSpritePosition(Vector2 position)
    {
        if (_currentObjects.Count == 0) return position;

        var obj = _currentObjects.Peek();
        position += obj switch
        {
            GraphicsComponent graphicsComponent => GetOffset(graphicsComponent) + GetOffset(graphicsComponent.Entity),
            Component component => GetOffset(component.Entity),
            _ => GetOffset(obj)
        };

        return position;
    }

    private Vector2 GetOffset(object obj)
    {
        if (GetState(obj) is not IPositionSmoothingState state)
            return Vector2.Zero;

        var targetPos = state.SmoothedRealPosition;
        return targetPos - state.OriginalDrawPosition;
    }

    private void HookComponentRender<T>() where T : Component
    {
        Hooks.Add(new Hook(typeof(T).GetMethod("Render")!, ComponentRenderHook));
    }

    private void HookEntityRender<T>() where T : Entity
    {
        Hooks.Add(new Hook(typeof(T).GetMethod("Render")!, EntityRenderHook));
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

    private static void EntityRenderHook(On.Monocle.Entity.orig_Render orig, Entity self)
    {
        PreObjectRender(self);
        orig(self);
        PostObjectRender();
    }

    private static void ComponentRenderHook(On.Monocle.Component.orig_Render orig, Component self)
    {
        PreObjectRender(self);
        orig(self);
        PostObjectRender();
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
            pos = Instance.GetSpritePosition(pos);
        orig(self, texture, sourceX, sourceY, sourceW, sourceH, pos.X, pos.Y, destinationW, destinationH, color,
            originX, originY, rotationSin, rotationCos, depth, effects);
    }
}