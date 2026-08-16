using System;
using System.Reflection;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class FrameRateTextMenuItem : TextMenuExt.IntSlider
{
    // Nasty Mode is allowed to take the framerate below the normal floor, all the way down to a
    // single frame per second. Only in Dynamic mode: Interval moves in steps of 60, so there's
    // nothing under 60 for it to land on anyway.
    private const int NastyModeMin = 10;

    private UpdateMode _updateMode;
    public UpdateMode UpdateMode
    {
        get => _updateMode;
        set => SetFrameUncapMode(value);
    }

    private readonly int _normalMin;
    private readonly int _max;
    private int _min;

    private int LastDir
    {
        set => typeof(TextMenuExt.IntSlider).GetField("lastDir", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(this, value);
    }

    // Dynamic mode defers to the base implementation of LeftPressed/RightPressed, which clamps
    // against its own copy of the minimum, so the floor has to move there as well as here.
    private int BaseMin
    {
        set => typeof(TextMenuExt.IntSlider).GetField("min", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(this, value);
    }

    public FrameRateTextMenuItem(string label, int min, int max, int value = 0) : base(
        // The base constructor clamps the initial value to the minimum without telling anyone, so
        // it has to be given the lowered floor up front -- otherwise a menu opened while Nasty Mode
        // is running below 60 would show 60 while the setting stayed where it was.
        label, EffectiveMin(min, MotionSmoothingModule.Settings.FramerateIncreaseMethod), max, value)
    {
        _normalMin = min;
        _min = EffectiveMin(min, MotionSmoothingModule.Settings.FramerateIncreaseMethod);
        _max = max;
        UpdateMode = MotionSmoothingModule.Settings.FramerateIncreaseMethod;
    }

    private static int EffectiveMin(int normalMin, UpdateMode mode)
    {
        return MotionSmoothingModule.Settings.SillyMode && mode == UpdateMode.Dynamic
            ? NastyModeMin
            : normalMin;
    }

    // Called whenever something the floor depends on changes: the update mode here, Nasty Mode from
    // the settings. Raising the floor back to 60 takes the current value up with it.
    public void RefreshMinimum()
    {
        var min = EffectiveMin(_normalMin, _updateMode);
        if (min == _min)
            return;

        _min = min;
        BaseMin = min;

        if (Index >= _min)
            return;

        PreviousIndex = Index;
        Index = _min;
        OnValueChange?.Invoke(Index);
    }

    private void SetFrameUncapMode(UpdateMode mode)
    {
        _updateMode = mode;

        // Before the rounding below, so that leaving Dynamic mode clamps against the restored floor.
        RefreshMinimum();

        if (UpdateMode == UpdateMode.Dynamic)
            return;

        // Ensure the value is a multiple of 60
        PreviousIndex = Index;
        Index = (int)(Math.Round(Index / 60f) * 60);
        Index = Math.Clamp(Index, _min, _max);
        if (Index != PreviousIndex)
            OnValueChange?.Invoke(Index);
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
        if (UpdateMode == UpdateMode.Dynamic)
        {
            base.LeftPressed();
            return;
        }

        Audio.Play("event:/ui/main/button_toggle_off");
        PreviousIndex = Index;
        Index -= 60;
        Index = Math.Clamp(Index, _min, _max);
        LastDir = -1;
        ValueWiggler.Start();
        OnValueChange?.Invoke(Index);
    }

    public override void RightPressed()
    {
        if (UpdateMode == UpdateMode.Dynamic)
        {
            base.RightPressed();
            return;
        }

        Audio.Play("event:/ui/main/button_toggle_on");
        PreviousIndex = Index;
        Index += 60;
        Index = Math.Clamp(Index, _min, _max);
        LastDir = 1;
        ValueWiggler.Start();
        OnValueChange?.Invoke(Index);
    }
}