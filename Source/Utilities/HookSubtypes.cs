using System;
using System.Collections.Generic;
using System.Linq;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class HookSubtypes
{
    public static List<Hook> HookAllMethods(Type type, string name, Delegate action)
    {
        var hooks = new List<Hook>();
        var subTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes())
            .Where(p => type.IsAssignableFrom(p) && !p.IsAbstract);

        foreach (var subType in subTypes)
        {
            if (subType.IsGenericType) continue; // Generic types can't be hooked (easily, at least)

            foreach (var method in subType.GetMethods().Where(m => m.Name == name))
            {
                // Make sure that the parameters match
                var parameters = method.GetParameters();
                if (parameters.Length != 0) continue; // TODO: Support parameters
                
                var hook = new Hook(method, action);
                hooks.Add(hook);
            }
        }

        return hooks;
    }

    public static List<ILHook> ILHookAllMethods(Type type, string name, Action<ILContext> action)
    {
        var hooks = new List<ILHook>();
        var subTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes())
            .Where(p => type.IsAssignableFrom(p) && !p.IsAbstract);

        foreach (var subType in subTypes)
        {
            if (subType.IsGenericType) continue; // Generic types can't be hooked (easily, at least)

            foreach (var method in subType.GetMethods().Where(m => m.Name == name))
            {
                // Make sure that the parameters match
                var parameters = method.GetParameters();
                if (parameters.Length != 0) continue; // TODO: Support parameters
                
                var hook = new ILHook(method, ctx => { action(ctx); });
                hooks.Add(hook);
            }
        }

        return hooks;
    }
}