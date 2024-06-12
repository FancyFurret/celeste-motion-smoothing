using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class DeltaTimeFix : ToggleableFeature<DeltaTimeFix>
{
    private static float _fixedDeltaTimeMultiplier = 1f;

    protected override void Hook()
    {
        base.Hook();
        IL.Celeste.Starfield.UpdateStar += StarfieldUpdateHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        IL.Celeste.Starfield.UpdateStar -= StarfieldUpdateHook;
    }

    public static void UpdateFixedDeltaTimeMultiplier()
    {
        var updateDeltaTime = (float)GameUtils.UpdateElapsedTime.TotalSeconds;
        var deltaTime = Engine.DeltaTime;
        _fixedDeltaTimeMultiplier = deltaTime / updateDeltaTime;
    }

    private static void ApplyMultiplierToVec2(ILCursor cursor)
    {
        cursor.EmitLdsfld(typeof(DeltaTimeFix).GetField(nameof(_fixedDeltaTimeMultiplier),
            BindingFlags.Static | BindingFlags.NonPublic)!);
        cursor.EmitCall(typeof(Vector2).GetMethod("op_Multiply", new[] { typeof(Vector2), typeof(float) })!);
    }

    private static void ApplyMultiplier(ILCursor cursor)
    {
        cursor.EmitLdfld(typeof(DeltaTimeFix).GetField(nameof(_fixedDeltaTimeMultiplier),
            BindingFlags.Static | BindingFlags.NonPublic)!);
        cursor.EmitMul();
    }

    private static void StarfieldUpdateHook(ILContext il)
    {
        ILCursor cursor = new(il);

        if (cursor.TryGotoNext(MoveType.After, i =>
                i.MatchCall(typeof(Vector2).GetMethod("op_Division", new[] { typeof(Vector2), typeof(float) })!)))
            ApplyMultiplierToVec2(cursor);
    }
}