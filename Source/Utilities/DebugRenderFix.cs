using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class DebugRenderFix : ToggleableFeature<DebugRenderFix>
{
    protected override void Hook()
    {
        base.Hook();
        On.Monocle.EntityList.DebugRender += EntityListDebugRenderHook;
    }

    protected override void Unhook()
    {
        base.Unhook();
        On.Monocle.EntityList.DebugRender -= EntityListDebugRenderHook;
    }

    private static void EntityListDebugRenderHook(On.Monocle.EntityList.orig_DebugRender orig, EntityList self,
        Camera camera)
    {
        // Need to set DeltaTime back to the regular Update DeltaTime, so that things like CelesteTAS's spinner cycle
        // colors work correctly
        var origDeltaTime = Engine.DeltaTime;
        var origRawDeltaTime = Engine.RawDeltaTime;
        Engine.RawDeltaTime = GameUtils.UpdateElapsedSeconds;
        Engine.DeltaTime = GameUtils.CalculateDeltaTime(Engine.RawDeltaTime);
        orig(self, camera);
        Engine.DeltaTime = origDeltaTime;
        Engine.RawDeltaTime = origRawDeltaTime;
    }
}