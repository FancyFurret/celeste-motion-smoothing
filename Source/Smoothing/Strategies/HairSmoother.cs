using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Strategies;

public class HairSmoother : ToggleableFeature<HairSmoother>
{
    protected override void Hook()
    {
        base.Hook();
        AddHook(new Hook(typeof(PlayerHair).GetMethod("Render")!, PlayerHairRenderHook));
    }

    private static void PlayerHairRenderHook(On.Celeste.PlayerHair.orig_Render orig, PlayerHair self)
    {
        var offset = GetHairOffset(self);
        if (offset != Vector2.Zero)
        {
            for (int i = 0; i < self.Nodes.Count; i++) self.Nodes[i] += offset;
            try { orig(self); }
            finally { for (int i = 0; i < self.Nodes.Count; i++) self.Nodes[i] -= offset; }
            return;
        }
        orig(self);
    }

    private static Vector2 GetHairOffset(PlayerHair hair)
    {
        var playerState = (hair.Entity is Player
            ? MotionSmoothingHandler.Instance.PlayerState
            : MotionSmoothingHandler.Instance.GetState(hair.Entity)) as IPositionSmoothingState;
        if (playerState == null) return Vector2.Zero;
        var targetPos = playerState.SmoothedRealPosition.Round();
        return targetPos - playerState.OriginalDrawPosition;
    }
}