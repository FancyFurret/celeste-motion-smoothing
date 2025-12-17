using System;
using System.Collections;
using Celeste.Mod.MotionSmoothing.Utilities;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;
using Monocle;

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
    private bool _renderBackgroundHires = true;
    private bool _renderMadelineWithSubpixels = true;
    private UnlockCameraMode _unlockCameraMode = UnlockCameraMode.Extend;
    private SmoothingMode _smoothingMode = SmoothingMode.Extrapolate;
    private UpdateMode _updateMode = UpdateMode.Interval;

    // Used for compatibility with Viv's game speed mod
    private double _gameSpeed = 60;
    private bool _gameSpeedInLevelOnly = true;

    private FrameRateTextMenuItem _frameRateMenuItem;
    private TextMenu.Item _unlockCameraModeItem;

    private TextMenu.Item _renderBackgroundHiresItem;
    private TextMenu.Item _renderMadelineWithSubpixelsItem;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;

            if (_frameRateMenuItem != null)
            {
                _frameRateMenuItem.Disabled = !_enabled;
                _frameRateMenuItem.Selectable = _enabled;
            }

            MotionSmoothingModule.Instance.ApplySettings();
            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(value));
        }
    }

    [DefaultButtonBinding(new Buttons(), Keys.F8)]
    public ButtonBinding ButtonToggleSmoothing { get; set; }

    [DefaultButtonBinding(new Buttons(), Keys.F9)]
    public ButtonBinding ButtonToggleUnlockStrategy { get; set; }

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
        _frameRateMenuItem = new FrameRateTextMenuItem("Framerate", 60, 480, FrameRate);
        _frameRateMenuItem.Change(fps => FrameRate = fps);
       
        if (!_enabled)
        {
            _frameRateMenuItem.Disabled = true;
            _frameRateMenuItem.Selectable = false;
        }

        menu.Add(_frameRateMenuItem);
    }

    public UnlockCameraStrategy UnlockCameraStrategy
    {
        get => _unlockCameraStrategy;
        set
        {
            _unlockCameraStrategy = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateUnlockCameraStrategyEntry(TextMenu menu, bool inGame)
    {
        var strategySlider = new TextMenu.Slider(
            "Smooth Camera",
            index => ((UnlockCameraStrategy)index).ToString(),
            0,
            Enum.GetValues(typeof(UnlockCameraStrategy)).Length - 1,
            (int)UnlockCameraStrategy
        );

        strategySlider.Change(index =>
        {
            UnlockCameraStrategy = (UnlockCameraStrategy)index;
            UpdateUnlockCameraModeState();
        });

        menu.Add(strategySlider);

        strategySlider.AddDescription(
            menu,
            "Allows the camera to move by fractions of a pixel, i.e.\n" +
            "half a pixel could be shown on the side of the screen.\n" +
            "This makes slow camera movements look *MUCH* smoother.\n\n" +
            "Hires: Changes level rendering to be at a higher internal\n" +
            "resolution. This usually produces the smoothest visuals, but it\n" +
            "may impact performance on low-end systems and may not\n" +
			"work in modded maps that use a large number of helpers.\n\n" +
            "Unlock: Lets the camera move without changing the rendering\n" +
            "pipeline. Has the highest compatibility, but makes the entire\n" +
            "background jitter when moving.\n\n" +
			"Off: Disables all camera smoothing."
        );
    }

    public bool RenderBackgroundHires
    {
        get => _renderBackgroundHires;
        set
        {
            _renderBackgroundHires = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateRenderBackgroundHiresEntry(TextMenu menu, bool inGame)
    {
        _renderBackgroundHiresItem = new TextMenu.OnOff(
            "Render Background Hires",
            _renderBackgroundHires
        );

        (_renderBackgroundHiresItem as TextMenu.OnOff).Change(value =>
        {
            RenderBackgroundHires = value;
        });

        // Set initial state based on UnlockCameraStrategy
        bool shouldDisable = UnlockCameraStrategy != UnlockCameraStrategy.Hires;
        _renderBackgroundHiresItem.Disabled = shouldDisable;
        _renderBackgroundHiresItem.Selectable = !shouldDisable;

        menu.Add(_renderBackgroundHiresItem);

        _renderBackgroundHiresItem.AddDescription(
            menu,
            "Only applies if Smooth Camera is set to Hires. Determines\n" +
            "whether the background is drawn at a 6x scale. This makes\n" +
            "for a much smoother result, particularly with parallax. It also\n" +
            "fixes occasional slightly incorrect colors (for example in the\n" +
            "final checkpoints of Farewell)"
        );
    }



    public bool RenderMadelineWithSubpixels
    {
        get => _renderMadelineWithSubpixels;
        set
        {
            _renderMadelineWithSubpixels = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateRenderMadelineWithSubpixelsEntry(TextMenu menu, bool inGame)
    {
        _renderMadelineWithSubpixelsItem = new TextMenu.OnOff(
            "Render Madeline with Subpixel Precision",
            _renderMadelineWithSubpixels
        );

        (_renderMadelineWithSubpixelsItem as TextMenu.OnOff).Change(value =>
        {
            RenderMadelineWithSubpixels = value;
        });

        // Set initial state based on UnlockCameraStrategy
        bool shouldDisable = UnlockCameraStrategy != UnlockCameraStrategy.Hires;
        _renderMadelineWithSubpixelsItem.Disabled = shouldDisable;
        _renderMadelineWithSubpixelsItem.Selectable = !shouldDisable;

        menu.Add(_renderMadelineWithSubpixelsItem);

        _renderMadelineWithSubpixelsItem.AddDescription(
            menu,
            "Only applies if Smooth Camera is set to Hires. Determines\n" +
            "whether Madeline is drawn at her exact subpixel position\n" +
            "(i.e. offset from the pixel grid). This makes Madeline's\n" +
			"sprite appear much more smooth and clear when moving.\n" +
            "When not moving, Madeline will always be drawn aligned to the\n" +
            "grid, so that information about her subpixels cannot be gleaned.\n"
        );
    }



    public UnlockCameraMode UnlockCameraMode
    {
        get => _unlockCameraMode;
        set => _unlockCameraMode = value;
    }

    public void CreateUnlockCameraModeEntry(TextMenu menu, bool inGame)
    {
        _unlockCameraModeItem = new TextMenu.Slider(
            "Unlocked Camera Mode",
            index => ((UnlockCameraMode)index).ToString(),
            0,
            Enum.GetValues(typeof(UnlockCameraMode)).Length - 1,
            (int)UnlockCameraMode
        );

        (_unlockCameraModeItem as TextMenu.Slider).Change(index =>
        {
            UnlockCameraMode = (UnlockCameraMode)index;
        });

        // Set initial state based on UnlockCameraStrategy
        bool shouldDisable = UnlockCameraStrategy == UnlockCameraStrategy.Hires;
        _unlockCameraModeItem.Disabled = shouldDisable;
        _unlockCameraModeItem.Selectable = !shouldDisable;

        menu.Add(_unlockCameraModeItem);

        _unlockCameraModeItem.AddDescription(
            menu,
            "Only applies if Smooth Camera is set to Unlock. Determines\n" +
            "how unrendered portions of the level are hidden.\n" +
            "Zoom: Zooms the camera in slightly\n" +
            "Extend: Extends the level to the edge of the window\n" +
            "Border: Adds a small black border around the level"
        );
    }

    private void UpdateUnlockCameraModeState()
    {
        if (_unlockCameraModeItem != null)
        {
            bool shouldDisableUnlockMode = UnlockCameraStrategy != UnlockCameraStrategy.Unlock;
            _unlockCameraModeItem.Disabled = shouldDisableUnlockMode;
            _unlockCameraModeItem.Selectable = !shouldDisableUnlockMode;

            bool shouldDisableRenderBackgroundHires = UnlockCameraStrategy != UnlockCameraStrategy.Hires;
            _renderBackgroundHiresItem.Disabled = shouldDisableRenderBackgroundHires;
            _renderBackgroundHiresItem.Selectable = !shouldDisableRenderBackgroundHires;
			_renderMadelineWithSubpixelsItem.Disabled = shouldDisableRenderBackgroundHires;
			_renderMadelineWithSubpixelsItem.Selectable = !shouldDisableRenderBackgroundHires;
        }
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