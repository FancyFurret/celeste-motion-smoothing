using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.MotionSmoothing.Maps;
using Celeste.Mod.MotionSmoothing.Utilities;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;
using Monocle;
using Celeste.Mod.MotionSmoothing.Interop;

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
    private bool _useMapSettings = true;
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

    // Set by the SpeedrunTool save-state hooks, which need everything unhooked while a state is
    // restored. Kept separate from _enabled so that neither the player's saved setting nor an
    // active map suggestion is disturbed by it.
    private bool _forceDisabled = false;

    // Used for compatibility with Viv's game speed mod
    private double _gameSpeed = 60;
    private bool _gameSpeedInLevelOnly = true;

    private FrameRateTextMenuItem _frameRateMenuItem;

    private TextMenu.Item _enabledItem;
    private TextMenu.Item _cameraStrategyItem;
    private TextMenu.Item _renderMadelineWithSubpixelsItem;
    private TextMenu.Item _renderBackgroundHiresItem;
    private TextMenu.Item _renderForegroundHiresItem;
	private TextMenu.Item _hideStretchedEdgesItem;
    private TextMenu.Item _objectSmoothingItem;
    private TextMenu.Item _framerateIncreaseMethodItem;
    private TextMenu.Item _tasModeItem;

	private TextMenu.Item _sillyModeItem;

    // `locked` means a map is deciding this setting right now. A locked item stays selectable and
    // keeps showing its value -- it just refuses to change, and is tinted to say why. See
    // LockableMenuItems.
    private static void SetItemState(TextMenu.Item item, bool shouldDisable, bool locked = false)
    {
        if (item == null)
        {
            return;
        }

        // "Doesn't apply" wins over "a map is deciding it": there's nothing worth saying about who
        // chose a value that isn't doing anything either way.
        item.Disabled = shouldDisable;
        item.Selectable = !shouldDisable;

        SetLocked(item, locked && !shouldDisable);
    }

    // Locked lives on the two lockable subclasses, which close over different type arguments of
    // TextMenu.Option<T> and so have no shared base to set it through.
    private static void SetLocked(TextMenu.Item item, bool locked)
    {
        switch (item)
        {
            case LockableOnOff onOff:
                onOff.Locked = locked;
                break;
            case LockableSlider slider:
                slider.Locked = locked;
                break;
            case FrameRateTextMenuItem frameRate:
                frameRate.Locked = locked;
                break;
        }
    }

    // Points an item at the value that's actually in force. Menu items are built from whatever the
    // getters returned at the time, so an item that was created while a map had the setting
    // overridden goes on showing the map's value after the override drops -- which not only reads
    // as "my setting didn't come back", but means nudging the item afterwards would write the map's
    // value into the player's own settings.
    private static void SetItemValue<T>(TextMenu.Item item, T value)
    {
        if (item is not TextMenu.Option<T> option) return;

        var index = option.Values.FindIndex(entry => EqualityComparer<T>.Default.Equals(entry.Item2, value));
        if (index >= 0) option.Index = index;
    }

    private void RefreshMenuItemValues()
    {
        // Not a TextMenu.Option<T>, so these can't go through SetItemValue. The mode goes first: it
        // decides the framerate item's floor.
        if (_frameRateMenuItem != null)
        {
            _frameRateMenuItem.UpdateMode = FramerateIncreaseMethod;
            _frameRateMenuItem.SetValue(FrameRate);
        }

        SetItemValue(_enabledItem, Enabled);
        SetItemValue(_cameraStrategyItem, (int)UnlockCameraStrategy);
        SetItemValue(_framerateIncreaseMethodItem, (int)FramerateIncreaseMethod);
        SetItemValue(_renderMadelineWithSubpixelsItem, RenderMadelineWithSubpixels);
        SetItemValue(_renderBackgroundHiresItem, RenderBackgroundHires);
        SetItemValue(_renderForegroundHiresItem, RenderForegroundHires);
    }

    // Centralizes the "non-interactive based on other settings" logic. While the mod is
    // disabled, every other setting is forced off; while it's enabled, items fall back to
    // their dependency on the camera smoothing strategy. Safe to call before every item
    // exists: SetItemState ignores nulls, so the Create*Entry methods can call this as
    // they're built up.
    private void RefreshMenuItemStates()
    {
        bool masterDisabled = !Enabled;

        // A map is deciding these, so the player can't. MapSmoothingSuggestions drops its
        // overrides when Use Suggested Map Settings goes off, so this needs no extra gating.
        SetItemState(_enabledItem, false, MapSmoothingSuggestions.IsLocked(MapSmoothingOption.Enabled));

        // These only depend on the master Enabled toggle.
        SetItemState(_frameRateMenuItem, masterDisabled,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.FrameRate));
        SetItemState(_cameraStrategyItem, masterDisabled,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.CameraSmoothingMode));
        // Object Smoothing only means something above 60fps -- below that the getter reports Off
        // whatever the item says.
        SetItemState(_objectSmoothingItem, masterDisabled || FrameRate <= PhysicsFrameRate);
        SetItemState(_framerateIncreaseMethodItem, masterDisabled,
            MapSmoothingSuggestions.ForcesDynamicUpdateMode);
        SetItemState(_tasModeItem, masterDisabled);

        // These additionally require the Fancy camera smoothing strategy.
        bool cameraNotFancy = UnlockCameraStrategy != UnlockCameraStrategy.Hires;
        SetItemState(_renderMadelineWithSubpixelsItem, masterDisabled || cameraNotFancy,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.RenderMadelineWithSubpixelPrecision));
        SetItemState(_renderBackgroundHiresItem, masterDisabled || cameraNotFancy,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.SmoothBackground));
        SetItemState(_renderForegroundHiresItem, masterDisabled || cameraNotFancy,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.SmoothForeground));
        SetItemState(_sillyModeItem, masterDisabled || cameraNotFancy);

        // This is disabled only when camera smoothing is fully Off.
        bool cameraOff = UnlockCameraStrategy == UnlockCameraStrategy.Off;
        SetItemState(_hideStretchedEdgesItem, masterDisabled || cameraOff);
    }

    public bool Enabled
    {
        get
        {
            // SpeedrunTool's state restore wins over everything; after that, a map's suggestion
            // (see MapSmoothingSuggestions) stands in for the player's saved value while they're
            // inside that map.
            if (_forceDisabled) return false;
            if (MapSmoothingSuggestions.TryGet(MapSmoothingOption.Enabled, out bool mapSmoothing))
                return mapSmoothing;

            return _enabled;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to: not the menu (whose
            // item refuses input), not the hotkeys, not another mod reaching in through interop.
            // The lock lifts when the player leaves the map or turns off Use Suggested Map
            // Settings. Nothing is locked while Everest deserializes the settings at startup, so
            // the saved value still loads.
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.Enabled)) return;

            _enabled = value;

            RefreshMenuItemStates();

            MotionSmoothingModule.Instance.ApplySettings();
            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(Enabled));
        }
    }

    // Built by hand rather than left to Everest so that a map can lock it like the rest.
    public void CreateEnabledEntry(TextMenu menu, bool inGame)
    {
        // A legend for the tint, above everything else because it explains items further down.
        // Enabled is the first property in the class, so this is the first thing after the section
        // header. Only worth the line when there's something tinted to explain.
        if (MapSmoothingSuggestions.AnyLocked)
        {
            menu.Add(new TextMenu.SubHeader(
                "Settings shown in purple are being chosen by this map.",
                topPadding: false
            ));
        }

        var item = new LockableOnOff("Enabled", Enabled);
        item.Change(value => Enabled = value);

        _enabledItem = item;

        menu.Add(item);

        RefreshMenuItemStates();
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public bool UserEnabled => _enabled;

    // Temporarily forces smoothing off without touching either the player's saved setting or an
    // active map suggestion, so that both are still there when it's switched back.
    [SettingIgnore]
    [YamlIgnore]
    public bool ForceDisabled
    {
        get => _forceDisabled;
        set
        {
            _forceDisabled = value;

            RefreshMenuItemStates();

            MotionSmoothingModule.Instance.ApplySettings();
            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(Enabled));
        }
    }

    [DefaultButtonBinding(new Buttons(), Keys.F8)]
    public ButtonBinding ButtonToggleMotionSmoothingEnabled { get; set; }

    [DefaultButtonBinding(new Buttons(), Keys.F9)]
    public ButtonBinding ButtonChangeCameraSmoothingMode { get; set; }

    public UnlockCameraStrategy UnlockCameraStrategy
    {
        get
        {
            // A map can ask for a specific strategy -- see MapSmoothingSuggestions.
            var strategy = MapSmoothingSuggestions.TryGetCameraSmoothing(out var mapStrategy)
                ? mapStrategy
                : _unlockCameraStrategy;

            // Fancy (Hires) is incompatible with auspicioushelper, so transparently
            // fall back to Fast (Unlock) regardless of what's persisted on disk.
            if (strategy == UnlockCameraStrategy.Hires && IsAuspiciousHelperLoaded)
            {
                return UnlockCameraStrategy.Unlock;
            }

            return strategy;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to: not the menu (whose
            // item refuses input), not the hotkeys, not another mod reaching in through interop.
            // The lock lifts when the player leaves the map or turns off Use Suggested Map
            // Settings. Nothing is locked while Everest deserializes the settings at startup, so
            // the saved value still loads.
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.CameraSmoothingMode)) return;

            _unlockCameraStrategy = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public UnlockCameraStrategy UserUnlockCameraStrategy => _unlockCameraStrategy;

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

        var strategySlider = new LockableSlider(
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
        get
        {
            if (MapSmoothingSuggestions.TryGet(MapSmoothingOption.RenderMadelineWithSubpixelPrecision, out bool mapValue))
                return mapValue;

            return _renderMadelineWithSubpixels;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to: not the menu (whose
            // item refuses input), not the hotkeys, not another mod reaching in through interop.
            // The lock lifts when the player leaves the map or turns off Use Suggested Map
            // Settings. Nothing is locked while Everest deserializes the settings at startup, so
            // the saved value still loads.
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.RenderMadelineWithSubpixelPrecision)) return;

            _renderMadelineWithSubpixels = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public bool UserRenderMadelineWithSubpixels => _renderMadelineWithSubpixels;

    public void CreateRenderMadelineWithSubpixelsEntry(TextMenu menu, bool inGame)
    {
        _renderMadelineWithSubpixelsItem = new LockableOnOff(
            "Render Madeline with Subpixel Precision",
            RenderMadelineWithSubpixels
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

    public static bool IsAuspiciousHelperLoaded
    {
        get
        {
			return AuspicioushelperImports.hasActiveLayer?.Invoke() ?? false;
        }
    }

    public bool RenderBackgroundHires
    {
        get
        {
            if (MapSmoothingSuggestions.TryGet(MapSmoothingOption.SmoothBackground, out bool mapValue))
                return mapValue;

            return _renderBackgroundHires;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to: not the menu (whose
            // item refuses input), not the hotkeys, not another mod reaching in through interop.
            // The lock lifts when the player leaves the map or turns off Use Suggested Map
            // Settings. Nothing is locked while Everest deserializes the settings at startup, so
            // the saved value still loads.
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.SmoothBackground)) return;

            _renderBackgroundHires = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public bool UserRenderBackgroundHires => _renderBackgroundHires;

    public void CreateRenderBackgroundHiresEntry(TextMenu menu, bool inGame)
    {
        _renderBackgroundHiresItem = new LockableOnOff(
            "Smooth Background",
            RenderBackgroundHires
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
        get
        {
            if (MapSmoothingSuggestions.TryGet(MapSmoothingOption.SmoothForeground, out bool mapValue))
                return mapValue;

            return _renderForegroundHires;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to: not the menu (whose
            // item refuses input), not the hotkeys, not another mod reaching in through interop.
            // The lock lifts when the player leaves the map or turns off Use Suggested Map
            // Settings. Nothing is locked while Everest deserializes the settings at startup, so
            // the saved value still loads.
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.SmoothForeground)) return;

            _renderForegroundHires = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public bool UserRenderForegroundHires => _renderForegroundHires;

    public void CreateRenderForegroundHiresEntry(TextMenu menu, bool inGame)
    {
        _renderForegroundHiresItem = new LockableOnOff(
            "Smooth Foreground",
            RenderForegroundHires
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



    public UpdateMode FramerateIncreaseMethod
    {
        get
        {
            // A map asking for a framerate Interval mode can't produce is asking for Dynamic mode
            // with it -- see MapSmoothingSuggestions.ForcesDynamicUpdateMode.
            if (MapSmoothingSuggestions.ForcesDynamicUpdateMode) return UpdateMode.Dynamic;

            return _updateMode;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to. The lock lifts when the
            // player leaves the map or turns off Use Suggested Map Settings. Nothing is locked
            // while Everest deserializes the settings at startup, so the saved value still loads.
            if (MapSmoothingSuggestions.ForcesDynamicUpdateMode) return;

            _updateMode = value;
            if (_frameRateMenuItem != null)
                _frameRateMenuItem.UpdateMode = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public UpdateMode UserFramerateIncreaseMethod => _updateMode;

    public void CreateFramerateIncreaseMethodEntry(TextMenu menu, bool inGame)
    {
        _framerateIncreaseMethodItem = new LockableSlider(
            "Framerate Increase Method",
            index => ((UpdateMode)index) == UpdateMode.Interval ? "Interval" : "Dynamic",
            0,
            Enum.GetValues(typeof(UpdateMode)).Length - 1,
            (int)FramerateIncreaseMethod
        );

        (_framerateIncreaseMethodItem as TextMenu.Slider).Change(index =>
        {
            FramerateIncreaseMethod = (UpdateMode)index;
        });

        menu.Add(_framerateIncreaseMethodItem);

        RefreshMenuItemStates();

        _framerateIncreaseMethodItem.AddDescription(
            menu,
            "Interval [Recommended]: Has the best compatibility, but restricts the FPS\n" +
            "to multiples of 60.\n" +
            "Dynamic: Allows any FPS (including below 60), but may rarely break other mods\n" +
			"(e.g. TAS Recorder)."
        );
    }

	

	// A framerate below 60 doesn't survive a restart -- MotionSmoothingModule.Initialize puts it
    // back to this -- so nobody can leave the game running at 5fps and not work out why.
    public const int MinPersistedFrameRate = 60;

    // The rate physics runs at. At or below it, every draw lands on a physics frame, so there are
    // no in-between frames for object smoothing to fill. Same number as above, different reason.
    public const int PhysicsFrameRate = 60;

    public int FrameRate
    {
        get
        {
            if (MapSmoothingSuggestions.TryGetFrameRate(out var mapFrameRate))
                return mapFrameRate;

            return _frameRate;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to: not the menu (whose
            // item refuses input), not another mod reaching in through interop. The lock lifts when
            // the player leaves the map or turns off Use Suggested Map Settings. Nothing is locked
            // while Everest deserializes the settings at startup, so the saved value still loads.
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.FrameRate)) return;

            // Always persist the value. This setter also runs during settings
            // deserialization, which can happen while Enabled is false (e.g. the mod was
            // saved disabled); returning early there would discard the saved framerate and
            // revert to the default. Only the live re-apply is gated on Enabled.
            _frameRate = value;

            // Object Smoothing stops applying at 60 and below, so the item has to gray out as the
            // framerate crosses that line.
            RefreshMenuItemStates();

            if (!Enabled)
            {
                return;
            }

            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

	// The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public int UserFrameRate => _frameRate;

    // ReSharper disable once UnusedMember.Global
    public void CreateFrameRateEntry(TextMenu menu, bool _)
    {
        _frameRateMenuItem = new FrameRateTextMenuItem(
            "Framerate", FrameRateTextMenuItem.MinFrameRate, int.MaxValue, FrameRate);

        _frameRateMenuItem.Change(fps => FrameRate = fps);

        menu.Add(_frameRateMenuItem);

        RefreshMenuItemStates();

        // With the change handler wired and the map lock applied, put the value in step with the
        // mode: a framerate loaded from the settings file needn't be one Interval mode can produce.
        _frameRateMenuItem.UpdateMode = FramerateIncreaseMethod;
    }



	public SmoothingMode ObjectSmoothing
    {
        get
        {
            // At 60 and below there are no frames in between physics frames: every draw lands on
            // one, so there's nothing to smooth into and predicting positions could only move
            // objects away from where they actually are. Madeline is still drawn at her exact
            // subpixel position -- that's independent of smoothing, see PositionSmoother.
            if (FrameRate <= PhysicsFrameRate) return SmoothingMode.Off;

            return _smoothingMode;
        }
        set => _smoothingMode = value;
    }

    // The player's own saved value, ignoring the framerate it doesn't apply at.
    [SettingIgnore][YamlIgnore] public SmoothingMode UserObjectSmoothing => _smoothingMode;

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
			"How to draw frames in between physics ones. Only applies when the framerate is above 60.\n\n" +
            "Extrapolate [Recommended]: Predicts object positions in between physics frames\n" +
            "based on their velocities.\n\n" +
            "Interpolate: Uses the last two physics frames to compute the exact positions\n" +
            "in between. More technically correct, but adds 1-2 frames of input delay."
        );
    }



	public bool UseMapSettings
    {
        get => _useMapSettings;
        set
        {
            _useMapSettings = value;

            // Turning this off hands control back immediately rather than at the next map.
            MapSmoothingSuggestions.UseMapSettingsChanged();

            // The items were built while the map's values were in force, so they need pointing
            // back at the player's own before they're unlocked.
            RefreshMenuItemValues();
            RefreshMenuItemStates();
        }
    }

    public void CreateUseMapSettingsEntry(TextMenu menu, bool inGame)
    {
        var item = new TextMenu.OnOff(
            "Use Suggested Map Settings",
            _useMapSettings
        );

        item.Change(value =>
        {
            UseMapSettings = value;
        });

        menu.Add(item);

        item.AddDescription(
            menu,
            "Maps can temporarily change Motion Smoothing settings. Turning this off\n" +
            "overrides maps' suggested settings and keeps yours."
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
            "This does not affect gameplay in levels! By default, the overworld is updated\n" +
            "at the full framerate, since accuracy there is not as important. Turning\n" +
            "this on locks the overworld update at 60 FPS, so that TASes function properly."
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
            "Smooths *everything* to an awful and glorious logical extreme (:\n" +
			"The framerate can also be set *below* 60 in this mode.\n\n" +
            "This setting is just for fun because it's technically possible; not\n" +
            "everything will be perfect. Playing with this enabled will get your\n" +
            "submissions rejected from Goldberries, the Hardlist, etc."
        );
    }
}