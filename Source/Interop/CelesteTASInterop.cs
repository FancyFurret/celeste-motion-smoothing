using System;

namespace Celeste.Mod.MotionSmoothing.Interop;

public static class CelesteTasInterop
{
    public static bool CenterCamera => _celesteTasLoaded && (_getCenterCamera?.Invoke() ?? false);

    private static bool _celesteTasLoaded;
    private static Func<bool> _getCenterCamera;

    public static void Load()
    {
        var moduleMetadata = new EverestModuleMetadata
        {
            Name = "CelesteTAS",
            Version = new Version(3, 39, 0)
        };

        // Surely there's a better way...
        if (Everest.Loader.TryGetDependency(moduleMetadata, out var module))
        {
            var assembly = module.GetType().Assembly;
            var type = assembly.GetType("TAS.Module.CelesteTasSettings", false);
            if (type == null) return;

            var property = type.GetProperty("CenterCamera");
            if (property == null) return;

            var method = property.GetGetMethod();
            if (method == null) return;

            var instance = type.GetProperty("Instance")?.GetGetMethod()?.Invoke(null, null);
            if (instance == null) return;

            _getCenterCamera = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), instance, method);
            _celesteTasLoaded = true;
        }
    }
}