using System;
using MonoMod.ModInterop;

namespace Celeste.Mod.MotionSmoothing.Interop;

[ModImportName("auspicioushelper.materials")]
public static class AuspicioushelperImports{
  // Returns true if any material layer is being used
  public static Func<bool> hasActiveLayer;
}