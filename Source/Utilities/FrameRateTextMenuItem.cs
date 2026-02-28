using System;
using System.Reflection;

namespace Celeste.Mod.MotionSmoothing.Utilities;

public class FrameRateTextMenuItem : TextMenuExt.IntSlider
{
    private UpdateMode _updateMode;
    public UpdateMode UpdateMode
    {
        get => _updateMode;
        set => SetFrameUncapMode(value);
    }

    private readonly int _min;
    private readonly int _max;

    private int LastDir
    {
        set => typeof(TextMenuExt.IntSlider).GetField("lastDir", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(this, value);
    }

    public FrameRateTextMenuItem(string label, int min, int max, int value = 0) : base(
        label, min, max, value)
    {
        _min = min;
        _max = max;
        UpdateMode = MotionSmoothingModule.Settings.FramerateIncreaseMethod;
    }

    private void SetFrameUncapMode(UpdateMode mode)
    {
        _updateMode = mode;
        if (UpdateMode == UpdateMode.Dynamic)
            return;

        // Ensure the value is a multiple of 60
        PreviousIndex = Index;
        Index = (int)(Math.Round(Index / 60f) * 60);
        Index = Math.Clamp(Index, _min, _max);
        if (Index != PreviousIndex)
            OnValueChange?.Invoke(Index);
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