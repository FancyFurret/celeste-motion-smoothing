using System;
using System.Collections.Generic;
using MonoMod.ModInterop;

namespace Celeste.Mod.MotionSmoothing.Interop;

[ModImportName("SpeedrunTool.SaveLoad")]
public static class SpeedrunToolImports
{
    public static Func<
        Action<Dictionary<Type, Dictionary<string, object>>, Level>, // saveState 
        Action<Dictionary<Type, Dictionary<string, object>>, Level>, // loadState
        Action, // clearState 
        Action<Level>, // beforeSaveState 
        Action<Level>, // beforeLoadState
        Action, // preCloneEntities 
        object // return value
    > RegisterSaveLoadAction;
}