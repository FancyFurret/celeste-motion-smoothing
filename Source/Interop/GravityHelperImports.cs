using System;
using MonoMod.ModInterop;

namespace Celeste.Mod.MotionSmoothing.Interop;

[ModImportName("GravityHelper")]
public static class GravityHelperImports
{
    public static Func<bool> IsPlayerInverted;
}