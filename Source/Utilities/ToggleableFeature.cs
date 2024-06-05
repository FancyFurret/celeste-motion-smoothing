using System.Collections.Generic;
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

    protected void AddHook(Hook hook)
    {
        _hooks.Add(hook);
    }

    protected void AddHook(ILHook ilHook)
    {
        _ilHooks.Add(ilHook);
    }
}