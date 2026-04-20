using System.Collections.Generic;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public class PushSpriteSmoother : SmoothingStrategy<PushSpriteSmoother>
{
	public static bool TemporarilyDisablePushSpriteSmoothing = false;

    private readonly Stack<object> _currentObjects = new();

    public void SmoothObject(object obj, IPositionSmoothingState state)
    {
        base.SmoothObject(obj, state);
    }

    protected override void Hook()
    {
        base.Hook();

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("PushSprite", MotionSmoothingModule.AllFlags)!,
            PushSpriteHook));

        // These catch renders that might happen outside a ComponentList
        HookComponentRender<Component>();
        HookComponentRender<Sprite>();
        HookComponentRender<Image>();
        HookComponentRender<DustGraphic>(); // Components (that aren't GraphicsComponents) can be smoothed by looking at their Entity's position
        HookComponentRender<PlayerHair>();

        // MoveBlock.Border uses Draw.Rect with Parent's position, which bypasses PushSprite;
        // temporarily swap Parent.Position to its smoothed value during Border.Render
        AddHook(new Hook(typeof(MoveBlock.Border).GetMethod("Render")!, BorderRenderHook));

        IL.Monocle.ComponentList.Render += ComponentListRenderHook;
        IL.Monocle.EntityList.Render += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnly += EntityListRenderHook;
        IL.Monocle.EntityList.RenderOnlyFullMatch += EntityListRenderHook;
        IL.Monocle.EntityList.RenderExcept += EntityListRenderHook;
    }

    protected override void Unhook()
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
        if (DebugRenderFix.IsDebugRendering) return position;
        if (_currentObjects.Count == 0) return position;

        var obj = _currentObjects.Peek();
        position += obj switch
        {
            GraphicsComponent graphicsComponent => GetOffset(graphicsComponent) + GetOffset(graphicsComponent.Entity),
            PlayerHair hair => GetHairOffset(hair),
            Component component => GetOffset(component.Entity),
            _ => GetOffset(obj)
        };

        return position;
    }

    private Vector2 GetHairOffset(PlayerHair hair)
    {
        var playerState = (hair.Entity is Player
            ? MotionSmoothingHandler.Instance.PlayerState
            : GetState(hair.Entity)) as IPositionSmoothingState;
        if (playerState == null) return Vector2.Zero;

        // PlayerHair.Nodes[0] is computed in AfterUpdate from Sprite.RenderPosition (which reads
        // Sprite.Entity.Position — always integer for an Actor, since subpixels live in
        // ExactPosition via movementCounter) and floored again in Render. So the destination
        // passed to PushSprite is integer-valued. Subtracting the *unrounded* real position
        // (ExactPosition) would produce a fractional offset, and adding that to an integer node
        // lands the destination on a half-integer — banker's-rounding parity flips it ±1 px
        // (visible as hair jitter while jumping or floating in water, and worsened during the
        // post-landing squash animation where Sprite.Scale.Y adds more fractional terms).
        //
        // Using OriginalDrawPosition (= Position.Round() captured at update time — integer for
        // an Actor) keeps the offset integer, matching the integer stride that ValueSmoother
        // applies to Player.Position via SmoothedRealPosition.Round(). The non-player-hair
        // branch is unaffected (ice FireBall et al. go through GetOffset, not GetHairOffset).
        var targetPos = playerState.SmoothedRealPosition.Round();
        return targetPos - playerState.OriginalDrawPosition;
    }

    private Vector2 GetOffset(object obj)
    {
        if (GetState(obj) is not IPositionSmoothingState state)
            return Vector2.Zero;

		if (MotionSmoothingModule.Settings.SillyMode)
		{
			return state.SmoothedRealPosition - state.SmoothedRealPosition.Round();
		}

        var targetPos = state.SmoothedRealPosition.Round();

        // For Actors, Position is always integer (subpixels live in ExactPosition via
        // movementCounter), so the destination PushSprite receives is integer-anchored.
        // Subtracting the *unrounded* ExactPosition (OriginalRealPosition) here would produce
        // a fractional offset and land the destination on a half-integer — banker's-rounding
        // parity then flips it ±1 px on rasterization. This is the same root cause as the
        // PlayerHair jitter fix above, and is what causes a thrown Glider to vertically
        // jitter ~2 px during its fall (movementCounter cycles 0/0.5/0/0.5 at the steady
        // ~30 px/s gravity-clamped speed, putting the offset right at the half-integer
        // boundary every other tick).
        //
        // For non-Actor Entities (e.g. FireBall in ice mode), render position itself is
        // subpixel — Position is set fractionally and passed straight to PushSprite — so
        // OriginalRealPosition is the right anchor and the integer-rounding form would
        // strip the subpixel motion.
        var anchor = obj is Actor ? state.OriginalDrawPosition : state.OriginalRealPosition;
        return targetPos - anchor;
    }

    private void HookComponentRender<T>() where T : Component
    {
        AddHook(new Hook(typeof(T).GetMethod("Render")!, ComponentRenderHook));
    }

    private void HookEntityRender<T>() where T : Entity
    {
        AddHook(new Hook(typeof(T).GetMethod("Render")!, EntityRenderHook));
    }

    private static void ComponentListRenderHook(ILContext il)
    {
        var c = new ILCursor(il);
        while (c.TryGotoNext(MoveType.Before, i => i.MatchCallvirt<Component>(nameof(Component.Render))))
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
        while (c.TryGotoNext(MoveType.Before, i => i.MatchCallvirt<Entity>(nameof(Entity.Render))))
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

    private static void BorderRenderHook(On.Monocle.Entity.orig_Render orig, Entity self)
    {
        if (self is MoveBlock.Border border && Instance.Enabled)
        {
            var parentState = Instance.GetState(border.Parent) as IPositionSmoothingState;
            if (parentState != null)
            {
                var originalPosition = border.Parent.Position;
                border.Parent.Position = parentState.SmoothedRealPosition.Round();
                orig(self);
                border.Parent.Position = originalPosition;
                return;
            }
        }

        orig(self);
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
        if (Instance.Enabled && !TemporarilyDisablePushSpriteSmoothing)
            pos = Instance.GetSpritePosition(pos);
        orig(self, texture, sourceX, sourceY, sourceW, sourceH, pos.X, pos.Y, destinationW, destinationH, color,
            originX, originY, rotationSin, rotationCos, depth, effects);
    }
}