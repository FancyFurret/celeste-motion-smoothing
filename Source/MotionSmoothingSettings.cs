using System;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;

namespace Celeste.Mod.MotionSmoothing;

public enum SmoothingMode
{
    Extrapolate,
    Interpolate
}

public enum UpdateMode
{
    Interval,
    Dynamic
}

public enum UnlockCameraStrategy
{
    Hires,
    Unlock,
    Off
}

public enum UnlockCameraMode
{
    Extend,
    Zoom,
    Border
}

public class MotionSmoothingSettings : EverestModuleSettings
{
    // Defaults
    private bool _enabled = true;
    private bool _tasMode = false;
    private int _frameRate = 120;
    private int _preferredFrameRate = 120;
    private UnlockCameraStrategy _unlockCameraStrategy = UnlockCameraStrategy.Hires;
    private UnlockCameraMode _unlockCameraMode = UnlockCameraMode.Extend;
    private SmoothingMode _smoothingMode = SmoothingMode.Extrapolate;
    private UpdateMode _updateMode = UpdateMode.Interval;

    // Used for compatibility with Viv's game speed mod
    private double _gameSpeed = 60;
    private bool _gameSpeedInLevelOnly = true;

    private FrameRateTextMenuItem _frameRateMenuItem;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;

            if (!_enabled)
            {
                _frameRate = 60;
                _enabled = true;
                MotionSmoothingModule.Instance.ApplySettings();
                _enabled = false;
            }

            else
            {
                _frameRate = _preferredFrameRate;
            }

            MotionSmoothingModule.Instance.ApplySettings();
            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(value));
        }
    }

    [DefaultButtonBinding(new Buttons(), Keys.F8)]
    public ButtonBinding ButtonToggleSmoothing { get; set; }

    [DefaultButtonBinding(new Buttons(), Keys.F9)]
    public ButtonBinding ButtonToggleUnlockedCamera { get; set; }

    public int FrameRate
    {
        get => _frameRate;
        set
        {
            if (!Enabled)
            {
                return;
            }

            _frameRate = value;
            _preferredFrameRate = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // ReSharper disable once UnusedMember.Global
    public void CreateFrameRateEntry(TextMenu menu, bool _)
    {
        _frameRateMenuItem = new FrameRateTextMenuItem("Frame Rate", 60, 480, FrameRate);
        _frameRateMenuItem.Change(fps => FrameRate = fps);
        menu.Add(_frameRateMenuItem);
    }

    [SettingSubText(
        "Allows the camera to move by fractions of a pixel, i.e.\n" +
        "half a pixel could be shown on the side of the screen.\n" +
        "This makes slow camera movements look *MUCH* smoother.\n\n" +
        "HiRes: Changes level rendering to be at a higher internal\n" +
        "resolution. Usually has the fewest visual glitches but may\n" +
        "not work in modded maps that use a large number of helpers\n\n" +
        "Unlock: lets the camera move without changing the rendering.\n" +
        "process. Has the highest compatibility, but makes the entire\n" +
        "background jitter when moving.")]
    public UnlockCameraStrategy UnlockCameraStrategy
    {
        get => _unlockCameraStrategy;
        set
        {
            _unlockCameraStrategy = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    [SettingSubText(
        "Only applies if Smooth Camera is set to Unlock. Determines\n" +
        "how unrendered portions of the level are hidden.\n" +
        "Zoom: Zooms the camera in slightly\n" +
        "Extend: Extends the level to the edge of the window\n" +
        "Border: Adds a small black border around the level")]
    public UnlockCameraMode UnlockCameraMode
    {
        get => _unlockCameraMode;
        set => _unlockCameraMode = value;
    }

    [SettingSubText(
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

    [SettingIgnore]
    [YamlIgnore]
    public double GameSpeed
    {
        get => _gameSpeed;
        set
        {
            _gameSpeed = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    [SettingIgnore][YamlIgnore] public bool GameSpeedModified => Math.Abs(_gameSpeed - 60) > double.Epsilon;

    [SettingIgnore]
    [YamlIgnore]
    public bool GameSpeedInLevelOnly
    {
        get => _gameSpeedInLevelOnly;
        set
        {
            _gameSpeedInLevelOnly = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }
}