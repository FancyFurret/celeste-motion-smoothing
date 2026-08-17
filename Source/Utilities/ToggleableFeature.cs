using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public abstract class ToggleableFeature<T> where T : class
{
    public static T Instance { get; private set; }

    public bool Enabled { get; private set; }

    private bool _hooked;
    private readonly HashSet<Hook> _hooks = new();
    private readonly HashSet<ILHook> _ilHooks = new();

    protected ToggleableFeature()
    {
        Instance = this as T;
    }

    public virtual void Load()
    {
    }

    public virtual void Unload()
    {
        Disable();
    }

    public virtual void Enable()
    {
        if (!_hooked)
        {
            Hook();
            _hooked = true;
        }

        Enabled = true;
    }

    public virtual void Disable()
    {
        if (_hooked)
        {
            Unhook();
            _hooked = false;
        }

        Enabled = false;
    }

    // Turns the feature off but leaves its hooks in place, so turning it back on costs nothing.
    // Detouring is expensive -- re-JITting something the size of Engine.Update or Game.Tick stalls
    // for long enough that the engine then runs a burst of catch-up updates -- so features that are
    // toggled in response to a menu keypress want this instead of Disable. The catch is that their
    // hook bodies have to check Enabled themselves and call orig when it's false; only use this on
    // a feature that does. Disable and Unload still remove the hooks.
    public void Deactivate()
    {
        Enabled = false;
    }

    protected virtual void Hook()
    {
    }

    protected virtual void Unhook()
    {
        foreach (var hook in _hooks)
            hook.Dispose();
        foreach (var ilHook in _ilHooks)
            ilHook.Dispose();

        _hooks.Clear();
        _ilHooks.Clear();
    }

    // Prefer the (MethodBase, Delegate) overload below. This one disables inlining *after* the
    // Hook has already been constructed -- and the constructor applies the detour, at which point
    // Everest's EnsureLegalHook has already checked (and warned about) the not-yet-disabled method.
    // Kept as a backstop for any caller that still hands us a pre-built Hook.
    protected void AddHook(Hook hook)
    {
        MotionSmoothingModule.TryDisableInlining(hook.Source);
        _hooks.Add(hook);
    }

    // Disables inlining on the target *before* constructing the Hook. The Hook constructor applies
    // the detour immediately, and Everest's EnsureLegalHook checks its inlining-disabled set at that
    // moment -- so registering the method first both keeps the JIT from inlining the target (which
    // is why hooks silently failed for some users) and avoids Everest's "does not have inlining
    // disabled" warning. Idempotent, so double-covering a method handled elsewhere is harmless.
    protected Hook AddHook(MethodBase source, Delegate detour)
    {
        MotionSmoothingModule.TryDisableInlining(source);
        var hook = new Hook(source, detour);
        _hooks.Add(hook);
        return hook;
    }

    // Backstop; see the note on AddHook(Hook).
    protected void AddHook(ILHook ilHook)
    {
        MotionSmoothingModule.TryDisableInlining(ilHook.Method);
        _ilHooks.Add(ilHook);
    }

    // IL-hook counterpart of AddHook(MethodBase, Delegate) -- disables inlining before applying.
    protected ILHook AddILHook(MethodBase source, ILContext.Manipulator manipulator)
    {
        MotionSmoothingModule.TryDisableInlining(source);
        var ilHook = new ILHook(source, manipulator);
        _ilHooks.Add(ilHook);
        return ilHook;
    }
}