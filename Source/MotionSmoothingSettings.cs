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

// The camera-smoothing strategy, and with it the renderer that draws the level. Off is only ever
// read now: it's how a settings file written before Camera Smoothing became a setting of its own
// stores "no camera smoothing", and how a map still asks for it. See RenderingMode below.
public enum UnlockCameraStrategy
{
    Hires,
    Unlock,
    Off
}

// What the player picks: the master switch and the choice of renderer, in one setting. Ordered so
// the index doubles as the menu slider's, with the option auspicioushelper rules out first.
public enum RenderingMode
{
    Fancy,
    Fast,
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
    private bool _cameraSmoothing = true;
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

    private TextMenu.Item _renderingModeItem;
    private TextMenu.Item _cameraSmoothingItem;
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

        SetItemValue(_renderingModeItem, (int)RenderingMode);
        SetItemValue(_cameraSmoothingItem, CameraSmoothing);
        SetItemValue(_framerateIncreaseMethodItem, (int)FramerateIncreaseMethod);
        SetItemValue(_renderMadelineWithSubpixelsItem, RenderMadelineWithSubpixels);
        SetItemValue(_renderBackgroundHiresItem, RenderBackgroundHires);
        SetItemValue(_renderForegroundHiresItem, RenderForegroundHires);
    }

    // Centralizes the "non-interactive based on other settings" logic. While the mod is
    // off, every other setting is forced off; while it's on, items fall back to their
    // dependency on the rendering mode and on camera smoothing. Safe to call before every
    // item exists: SetItemState ignores nulls, so the Create*Entry methods can call this as
    // they're built up.
    private void RefreshMenuItemStates()
    {
        bool masterDisabled = RenderingMode == RenderingMode.Off;

        // A map is deciding these, so the player can't. MapSmoothingSuggestions drops its
        // overrides when Use Suggested Map Settings goes off, so this needs no extra gating.
        SetItemState(_renderingModeItem, false, RenderingModeLocked);

        // These only depend on the mod being on at all.
        SetItemState(_frameRateMenuItem, masterDisabled,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.FrameRate));
        SetItemState(_cameraSmoothingItem, masterDisabled,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.CameraSmoothing));
        // Object Smoothing only means something above 60fps -- below that the getter reports Off
        // whatever the item says.
        SetItemState(_objectSmoothingItem, masterDisabled || FrameRate <= PhysicsFrameRate);
        SetItemState(_framerateIncreaseMethodItem, masterDisabled,
            MapSmoothingSuggestions.ForcesDynamicUpdateMode);
        SetItemState(_tasModeItem, masterDisabled);

        // These additionally require the Fancy rendering mode.
        bool notFancy = RenderingMode != RenderingMode.Fancy;
        SetItemState(_renderMadelineWithSubpixelsItem, masterDisabled || notFancy,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.RenderMadelineWithSubpixelPrecision));
        SetItemState(_renderBackgroundHiresItem, masterDisabled || notFancy,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.SmoothBackground));
        SetItemState(_renderForegroundHiresItem, masterDisabled || notFancy,
            MapSmoothingSuggestions.IsLocked(MapSmoothingOption.SmoothForeground));
        SetItemState(_sillyModeItem, masterDisabled || notFancy);

        // Nothing offsets the gameplay layer with camera smoothing off, so there are no gaps at
        // the screen edges left to hide.
        SetItemState(_hideStretchedEdgesItem, masterDisabled || !CameraSmoothing);
    }

    // The setting that replaced the old Enabled toggle: the master switch and the choice between
    // the two renderers, in one place. It keeps no state of its own -- Off is Enabled false, and
    // Fancy and Fast are the two camera strategies -- so a settings file written before the two
    // were merged loads straight into it, and every existing read of Enabled still works.
    [YamlIgnore]
    public RenderingMode RenderingMode
    {
        get
        {
            // Enabled already folds in SpeedrunTool's force-disable and a map's suggestion, so this
            // reports Off for those the same way it does for the player's own choice.
            if (!Enabled) return RenderingMode.Off;

            return UnlockCameraStrategy == UnlockCameraStrategy.Hires
                ? RenderingMode.Fancy
                : RenderingMode.Fast;
        }
        set
        {
            // Both halves have to move together, so this refuses while a map is deciding either one:
            // the two setters below would each refuse on their own, which would leave the mode
            // applied by halves. The lock lifts when the player leaves the map or turns off Use
            // Suggested Map Settings, and nothing is locked while Everest deserializes at startup.
            if (RenderingModeLocked) return;

            // A map with a hires styleground has to stay in Fancy. The getter would report Fancy
            // whatever were written here, so refusing keeps the player's own saved mode intact for
            // when they leave rather than quietly overwriting it with the one the map insists on.
            if (HiresStylegrounds.RequiresFancy && value != RenderingMode.Fancy) return;

            // Off leaves the camera strategy alone rather than writing one over it, so that turning
            // the mod back on -- from the menu, the hotkey, or a map -- comes back to Fancy or Fast
            // exactly as the player left it.
            if (value == RenderingMode.Off)
            {
                Enabled = false;
                return;
            }

            // Strategy first: Enabled's setter re-applies everything, and it should see the strategy
            // this mode is asking for rather than the one before it.
            UnlockCameraStrategy = value == RenderingMode.Fancy
                ? UnlockCameraStrategy.Hires
                : UnlockCameraStrategy.Unlock;

            // The strategy setter has already re-applied, so only take the Enabled path -- which
            // also notifies the other mods watching it -- when it actually changes.
            if (!_enabled) Enabled = true;
        }
    }

    // A map deciding either half of Rendering Mode is deciding the whole of it.
    public static bool RenderingModeLocked =>
        MapSmoothingSuggestions.IsLocked(MapSmoothingOption.Enabled) ||
        MapSmoothingSuggestions.IsLocked(MapSmoothingOption.CameraStrategy);

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore]
    public RenderingMode UserRenderingMode => !_enabled
        ? RenderingMode.Off
        : _unlockCameraStrategy == UnlockCameraStrategy.Hires
            ? RenderingMode.Fancy
            : RenderingMode.Fast;

    // Built by hand rather than left to Everest so that a map can lock it like the rest.
    public void CreateRenderingModeEntry(TextMenu menu, bool inGame)
    {
        // A legend for the tint, above everything else because it explains items further down.
        // Rendering Mode is the first property in the class, so this is the first thing after the
        // section header. Only worth the line when there's something tinted to explain.
        if (MapSmoothingSuggestions.AnyLocked)
        {
            menu.Add(new TextMenu.SubHeader(
                "Settings shown in purple are being chosen by this map.",
                topPadding: false
            ));
        }

        bool auspiciousHelperLoaded = IsAuspiciousHelperLoaded;
        bool fancyRequired = HiresStylegrounds.RequiresFancy;

        // Two maps' worth of constraint, from opposite directions. auspicioushelper's material
        // layers can't be rendered in Fancy, so it comes off the bottom of the slider; a hires
        // styleground can't be rendered in anything else, so it's the only value left. They never
        // both apply -- a map that would trip both is refused outright, see HiresStylegrounds.
        int minIndex = auspiciousHelperLoaded ? (int)RenderingMode.Fast : 0;
        int maxIndex = fancyRequired
            ? (int)RenderingMode.Fancy
            : Enum.GetValues(typeof(RenderingMode)).Length - 1;
        int initialIndex = (int)RenderingMode;
        if (initialIndex < minIndex) initialIndex = minIndex;
        if (initialIndex > maxIndex) initialIndex = maxIndex;
        if (initialIndex != (int)RenderingMode)
        {
            RenderingMode = (RenderingMode)initialIndex;
        }

        var modeSlider = new LockableSlider(
            "Rendering Mode",
            index => (RenderingMode)index switch
            {
                RenderingMode.Fancy => "Fancy",
                RenderingMode.Fast => "Fast",
                _ => "Off"
            },
            minIndex,
            maxIndex,
            initialIndex
        );

        modeSlider.Change(index =>
        {
            RenderingMode = (RenderingMode)index;

            RefreshMenuItemStates();
        });

        _renderingModeItem = modeSlider;

        menu.Add(modeSlider);

        RefreshMenuItemStates();

        if (auspiciousHelperLoaded)
        {
            menu.Add(new TextMenu.SubHeader(
                "Fancy mode is incompatible with this map.",
                topPadding: false
            ));
        }

        else if (fancyRequired)
        {
            menu.Add(new TextMenu.SubHeader(
                "This map requires Fancy mode to be enabled.",
                topPadding: false
            ));
        }

        modeSlider.AddDescription(
            menu,
            "Fancy: Supports all features at the highest quality, but may impact performance\n" +
			"on low-end systems.\n\n" +
            "Fast: Has negligible performance impact, but makes the entire background jitter\n" +
            "when Smooth Camera is on and does not support some features.\n\n" +
            "Off: Disables Motion Smoothing entirely."
        );
    }

    // No longer a setting the player picks directly -- Rendering Mode above is -- but still what
    // everything downstream asks, and still what the settings file stores.
    [SettingIgnore]
    public bool Enabled
    {
        get
        {
            // SpeedrunTool's state restore wins over everything; after that, a map's suggestion
            // (see MapSmoothingSuggestions) stands in for the player's saved value while they're
            // inside that map.
            if (_forceDisabled) return false;

            // A hires styleground has no meaning outside Fancy mode, so a map with one has the mod
            // on for as long as the player is in it. This sits above the map-suggestion layer
            // because it isn't a suggestion: a map that turned smoothing off *and* had a hires
            // styleground would render the styleground six times too large.
            if (HiresStylegrounds.RequiresFancy) return true;

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

    // The mod's one hotkey: it cycles Rendering Mode, which is both the master switch and the
    // choice of renderer. The property keeps its old name so that a player who has already bound
    // this doesn't lose the binding -- Everest stores bindings under the property name -- and the
    // label it shows comes from the dialog key instead.
    [SettingName("modoptions_motionsmoothing_buttonrenderingmode")]
    [DefaultButtonBinding(new Buttons(), Keys.F8)]
    public ButtonBinding ButtonToggleMotionSmoothingEnabled { get; set; }

    [SettingIgnore]
    public UnlockCameraStrategy UnlockCameraStrategy
    {
        get
        {
            // The other half of forcing Fancy. Above the map suggestion for the same reason
            // Enabled's is; the two forces can't both apply, because HiresStylegrounds refuses a
            // map that would trip auspicioushelper's as well.
            if (HiresStylegrounds.RequiresFancy) return UnlockCameraStrategy.Hires;

            // A map can ask for a specific renderer -- see MapSmoothingSuggestions.
            var strategy = MapSmoothingSuggestions.TryGetCameraStrategy(out var mapStrategy)
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
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.CameraStrategy)) return;

            _unlockCameraStrategy = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public UnlockCameraStrategy UserUnlockCameraStrategy => _unlockCameraStrategy;

    public bool CameraSmoothing
    {
        get
        {
            if (MapSmoothingSuggestions.TryGet(MapSmoothingOption.CameraSmoothing, out bool mapValue))
                return mapValue;

            // A settings file written before this was a setting of its own stores it as the third
            // camera strategy. NormalizeLegacyCameraStrategy folds that away at startup; this
            // covers the reads that happen before it gets the chance.
            if (_unlockCameraStrategy == UnlockCameraStrategy.Off) return false;

            return _cameraSmoothing;
        }
        set
        {
            // A map is deciding this right now, so nothing else gets to: not the menu (whose
            // item refuses input), not another mod reaching in through interop. The lock lifts when
            // the player leaves the map or turns off Use Suggested Map Settings. Nothing is locked
            // while Everest deserializes the settings at startup, so the saved value still loads.
            if (MapSmoothingSuggestions.IsLocked(MapSmoothingOption.CameraSmoothing)) return;

            _cameraSmoothing = value;
            MotionSmoothingModule.Instance.ApplySettings();
        }
    }

    // The player's own saved value, ignoring any map suggestion currently in force.
    [SettingIgnore][YamlIgnore] public bool UserCameraSmoothing => _cameraSmoothing;

    // A settings file from before Rendering Mode and Camera Smoothing were separate settings stores
    // "no camera smoothing" as a third camera strategy. Fold it into the pair that means the same
    // thing now -- Fast, with the camera left on the pixel grid -- so nothing downstream has to keep
    // the old shape in mind, and so turning Camera Smoothing back on has a strategy to return to.
    // Written straight to the fields: the setters would re-apply settings and re-lock against a map,
    // neither of which exists yet when this runs.
    public void NormalizeLegacyCameraStrategy()
    {
        if (_unlockCameraStrategy != UnlockCameraStrategy.Off) return;

        _unlockCameraStrategy = UnlockCameraStrategy.Unlock;
        _cameraSmoothing = false;
    }

    // Built by hand rather than left to Everest so that a map can lock it like the rest.
    public void CreateCameraSmoothingEntry(TextMenu menu, bool inGame)
    {
        var item = new LockableOnOff("Smooth Camera", CameraSmoothing);

        item.Change(value =>
        {
            CameraSmoothing = value;

            RefreshMenuItemStates();
        });

        _cameraSmoothingItem = item;

        menu.Add(item);

        RefreshMenuItemStates();

        item.AddDescription(
            menu,
            "Lets the camera move continuously: half of a pixel could be shown on the side of\n" +
            "the screen while the camera is moving. This is especially noticeable when the\n" +
            "camera is moving slowly."
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
            "Only supported in the Fancy rendering mode. Turning this on lets Madeline\n" +
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
            "Only supported in the Fancy rendering mode. Turning this on lets the\n" +
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
            "Only supported in the Fancy rendering mode. Turning this on lets the\n" +
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
            "the screen edges to cover the gaps instead."
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
            "Smooths *everything* to an awful and glorious logical extreme (:\n\n" +
            "This setting is just for fun because it's technically possible; not\n" +
            "everything will be perfect. Playing with this enabled will get your\n" +
            "submissions rejected from Goldberries, the Hardlist, etc."
        );
    }
}