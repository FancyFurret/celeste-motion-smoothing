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

public class MotionSmoothingSettings : EverestModuleSettings
{
    // Defaults
    private bool _enabled = true;
    private bool _tasMode = false;
    private int _frameRate = 120;
    private UnlockCameraStrategy _unlockCameraStrategy = UnlockCameraStrategy.Hires;
    private bool _renderBackgroundHires = true;
    private bool _renderMadelineWithSubpixels = true;
	private bool _hideStretchedEdges = true;
    private SmoothingMode _smoothingMode = SmoothingMode.Extrapolate;
    private UpdateMode _updateMode = UpdateMode.Interval;

    // Used for compatibility with Viv's game speed mod
    private double _gameSpeed = 60;
    private bool _gameSpeedInLevelOnly = true;

    private FrameRateTextMenuItem _frameRateMenuItem;

    private TextMenu.Item _renderBackgroundHiresItem;
    private TextMenu.Item _renderMadelineWithSubpixelsItem;
	private TextMenu.Item _hideStretchedEdgesItem;

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
            index => {
				if ((UnlockCameraStrategy)index == UnlockCameraStrategy.Hires)
				{
					return "Highest Quality";
				}

				if ((UnlockCameraStrategy)index == UnlockCameraStrategy.Unlock)
				{
					return "Most Compatible";
				}

				return "Off";
			},
            0,
            Enum.GetValues(typeof(UnlockCameraStrategy)).Length - 1,
            (int)UnlockCameraStrategy
        );

        strategySlider.Change(index =>
        {
            UnlockCameraStrategy = (UnlockCameraStrategy)index;

			bool shouldDisableRenderBackgroundHires = UnlockCameraStrategy != UnlockCameraStrategy.Hires;
            _renderBackgroundHiresItem.Disabled = shouldDisableRenderBackgroundHires;
            _renderBackgroundHiresItem.Selectable = !shouldDisableRenderBackgroundHires;
			_renderMadelineWithSubpixelsItem.Disabled = shouldDisableRenderBackgroundHires;
			_renderMadelineWithSubpixelsItem.Selectable = !shouldDisableRenderBackgroundHires;

			bool shouldDisableHideStretchedEdges = UnlockCameraStrategy == UnlockCameraStrategy.Off;
            _hideStretchedEdgesItem.Disabled = shouldDisableHideStretchedEdges;
            _hideStretchedEdgesItem.Selectable = !shouldDisableHideStretchedEdges;
        });

        menu.Add(strategySlider);

        strategySlider.AddDescription(
            menu,
            "This lets the camera move continuously: that is, half of a pixel\n" +
            "could be shown on the side of the screen while the camera \n" +
            "is moving. This is especially noticeable when the camera\n" +
			"is slowly catching up to the player.\n\n" +
            "Highest Quality: Produces the smoothest visuals, but is\n" +
            "incompatible with a small number of other mods and may\n" +
            "impact performance on low-end systems.\n\n" +
            "Most Compatible: Has the highest compatibility, but makes\n" +
            "the entire background jitter uncontrollably when moving."
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
            "Smooth Background",
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
            "Only applies if Smooth Camera is set to Highest Quality.\n" +
            "Determines whether the background is drawn at a 6x scale.\n" +
            "This makes for a much smoother result, particularly with\n" +
            "parallax, and fixes occasional slightly incorrect colors\n" +
            "(for example in the final checkpoints of Farewell)"
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
            "Only applies if Smooth Camera is set to Highest Quality.\n" +
            "Determines whether Madeline is drawn at her exact subpixel\n" +
            "position (i.e. offset from the pixel grid). This makes\n" +
			"Madeline's sprite appear much more smooth and clear when\n" +
            "moving. When not moving, Madeline will always be drawn aligned\n" +
            "to the grid, so that subpixel information cannot be gleaned.\n"
        );
    }



	public bool HideStretchedEdges
    {
        get => _hideStretchedEdges;
        set
        {
            _hideStretchedEdges = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateHideStretchedEdgesEntry(TextMenu menu, bool inGame)
    {
        _hideStretchedEdgesItem = new TextMenu.OnOff(
            "Hide Stretched Level Edges",
            _hideStretchedEdges
        );

        (_hideStretchedEdgesItem as TextMenu.OnOff).Change(value =>
        {
            HideStretchedEdges = value;
        });

        // Set initial state based on UnlockCameraStrategy
        bool shouldDisable = UnlockCameraStrategy == UnlockCameraStrategy.Off;
        _hideStretchedEdgesItem.Disabled = shouldDisable;
        _hideStretchedEdgesItem.Selectable = !shouldDisable;

        menu.Add(_hideStretchedEdgesItem);

        _hideStretchedEdgesItem.AddDescription(
            menu,
            "Camera smoothing causes gaps on the right and bottom screen\n" +
            "edges, since offsetting the gameplay leaves nothing to fill\n" +
            "the gap. This setting very slightly zooms in the level to hide\n" +
			"these, but it can be turned off to stretch the level edges to\n" +
            "the screen edges to cover the gaps instead. It's recommended to\n" +
			"leave this on."
        );
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