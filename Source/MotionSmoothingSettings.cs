using System;
using System.Collections;
using System.Reflection;
using Celeste.Mod.MotionSmoothing.Utilities;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;
using Monocle;

namespace Celeste.Mod.MotionSmoothing;

public enum SmoothingMode
{
    Extrapolate,
    Interpolate,
    Off
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
    private bool _renderMadelineWithSubpixels = true;
    private bool _renderBackgroundHires = true;
    private bool _renderForegroundHires = true;
	private bool _hideStretchedEdges = true;
    private SmoothingMode _smoothingMode = SmoothingMode.Extrapolate;
    private UpdateMode _updateMode = UpdateMode.Interval;

	private bool _sillyMode = false;

    // Used for compatibility with Viv's game speed mod
    private double _gameSpeed = 60;
    private bool _gameSpeedInLevelOnly = true;

    private FrameRateTextMenuItem _frameRateMenuItem;

    private TextMenu.Item _cameraStrategyItem;
    private TextMenu.Item _renderMadelineWithSubpixelsItem;
    private TextMenu.Item _renderBackgroundHiresItem;
    private TextMenu.Item _renderForegroundHiresItem;
	private TextMenu.Item _hideStretchedEdgesItem;
    private TextMenu.Item _objectSmoothingItem;
    private TextMenu.Item _framerateIncreaseMethodItem;
    private TextMenu.Item _tasModeItem;

	private TextMenu.Item _sillyModeItem;

    private static void SetItemState(TextMenu.Item item, bool shouldDisable)
    {
        if (item == null)
        {
            return;
        }

        item.Disabled = shouldDisable;
        item.Selectable = !shouldDisable;
    }

    // Centralizes the "non-interactive based on other settings" logic. While the mod is
    // disabled, every other setting is forced off; while it's enabled, items fall back to
    // their dependency on the camera smoothing strategy. Safe to call before every item
    // exists: SetItemState ignores nulls, so the Create*Entry methods can call this as
    // they're built up.
    private void RefreshMenuItemStates()
    {
        bool masterDisabled = !_enabled;

        // These only depend on the master Enabled toggle.
        SetItemState(_frameRateMenuItem, masterDisabled);
        SetItemState(_cameraStrategyItem, masterDisabled);
        SetItemState(_objectSmoothingItem, masterDisabled);
        SetItemState(_framerateIncreaseMethodItem, masterDisabled);
        SetItemState(_tasModeItem, masterDisabled);

        // These additionally require the Fancy camera smoothing strategy.
        bool cameraNotFancy = UnlockCameraStrategy != UnlockCameraStrategy.Hires;
        SetItemState(_renderMadelineWithSubpixelsItem, masterDisabled || cameraNotFancy);
        SetItemState(_renderBackgroundHiresItem, masterDisabled || cameraNotFancy);
        SetItemState(_renderForegroundHiresItem, masterDisabled || cameraNotFancy);
        SetItemState(_sillyModeItem, masterDisabled || cameraNotFancy);

        // This is disabled only when camera smoothing is fully Off.
        bool cameraOff = UnlockCameraStrategy == UnlockCameraStrategy.Off;
        SetItemState(_hideStretchedEdgesItem, masterDisabled || cameraOff);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;

            RefreshMenuItemStates();

            MotionSmoothingModule.Instance.ApplySettings();
            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(value));
        }
    }

    [DefaultButtonBinding(new Buttons(), Keys.F8)]
    public ButtonBinding ButtonToggleMotionSmoothingEnabled { get; set; }

    [DefaultButtonBinding(new Buttons(), Keys.F9)]
    public ButtonBinding ButtonChangeCameraSmoothingMode { get; set; }

    public int FrameRate
    {
        get => _frameRate;
        set
        {
            // Always persist the value. This setter also runs during settings
            // deserialization, which can happen while Enabled is false (e.g. the mod was
            // saved disabled); returning early there would discard the saved framerate and
            // revert to the default. Only the live re-apply is gated on Enabled.
            _frameRate = value;

            if (!Enabled)
            {
                return;
            }

            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // ReSharper disable once UnusedMember.Global
    public void CreateFrameRateEntry(TextMenu menu, bool _)
    {
        _frameRateMenuItem = new FrameRateTextMenuItem("Framerate", 60, 480, FrameRate);
        _frameRateMenuItem.Change(fps => FrameRate = fps);

        menu.Add(_frameRateMenuItem);

        RefreshMenuItemStates();
    }

    public UnlockCameraStrategy UnlockCameraStrategy
    {
        get
        {
            // Fancy (Hires) is incompatible with auspicioushelper, so transparently
            // fall back to Fast (Unlock) regardless of what's persisted on disk.
            if (_unlockCameraStrategy == UnlockCameraStrategy.Hires && IsAuspiciousHelperLoaded)
            {
                return UnlockCameraStrategy.Unlock;
            }

            return _unlockCameraStrategy;
        }
        set
        {
            _unlockCameraStrategy = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateUnlockCameraStrategyEntry(TextMenu menu, bool inGame)
    {
        bool auspiciousHelperLoaded = IsAuspiciousHelperLoaded;

        // When auspicioushelper is loaded, Fancy (Hires) is incompatible, so
        // exclude it from the slider and clamp the current value if needed.
        int minIndex = auspiciousHelperLoaded ? (int)UnlockCameraStrategy.Unlock : 0;
        int maxIndex = Enum.GetValues(typeof(UnlockCameraStrategy)).Length - 1;
        int initialIndex = (int)UnlockCameraStrategy;
        if (initialIndex < minIndex)
        {
            initialIndex = minIndex;
            UnlockCameraStrategy = (UnlockCameraStrategy)initialIndex;
        }

        var strategySlider = new TextMenu.Slider(
            "Camera Smoothing",
            index => {
				if ((UnlockCameraStrategy)index == UnlockCameraStrategy.Hires)
				{
					return "Fancy";
				}

				if ((UnlockCameraStrategy)index == UnlockCameraStrategy.Unlock)
				{
					return "Fast";
				}

				return "Off";
			},
            minIndex,
            maxIndex,
            initialIndex
        );

        strategySlider.Change(index =>
        {
            UnlockCameraStrategy = (UnlockCameraStrategy)index;

            RefreshMenuItemStates();
        });

        _cameraStrategyItem = strategySlider;

        menu.Add(strategySlider);

        RefreshMenuItemStates();

        if (auspiciousHelperLoaded)
        {
            menu.Add(new TextMenu.SubHeader(
                "Fancy mode is incompatible with this map.",
                topPadding: false
            ));
        }

        strategySlider.AddDescription(
            menu,
            "Lets the camera move continuously: that is, half of a pixel could be shown on\n" +
            "the side of the screen while the camera is moving. This is especially noticeable\n" +
            "when the camera is moving slowly.\n\n" +
            "Fancy: The highest quality result, but may impact performance on low-end systems.\n\n" +
            "Fast: Has negligible performance impact, but makes the entire background jitter\n" +
            "uncontrollably when moving." 
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

        menu.Add(_renderMadelineWithSubpixelsItem);

        RefreshMenuItemStates();

        _renderMadelineWithSubpixelsItem.AddDescription(
            menu,
            "Only applies if Camera Smoothing is set to Fancy. Turning this on lets Madeline\n" +
            "be drawn at her exact subpixel position (i.e. offset from the pixel grid),\n" +
			"which dramatically improves the clarity of her sprite while moving. There are\n" +
            "many safeguards in place to prevent subpixel information from being gleanable.\n" +
            "Turning this off may mildly improve performance.\n"
        );
    }



    // Reflection-based handle to auspicioushelper's MaterialPipe.layers field. We resolve
    // it lazily (the type only exists if the mod is loaded) and cache the FieldInfo.
    private static FieldInfo _auspiciousMaterialPipeLayersField;
    private static bool _auspiciousMaterialPipeLayersFieldResolved;

    private static FieldInfo GetAuspiciousMaterialPipeLayersField()
    {
        if (_auspiciousMaterialPipeLayersFieldResolved)
        {
            return _auspiciousMaterialPipeLayersField;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try
            {
                type = assembly.GetType("Celeste.Mod.auspicioushelper.MaterialPipe");
            }
            catch
            {
                continue;
            }

            if (type != null)
            {
                _auspiciousMaterialPipeLayersField = type.GetField(
                    "layers",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                );
                break;
            }
        }

        _auspiciousMaterialPipeLayersFieldResolved = true;
        return _auspiciousMaterialPipeLayersField;
    }

    public static bool IsAuspiciousHelperLoaded
    {
        get
        {
            var layersField = GetAuspiciousMaterialPipeLayersField();
            if (layersField == null)
            {
                return false;
            }

            return layersField.GetValue(null) is ICollection layers && layers.Count > 0;
        }
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

        menu.Add(_renderBackgroundHiresItem);

        RefreshMenuItemStates();

        _renderBackgroundHiresItem.AddDescription(
            menu,
            "Only applies if Camera Smoothing is set to Fancy. Turning this on lets the\n" +
            "background draw unlocked from the pixel grid, which makes parallax\n" +
            "backgrounds substantially smoother. Turning this off may mildly *reduce*\n" +
            "performance, especially in levels with unusually complicated backgrounds."
        );
    }



    public bool RenderForegroundHires
    {
        get => _renderForegroundHires;
        set
        {
            _renderForegroundHires = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateRenderForegroundHiresEntry(TextMenu menu, bool inGame)
    {
        _renderForegroundHiresItem = new TextMenu.OnOff(
            "Smooth Foreground",
            _renderForegroundHires
        );

        (_renderForegroundHiresItem as TextMenu.OnOff).Change(value =>
        {
            RenderForegroundHires = value;
        });

        menu.Add(_renderForegroundHiresItem);

        RefreshMenuItemStates();

        _renderForegroundHiresItem.AddDescription(
            menu,
            "Only applies if Camera Smoothing is set to Fancy. Turning this on lets the\n" +
            "foreground draw unlocked from the pixel grid; for example, the snow in\n" +
            "chapter 7 will drift smoothly. Turning this off may moderately *reduce*\n" +
            "performance, especially in levels with unusually complicated foregrounds."
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

        menu.Add(_hideStretchedEdgesItem);

        RefreshMenuItemStates();

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



    public SmoothingMode ObjectSmoothing
    {
        get => _smoothingMode;
        set => _smoothingMode = value;
    }

    public void CreateObjectSmoothingEntry(TextMenu menu, bool inGame)
    {
        _objectSmoothingItem = new TextMenu.Slider(
            "Object Smoothing",
            index => ((SmoothingMode)index) switch
            {
                SmoothingMode.Extrapolate => "Extrapolate",
                SmoothingMode.Interpolate => "Interpolate",
                _ => "Off"
            },
            0,
            Enum.GetValues(typeof(SmoothingMode)).Length - 1,
            (int)_smoothingMode
        );

        (_objectSmoothingItem as TextMenu.Slider).Change(index =>
        {
            ObjectSmoothing = (SmoothingMode)index;
        });

        menu.Add(_objectSmoothingItem);

        RefreshMenuItemStates();

        _objectSmoothingItem.AddDescription(
            menu,
            "Extrapolate: [Recommended] Predicts object positions in between physics frames\n" +
            "based on their velocities.\n\n" +
            "Interpolate: Uses the last two physics frames to compute the exact positions\n" +
            "in between. This is more technically correct, but it adds 1-2 frames of input delay.\n\n" +
            "Off: Disables smoothing entirely. Objects render only at their exact physics positions."
        );
    }

    public UpdateMode FramerateIncreaseMethod
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

    public void CreateFramerateIncreaseMethodEntry(TextMenu menu, bool inGame)
    {
        _framerateIncreaseMethodItem = new TextMenu.Slider(
            "Framerate Increase Method",
            index => ((UpdateMode)index) == UpdateMode.Interval ? "Interval" : "Dynamic",
            0,
            Enum.GetValues(typeof(UpdateMode)).Length - 1,
            (int)_updateMode
        );

        (_framerateIncreaseMethodItem as TextMenu.Slider).Change(index =>
        {
            FramerateIncreaseMethod = (UpdateMode)index;
        });

        menu.Add(_framerateIncreaseMethodItem);

        RefreshMenuItemStates();

        _framerateIncreaseMethodItem.AddDescription(
            menu,
            "Interval: [Recommended] Has the best compatibility, but restricts the FPS\n" +
            "to multiples of 60.\n" +
            "Dynamic: Allows any FPS, but may rarely break other mods (e.g. TAS Recorder)."
        );
    }

    public bool TasMode
    {
        get => _tasMode;
        set
        {
            _tasMode = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateTasModeEntry(TextMenu menu, bool inGame)
    {
        _tasModeItem = new TextMenu.OnOff(
            "TAS Mode",
            _tasMode
        );

        (_tasModeItem as TextMenu.OnOff).Change(value =>
        {
            TasMode = value;
        });

        menu.Add(_tasModeItem);

        RefreshMenuItemStates();

        _tasModeItem.AddDescription(
            menu,
            "*** This does not affect gameplay in levels! ***\n" +
            "By default, the overworld is updated at the full\n" +
            "framerate since accuracy there is not as important.\n" +
            "Turning this on locks the overworld update at 60 FPS\n" +
            "so that TASes function properly."
        );
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



	public bool SillyMode
    {
        get => _sillyMode;
        set
        {
            _sillyMode = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    public void CreateSillyModeEntry(TextMenu menu, bool inGame)
    {
        _sillyModeItem = new TextMenu.OnOff(
            "Nasty Mode",
            _sillyMode
        );

        (_sillyModeItem as TextMenu.OnOff).Change(value =>
        {
            SillyMode = value;
        });

        menu.Add(_sillyModeItem);

        RefreshMenuItemStates();

        _sillyModeItem.AddDescription(
            menu,
            "Smoothing too close to the sun (:\n\n" +
            "This setting is just for fun because it's technically possible; not\n" +
            "everything will be perfect. Playing with this enabled will get your\n" +
            "submissions rejected from Goldberries, the Hardlist, etc."
        );
    }
}