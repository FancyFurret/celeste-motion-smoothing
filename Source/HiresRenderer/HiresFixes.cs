using System.Numerics;
using Celeste.Mod.MotionSmoothing.Utilities;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.HiresRenderer;

public class HiresFixes : ToggleableFeature<HiresFixes>
{
    protected override void Hook()
    {
        base.Hook();
        IL.Celeste.Parallax.Render += RemoveFloors;
        AddHook(new ILHook(typeof(Parallax).GetMethod("orig_Render")!, RemoveFloors));
    }

    protected override void Unhook()
    {
        base.Unhook();
        IL.Celeste.Parallax.Render -= RemoveFloors;
    }

    private static void RemoveFloors(ILContext il)
    {
        var c = new ILCursor(il);
        while (c.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(Calc), "Floor")))
        {
            // For compatibility, instead of removing the Floor call, have it floor a dummy Vector
            c.EmitCall(typeof(Vector2).GetProperty(nameof(Vector2.Zero))!.GetGetMethod()!);
            c.Index++; // Vector2.Zero.Floor()
            c.EmitPop(); // Pop the result of Vector2.Zero.Floor()
        }
    }
}