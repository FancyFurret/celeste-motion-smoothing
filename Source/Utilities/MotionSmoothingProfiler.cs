using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// Opt-in per-phase profiler covering the whole life of a frame: the update tick, the mod's
// pre-render and post-render work, the render itself, and every hook the mod installs inside it.
// Inert until the `motion_smoothing_profile` command switches it on — the call sites pay one
// static bool test each and nothing more.
//
// Purely diagnostic. Nothing else in the mod reads any of it, so the file plus its
// `MotionSmoothingProfiler.` call sites can be removed wholesale without changing behaviour.
public static class MotionSmoothingProfiler
{
    // Hot-path gate. Read directly at every call site so it stays a plain static field test.
    public static bool Enabled;

    // Whether the frame being drawn counts. The debug console renders its whole scrollback, which
    // is tens of thousands of sprites on its own and grows every time a report is logged to it, so
    // console-open frames are excluded outright rather than left to skew the averages.
    private static bool _sampling;

    // Which phases are *timed*. Counts are always collected -- they're a single increment -- but a
    // timestamp pair costs more than some of these hook bodies do, and the per-sprite phases fire
    // often enough that timing all of them at once distorts the very thing being measured. So
    // timing is selectable by group; run one group at a time for numbers worth trusting.
    [Flags]
    public enum Group
    {
        None = 0,
        Frame = 1 << 0,   // update and draw phases, and whole render passes
        Sprites = 1 << 1, // the per-sprite hook chain
        Objects = 1 << 2, // the per-entity and per-component hooks
        All = Frame | Sprites | Objects
    }

    public enum Phase
    {
        // --- Update tick ------------------------------------------------------------------
        UpdateTotal,            // the whole of Engine.Update, mod hooks and vanilla alike
        UpdatePusherScan,       // ActorPushTracker's ride scan, ahead of the scene update
        UpdatePositions,        // UpdateHistory over every smoothed object, in Scene.AfterUpdate

        // --- Draw, outside the render ----------------------------------------------------
        DrawTotal,              // the whole of Engine.Draw
        DrawSmoothing,          // CalculateSmoothedPositions over every smoothed object
        DrawPreRender,          // writing smoothed positions onto the objects
        DrawPostRender,         // putting them back
        DrawAtDrawInput,        // AtDrawInputHandler update/reset pair
        DrawAtDrawUpdates,      // UpdateAtDraw replaying particle/backdrop updates
        DrawOrig,               // the render itself; everything below is nested inside it

        // --- Whole render passes ----------------------------------------------------------
        RenderGameplay,         // GameplayRenderer.Render
        RenderBackdrop,         // BackdropRenderer.Render
        RenderBloom,            // BloomRenderer.Apply
        RenderGaussianBlur,     // GaussianBlur.Blur
        RenderDistort,          // Distort.Render
        RenderSetRenderTarget,  // render-target binds
        RenderTextureBind,      // TextureCollection item sets
        RenderSpriteBatchState, // SpriteBatch.Begin/End hooks

        // --- Per-object hooks (inclusive: each contains the work it wraps) -----------------
        RenderObjectPush,       // EntityList/ComponentList IL hooks marking the render subject
        RenderComponentHook,    // the Component/Image/DustGraphic/PlayerHair render detours
        RenderSubpixelIntercept,// Fancy's per-entity BeginSubpixelEntityRender test

        // --- Per-sprite hooks (inclusive, and nested in each other) ------------------------
        RenderSpriteBatchDraw,  // SpriteBatch.Draw overload hooks, outermost of the chain
        RenderPushSpriteTotal,  // PushSpriteSmoother's whole PushSprite hook, orig included
        RenderPushSprite,       // just its offset work, orig excluded
        RenderHiresPushSprite,  // HiresCameraSmoother's whole PushSprite hook, orig included
        RenderCalcRounding,     // its Calc.Floor/Ceiling/Round hooks
    }

    private static readonly int PhaseCount = Enum.GetValues(typeof(Phase)).Length;
    private static readonly long[] Ticks = new long[PhaseCount];
    private static readonly long[] Calls = new long[PhaseCount];
    private static readonly bool[] Timed = new bool[PhaseCount];

    private static long _frames;
    private static long _updates;
    private static long _startTimestamp;
    private static double _timestampCostTicks;
    private static Group _group = Group.All;

    private static Group GroupOf(Phase phase) => phase switch
    {
        Phase.RenderObjectPush or Phase.RenderComponentHook or Phase.RenderSubpixelIntercept
            => Group.Objects,
        Phase.RenderSpriteBatchDraw or Phase.RenderPushSpriteTotal or Phase.RenderPushSprite
            or Phase.RenderHiresPushSprite or Phase.RenderCalcRounding
            => Group.Sprites,
        _ => Group.Frame
    };

    // Returns 0 when this phase isn't being timed, which Stop treats as "count only". A real
    // timestamp is never 0 in practice — the clock has been running since long before this.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Start(Phase phase) => _sampling && Timed[(int)phase] ? Stopwatch.GetTimestamp() : 0L;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Stop(Phase phase, long start)
    {
        if (start == 0L) return;
        Ticks[(int)phase] += Stopwatch.GetTimestamp() - start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Count(Phase phase)
    {
        if (_sampling) Calls[(int)phase]++;
    }

    public static void BeginFrame()
    {
        _sampling = Enabled && !Engine.Commands.Open;
        if (_sampling) _frames++;
    }

    public static void CountUpdate()
    {
        if (_sampling) _updates++;
    }

    private static void Reset(Group group)
    {
        Array.Clear(Ticks, 0, PhaseCount);
        Array.Clear(Calls, 0, PhaseCount);
        for (var i = 0; i < PhaseCount; i++)
            Timed[i] = (GroupOf((Phase)i) & group) != 0;

        _group = group;
        _frames = 0;
        _updates = 0;

        // Calibrate a timestamp so the report can say how much of what it measured is the
        // measuring. Every timed phase pays two of these per call.
        var calibrationStart = Stopwatch.GetTimestamp();
        const int samples = 200000;
        for (var i = 0; i < samples; i++) Stopwatch.GetTimestamp();
        _timestampCostTicks = (double)(Stopwatch.GetTimestamp() - calibrationStart) / samples;

        _startTimestamp = Stopwatch.GetTimestamp();
    }

    [Command("motion_smoothing_profile",
        "Toggle Motion Smoothing's frame profiler. Optional group: frame (default groups: all), "
        + "sprites, objects, all. Run once to start, close the console, play, run again to report.")]
    public static void Toggle(string group = null)
    {
        if (!Enabled)
        {
            var selected = group?.ToLowerInvariant() switch
            {
                "frame" => Group.Frame,
                "sprites" => Group.Sprites,
                "objects" => Group.Objects,
                _ => Group.All
            };

            Reset(selected);
            Enabled = true;
            Engine.Commands.Log($"Motion Smoothing: profiling ({selected}). CLOSE THE CONSOLE, play for a few "
                                + "seconds, then reopen it and run this again. Console-open frames are not counted.");
            Engine.Commands.Log("Counts are always collected; only the selected group is timed. Run one group at "
                                + "a time for the per-sprite numbers — timing them all at once distorts them.");
            return;
        }

        Enabled = false;
        _sampling = false;
        Report();
    }

    private static void Report()
    {
        var seconds = (double)(Stopwatch.GetTimestamp() - _startTimestamp) / Stopwatch.Frequency;

        if (_frames == 0 || seconds <= 0)
        {
            Line("Motion Smoothing profiler: no frames sampled. Close the console while sampling.");
            return;
        }

        Line("=== Motion Smoothing frame profile ===");
        Line($"  {_frames} frames and {_updates} updates over {seconds:0.00}s "
             + $"({_frames / seconds:0} fps, {_updates / seconds:0} ups)");
        Line($"  timed group: {_group}; timestamp cost {_timestampCostTicks / Stopwatch.Frequency * 1e9:0} ns, "
             + "two per timed call");
        Line("");
        Line($"  {"phase",-24}{"calls/frame",13}{"ms/s",9}{"% core",8}{"ns/call",10}{"measuring",11}");

        for (var i = 0; i < PhaseCount; i++)
        {
            var calls = Calls[i];
            if (calls == 0) continue;

            var name = ((Phase)i).ToString();
            var callsPerFrame = (double)calls / _frames;

            if (Ticks[i] == 0)
            {
                Line($"  {name,-24}{callsPerFrame,13:0.0}{"(untimed)",9}");
                continue;
            }

            var ms = (double)Ticks[i] / Stopwatch.Frequency * 1000.0 / seconds;
            var percent = ms / 10.0; // ms per second of wall time -> % of one core
            var nsPerCall = (double)Ticks[i] / Stopwatch.Frequency * 1e9 / calls;
            var measuringNs = 2 * _timestampCostTicks / Stopwatch.Frequency * 1e9;

            Line($"  {name,-24}{callsPerFrame,13:0.0}{ms,9:0.0}{percent,7:0.0}%{nsPerCall,10:0}{measuringNs,10:0}ns");
        }

        Line("");
        Line("  ns/call includes the two timestamps in the `measuring` column — subtract it for the real cost.");
        Line("  Nesting: DrawOrig is inside DrawTotal; the render passes and hooks are inside DrawOrig;");
        Line("  RenderSpriteBatchDraw > RenderPushSpriteTotal > RenderHiresPushSprite are nested in that order,");
        Line("  each inclusive of everything below it, so subtract to get each hook's own cost.");
        ReportSceneCensus();
        Line("=== end ===");
    }

    // Context for reading the numbers above: several phases scale with how many entities are in
    // the room rather than how much of it is on screen.
    private static void ReportSceneCensus()
    {
        if (Engine.Scene is not { } scene) return;

        var total = 0;
        var visible = 0;
        foreach (var entity in scene.Entities)
        {
            total++;
            if (entity.Visible) visible++;
        }

        Line("");
        Line($"  Scene entities: {total} ({visible} visible)");
    }

    // Mirrored to the Everest log so a report can be copied out of log.txt without the console
    // being open to read it.
    private static void Line(string text)
    {
        Engine.Commands.Log(text);
        Logger.Log(LogLevel.Info, "MotionSmoothingProfiler", text);
    }
}
