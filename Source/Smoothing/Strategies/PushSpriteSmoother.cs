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

    private Texture _currentRenderTarget;

    public void SmoothObject(object obj, IPositionSmoothingState state)
    {
        base.SmoothObject(obj, state);
    }

    protected override void Hook()
    {
        base.Hook();

        AddHook(new Hook(typeof(SpriteBatch).GetMethod("PushSprite", MotionSmoothingModule.AllFlags)!,
            PushSpriteHook));

        // Track the current render target so we can suppress smoothing while an entity draws
        // into its own scratch buffer. The singular GraphicsDevice.SetRenderTarget routes
        // through this same overload in FNA.
        AddHook(new Hook(typeof(GraphicsDevice).GetMethod("SetRenderTargets",
            [typeof(RenderTargetBinding[])])!, SetRenderTargetsHook));

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

    private delegate void orig_SetRenderTargets(GraphicsDevice self, RenderTargetBinding[] renderTargets);

    private static void SetRenderTargetsHook(orig_SetRenderTargets orig, GraphicsDevice self,
        RenderTargetBinding[] renderTargetBindings)
    {
        Instance._currentRenderTarget = renderTargetBindings is { Length: > 0 }
            ? renderTargetBindings[0].RenderTarget
            : null;
        orig(self, renderTargetBindings);
    }

    // True when the current render target isn't one of the gameplay/level buffers — i.e. an
    // entity has redirected rendering into its own scratch VirtualRenderTarget (e.g.
    // ScugHelper's DreamCrystal renders the crystal interior into a 32x32 buffer before
    // compositing it back). The whole Render() runs under our _currentObjects push, so without
    // this guard the scratch-internal draws get the smoothing offset baked in, then get it
    // applied *again* on composite — double-shifting the content and leaving the cleared
    // background color showing as a 1-2px square outline.
    //
    // Both the vanilla buffers and their Fancy-mode large equivalents are recognized, so this
    // works regardless of camera mode and regardless of whether HiresCameraSmoother has
    // swapped the binding to a large buffer.
    private bool IsRenderingToForeignTarget()
    {
        var target = _currentRenderTarget;
        if (target == null) return false; // backbuffer/screen — not a scratch target

        if (target == GameplayBuffers.Gameplay?.Target) return false;
        if (target == GameplayBuffers.Level?.Target) return false;
        if (target == GameplayBuffers.TempA?.Target) return false;
        if (target == GameplayBuffers.TempB?.Target) return false;

        if (Targets.HiresRenderer.Instance is { } renderer)
        {
            if (target == renderer.LargeGameplayBuffer?.Target) return false;
            if (target == renderer.LargeLevelBuffer?.Target) return false;
            if (target == renderer.LargeTempABuffer?.Target) return false;
            if (target == renderer.LargeTempBBuffer?.Target) return false;
            if (target == renderer.SmallBuffer?.Target) return false;
            if (target == renderer.GaussianBlurTempBuffer?.Target) return false;
        }

        return true;
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
        if (IsRenderingToForeignTarget()) return position;

        var obj = _currentObjects.Peek();
        if (NoInterpolate.IsDisabled(obj)) return position;

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
        // SillyMode: draw at the unrounded SmoothedRealPosition so the 6x composite gets
        // 1/6-px hair motion instead of a 6-px grid snap. Anchor stays OriginalDrawPosition
        // (integer) so the offset still lands the head at SmoothedRealPosition and shifts
        // Nodes[1..N] by the same delta.
        var targetPos = MotionSmoothingModule.Settings.SillyMode
            ? playerState.SmoothedRealPosition
            : playerState.SmoothedRealPosition.Round();
        return targetPos - playerState.OriginalDrawPosition;
    }

    private Vector2 GetOffset(object obj)
    {
        if (GetState(obj) is not IPositionSmoothingState state)
            return Vector2.Zero;

		if (MotionSmoothingModule.Settings.SillyMode)
		{
			return state.SmoothedRealPosition - state.OriginalDrawPosition;
		}

        // For Actors *and Platforms* (e.g. MoveBlock, which is Solid → Platform), Position
        // is always integer — subpixels live in ExactPosition via movementCounter, and
        // physics moves via MoveH/MoveV. So the destination PushSprite receives is integer-
        // anchored. Subtracting the *unrounded* ExactPosition (OriginalRealPosition) here
        // would produce a fractional offset and land the destination on a half-integer —
        // banker's-rounding parity then flips it ±1 px on rasterization. This is the same
        // root cause as the PlayerHair jitter fix above, and is what causes a thrown Glider
        // to vertically jitter ~2 px during its fall (movementCounter cycles 0/0.5/0/0.5
        // at the steady ~30 px/s gravity-clamped speed, putting the offset right at the
        // half-integer boundary every other tick). Under SillyMode it also manifested as
        // MoveBlocks appearing to grid-snap while everything else rendered subpixel,
        // because the offset math here was adding a fractional delta on top of an integer
        // Position, but the delta was measured from the *subpixel* ExactPosition — so it
        // effectively erased the subpixel advance.
        //
        // For non-Actor/non-Platform Entities (e.g. FireBall in ice mode), render position
        // itself is subpixel — Position is set fractionally and passed straight to
        // PushSprite — so OriginalRealPosition is the right anchor and the integer-rounding
        // form would strip the subpixel motion.
        var anchor = obj is Actor or Platform ? state.OriginalDrawPosition : state.OriginalRealPosition;

        // At rest (RealPositionHistory unchanged), SmoothedRealPosition collapses to
        // ExactPosition for an Actor/Platform — i.e. Position + movementCounter. When
        // movementCounter ends up near ±0.5 after the last integer spill (as IntroCar
        // does while ridden: MoveV(-10/60) per tick until Platform.MoveV's
        // Math.Round(counter) flips, leaving counter ≈ 0.5), float-precision drift
        // can put Round(ExactPosition) one pixel off Position.Round() — even though
        // nothing is actually moving. The subpixel-oscillation guard above doesn't
        // catch this because at perfect rest Sign(ΔReal)=0 and the sign-change counter
        // never advances. Vanilla renders straight from Position (integer), so falling
        // back to a zero offset here matches vanilla at rest.
        if (!state.Changed && obj is Actor or Platform)
            return Vector2.Zero;

        return state.SmoothedRealPosition.Round() - anchor;
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