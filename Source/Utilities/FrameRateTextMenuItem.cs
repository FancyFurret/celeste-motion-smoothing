using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class FrameRateTextMenuItem : TextMenuExt.IntSlider
{
    // Dynamic mode can run at any framerate, down to this. Anything below 60 is put back to 60 on
    // the next launch (MotionSmoothingModule.Initialize), so it can't be left on without noticing.
    public const int MinFrameRate = 5;

    // Interval mode runs the game's tick at the draw rate and lets every Nth through to Update, so
    // it can only do multiples of the 60fps update rate -- which is also its floor.
    private const int IntervalStep = 60;

    private UpdateMode _updateMode;
    public UpdateMode UpdateMode
    {
        get => _updateMode;
        set => SetFrameUncapMode(value);
    }

    // Whatever stalls the game -- a level load, an alt-tab, changing a setting that has to install
    // hooks -- is repaid as a run of catch-up updates, and each one of those re-reads the direction
    // key as still held down and repeats the press, walking the slider several steps in one go.
    // Wall-clock time is what separates the two cases: a burst of catch-up updates arrives inside a
    // couple of milliseconds, while a genuine held-key repeat is 0.1s apart (Input.MenuLeft's
    // repeat rate) and a deliberate second press slower still.
    private const double MinSecondsBetweenPresses = 0.05;

    private static readonly Stopwatch PressTimer = Stopwatch.StartNew();
    private double _lastPressTime = double.NegativeInfinity;

    // The floor depends on the mode: Dynamic goes down to _dynamicMin, Interval stops at 60.
    private readonly int _dynamicMin;
    private readonly int _max;
    private int _min;

    // The base class's private render state, which Render below reproduces. Cached, since they're
    // read every frame.
    private static readonly FieldInfo SineField =
        typeof(TextMenuExt.IntSlider).GetField("sine", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo LastDirField =
        typeof(TextMenuExt.IntSlider).GetField("lastDir", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private float Sine => (float)SineField.GetValue(this)!;

    private int LastDir
    {
        get => (int)LastDirField.GetValue(this)!;
        set => LastDirField.SetValue(this, value);
    }

    // Dynamic mode defers to the base implementation of LeftPressed/RightPressed, which clamps
    // against its own copy of the minimum, so the floor has to move there as well as here.
    private int BaseMin
    {
        set => typeof(TextMenuExt.IntSlider).GetField("min", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(this, value);
    }

    // A map is deciding the framerate right now (see MapSmoothingSuggestions), so the item refuses
    // to change and says so in the same purple the lockable items use.
    public bool Locked { get; set; }

    // `min` is the Dynamic-mode floor; Interval mode's is 60 either way.
    public FrameRateTextMenuItem(string label, int min, int max, int value = 0) : base(
        label, min, max, value)
    {
        _dynamicMin = min;
        _max = max;

        // Sets the floor for the mode we're starting in. Any snapping it does here is undone by the
        // SetValue below and would go unreported anyway, since the change handler is wired up after
        // construction -- the caller assigns UpdateMode again once it is, and that snap sticks.
        SetFrameUncapMode(MotionSmoothingModule.Settings.FramerateIncreaseMethod);

        // The base constructor has already clamped the value to the minimum; put back what we were
        // actually given.
        SetValue(value);
    }

    // Points the slider at a value that came from outside it: the setting it was built from, or a
    // map suggestion coming or going. A map can ask for a framerate the slider would never stop on
    // (24) or reach (3), and it's shown exactly as asked -- the item is locked while a map is
    // deciding it, so there's nothing to step away from. Not reported back as a change the player
    // made.
    public void SetValue(int value)
    {
        PreviousIndex = Index;
        Index = Math.Clamp(value, 1, _max);
    }

    private void SetFrameUncapMode(UpdateMode mode)
    {
        _updateMode = mode;

        // Dynamic mode can run at any framerate; Interval mode only at multiples of 60, 60 up.
        _min = mode == UpdateMode.Dynamic ? _dynamicMin : IntervalStep;
        BaseMin = _min;

        if (UpdateMode == UpdateMode.Dynamic)
            return;

        // A map is deciding the framerate: it stays exactly what the map asked for. The setting
        // would refuse a snapped value anyway, leaving the two disagreeing.
        if (Locked)
            return;

        // Ensure the value is a multiple of 60
        PreviousIndex = Index;
        Index = (int)(Math.Round(Index / 60f) * 60);
        Index = Math.Clamp(Index, _min, _max);
        if (Index != PreviousIndex)
            OnValueChange?.Invoke(Index);
    }

    private bool SwallowRepeatedPress()
    {
        var now = PressTimer.Elapsed.TotalSeconds;
        if (now - _lastPressTime < MinSecondsBetweenPresses)
            return true;

        _lastPressTime = now;
        return false;
    }

    // The base implementation sizes the value column to fit the max, which would reserve
    // room for ten digits now that there's effectively no cap. Size it to the current
    // value instead, with enough room for a four-digit framerate.
    public override float RightWidth()
    {
        return Math.Max(ActiveFont.Measure("8888").X, ActiveFont.Measure(Index.ToString()).X) + 120f;
    }

    public override void LeftPressed()
    {
        // Debounce first: a locked item would otherwise play its refusal sound once per update in a
        // catch-up burst.
        if (SwallowRepeatedPress() || LockableMenuItem.Refuse(Locked))
            return;

        if (UpdateMode == UpdateMode.Dynamic)
        {
            base.LeftPressed();
            return;
        }

        Audio.Play("event:/ui/main/button_toggle_off");
        PreviousIndex = Index;
        Index = Math.Clamp(Index - IntervalStep, _min, _max);
        LastDir = -1;
        ValueWiggler.Start();
        OnValueChange?.Invoke(Index);
    }

    public override void RightPressed()
    {
        // Debounce first: a locked item would otherwise play its refusal sound once per update in a
        // catch-up burst.
        if (SwallowRepeatedPress() || LockableMenuItem.Refuse(Locked))
            return;

        if (UpdateMode == UpdateMode.Dynamic)
        {
            base.RightPressed();
            return;
        }

        Audio.Play("event:/ui/main/button_toggle_on");
        PreviousIndex = Index;
        Index = Math.Clamp(Index + IntervalStep, _min, _max);
        LastDir = 1;
        ValueWiggler.Start();
        OnValueChange?.Invoke(Index);
    }

    // TextMenuExt.IntSlider.Render with the two changes LockableMenuItem makes for the lockable
    // Option<T> items: a locked item's label goes purple and its arrows go gray, since there's
    // nothing further any way. Both are decided inside the base method, so the body is reproduced
    // here -- see the note on LockableMenuItem.Render.
    public override void Render(Vector2 position, bool highlighted)
    {
        var alpha = Container.Alpha;
        var stroke = Color.Black * (alpha * alpha * alpha);
        var unselected = Locked ? LockableMenuItem.LockedColor : Color.White;
        var color = Disabled
            ? Color.DarkSlateGray
            : (highlighted ? Container.HighlightColor : unselected) * alpha;

        ActiveFont.DrawOutline(Label, position, new Vector2(0f, 0.5f), Vector2.One, color, 2f, stroke);

        if (_max - _min <= 0) return;

        var right = RightWidth();
        var lastDir = LastDir;

        ActiveFont.DrawOutline(Index.ToString(),
            position + new Vector2(Container.Width - right * 0.5f + lastDir * ValueWiggler.Value * 8f, 0f),
            new Vector2(0.5f, 0.5f), Vector2.One * 0.8f, color, 2f, stroke);

        var wiggle = Vector2.UnitX * (highlighted ? (float)Math.Sin(Sine * 4f) * 4f : 0f);
        var dimmed = Color.DarkSlateGray * alpha;

        var canGoLeft = !Locked && Index > _min;
        ActiveFont.DrawOutline("<",
            position + new Vector2(
                Container.Width - right + 40f + (lastDir < 0 ? -ValueWiggler.Value * 8f : 0f), 0f)
            - (canGoLeft ? wiggle : Vector2.Zero),
            new Vector2(0.5f, 0.5f), Vector2.One, canGoLeft ? color : dimmed, 2f, stroke);

        var canGoRight = !Locked && Index < _max;
        ActiveFont.DrawOutline(">",
            position + new Vector2(
                Container.Width - 40f + (lastDir > 0 ? ValueWiggler.Value * 8f : 0f), 0f)
            + (canGoRight ? wiggle : Vector2.Zero),
            new Vector2(0.5f, 0.5f), Vector2.One, canGoRight ? color : dimmed, 2f, stroke);
    }
}