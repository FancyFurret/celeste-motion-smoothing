using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MotionSmoothing;

public class MotionSmoothingSettings : EverestModuleSettings
{
    private bool _enabled = true;

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
        Mode120,
        Mode180,
        Mode240,
        Mode300,
        Mode360
    };

    private FrameRateMode _frameRate = FrameRateMode.Mode120;

    public FrameRateMode FrameRate
    {
        get => _frameRate;
        set
        {
            _frameRate = value;
            MotionSmoothingModule.Instance.ApplySmoothing();
        }
    }

    public enum PlayerSmoothingMode
    {
        None,
        Interpolate,
        Extrapolate
    }

    [SettingSubText(
        "None: No player smoothing, movement should feel the same as vanilla\n" +
        "Interpolate: Smooths the player position, at the cost of some delay (1-2 frames)\n" +
        "Extrapolate: Smoother than none, but not perfect. Should feel similar to vanilla")]
    public PlayerSmoothingMode PlayerSmoothing { get; set; } = PlayerSmoothingMode.Interpolate;
}

public static class FrameRateModeExtensions
{
    public static int ToFps(this MotionSmoothingSettings.FrameRateMode mode)
    {
        return mode switch
        {
            MotionSmoothingSettings.FrameRateMode.Mode120 => 120,
            MotionSmoothingSettings.FrameRateMode.Mode180 => 180,
            MotionSmoothingSettings.FrameRateMode.Mode240 => 240,
            MotionSmoothingSettings.FrameRateMode.Mode300 => 300,
            MotionSmoothingSettings.FrameRateMode.Mode360 => 360,
            _ => 60
        };
    }
}