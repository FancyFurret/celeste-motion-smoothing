using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MotionSmoothing;

public enum SmoothingMode
{
    None,
    Extrapolate,
    Interpolate
}

public enum UpdateMode
{
    Interval,
    Dynamic
}

public class MotionSmoothingSettings : EverestModuleSettings
{
    // Defaults
    private bool _enabled = true;
    private bool _unlockCamera = true;
    private bool _tasMode = false;
    private int _frameRate = 120;
    private SmoothingMode _smoothingMode = SmoothingMode.Extrapolate;
    private UpdateMode _updateMode = UpdateMode.Interval;

    private FrameRateTextMenuItem _frameRateMenuItem;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    [DefaultButtonBinding(new Buttons(), Keys.F8)]
    public ButtonBinding ButtonToggleSmoothing { get; set; }

    [SettingIgnore]
    public int FrameRate
    {
        get => _frameRate;
        set
        {
            _frameRate = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // ReSharper disable once UnusedMember.Global
    public void CreateFrameRateEntry(TextMenu menu, bool _)
    {
        _frameRateMenuItem = new FrameRateTextMenuItem("Frame Rate", 60, 360, FrameRate);
        _frameRateMenuItem.Change(fps => FrameRate = fps);
        menu.Add(_frameRateMenuItem);
    }

    [SettingSubText(
        "This setting makes it so the camera is no longer\n" +
        "restricted to full pixel increments. Ie, half a pixel\n" +
        "could be shown on the side of the screen. This makes\n" +
        "slow camera movements look *MUCH* smoother.")]
    public bool UnlockCamera
    {
        get => _unlockCamera;
        set
        {
            _unlockCamera = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    [SettingSubText(
        "None: No smoothing\n" +
        "Extrapolate: [Recommended] Predicts object positions\n" +
        "        * Should feel very similar to vanilla\n" +
        "Interpolate: Smooths object position\n" +
        "        * The smoothest option, at the cost of 1-2 frames of delay")]
    public SmoothingMode SmoothingMode
    {
        get => _smoothingMode;
        set => _smoothingMode = value;
    }

    [SettingSubText(
        "Interval: Update the game every N draws\n" +
        "        * FPS must be a multiple of 60\n" +
        "Dynamic: Update and draw at different rates\n" +
        "        * FPS can be any value\n" +
        "        * More likely to break other mods"
    )]
    public UpdateMode UpdateMode
    {
        get => _updateMode;
        set
        {
            _updateMode = value;
            if (_frameRateMenuItem != null)
                _frameRateMenuItem.UpdateMode = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    [SettingSubText(
        "*** This mode does not affect gameplay in levels! ***\n" +
        "By default, the Overworld will be updated at the full\n" +
        "framerate since accuracy there is not as important.\n" +
        "This mode will keep the Overworld update at 60fps as\n" +
        "well, so that TASes will function properly.")]
    public bool TasMode
    {
        get => _tasMode;
        set
        {
            _tasMode = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }
}