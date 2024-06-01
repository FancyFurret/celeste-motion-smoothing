using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MotionSmoothing;

public class MotionSmoothingSettings : EverestModuleSettings
{
    private bool _enabled = true;
    private FrameRateMode _frameRate = FrameRateMode.Mode120;
    private bool _unlockCamera = false;
    private bool _tasMode = false;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            var old = _enabled;
            _enabled = value;

            if (old != value)
                MotionSmoothingModule.Instance.ApplySmoothing();
        }
    }

    [DefaultButtonBinding(new Buttons(), Keys.F8)]
    public ButtonBinding ButtonToggleSmoothing { get; set; }

    public enum FrameRateMode
    {
        Mode60,
        Mode120,
        Mode180,
        Mode240,
        Mode300,
        Mode360
    };

    public FrameRateMode FrameRate
    {
        get => _frameRate;
        set
        {
            _frameRate = value;
            MotionSmoothingModule.Instance.ApplySmoothing();
        }
    }

    [SettingSubText(
        "None: No smoothing\n" +
        "Extrapolate: [Recommended] Predicts object positions\n" +
        "        * Should feel very similar to vanilla\n" +
        "Interpolate: Smooths object position\n" +
        "        * The smoothest option, at the cost of 1-2 frames of delay")]
    public SmoothingMode Smoothing { get; set; } = SmoothingMode.Extrapolate;

    [SettingSubText(
        "This setting makes it so the camera is no longer\n" +
        "restricted to full pixel increments. Ie, half a pixel\n" +
        "could be shown on the side of the screen. This makes\n" +
        "slow camera movements look *MUCH* smoother.\n" +
        "NOTE: This can make make it look like the player has\n" +
        "motion blur, since the player is still pixel locked.")]
    public bool UnlockCamera
    {
        get => _unlockCamera;
        set
        {
            _unlockCamera = value;
            MotionSmoothingModule.Instance.ApplySmoothing();
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
            MotionSmoothingModule.Instance.ApplySmoothing();
        }
    }
}

public static class FrameRateModeExtensions
{
    public static int ToFps(this MotionSmoothingSettings.FrameRateMode mode)
    {
        return mode switch
        {
            MotionSmoothingSettings.FrameRateMode.Mode60 => 60,
            MotionSmoothingSettings.FrameRateMode.Mode120 => 120,
            MotionSmoothingSettings.FrameRateMode.Mode180 => 180,
            MotionSmoothingSettings.FrameRateMode.Mode240 => 240,
            MotionSmoothingSettings.FrameRateMode.Mode300 => 300,
            MotionSmoothingSettings.FrameRateMode.Mode360 => 360,
            _ => 60
        };
    }
}