using System;

namespace Celeste.Mod.MotionSmoothing.Maps;

// The settings a map can ask for.
public enum MapSmoothingOption
{
    Enabled,
    SmoothBackground,
    SmoothForeground,
    RenderMadelineWithSubpixelPrecision,
    CameraSmoothingMode
}

// What a single controller asks for. Every field is null when it has no preference for that option.
public class MapSmoothingSuggestion
{
    public bool? Enabled;
    public bool? SmoothBackground;
    public bool? SmoothForeground;
    public bool? RenderMadelineWithSubpixelPrecision;
    public UnlockCameraStrategy? CameraSmoothingMode;

    public bool IsEmpty =>
        !Enabled.HasValue && !SmoothBackground.HasValue && !SmoothForeground.HasValue &&
        !RenderMadelineWithSubpixelPrecision.HasValue && !CameraSmoothingMode.HasValue;

    // The four options whose value is a plain on/off. CameraSmoothingMode isn't one of them and
    // always reads back null here -- use the CameraSmoothingMode field for it.
    public bool? GetBoolean(MapSmoothingOption option) => option switch
    {
        MapSmoothingOption.Enabled => Enabled,
        MapSmoothingOption.SmoothBackground => SmoothBackground,
        MapSmoothingOption.SmoothForeground => SmoothForeground,
        MapSmoothingOption.RenderMadelineWithSubpixelPrecision => RenderMadelineWithSubpixelPrecision,
        _ => null
    };

    public bool HasPreference(MapSmoothingOption option) =>
        option == MapSmoothingOption.CameraSmoothingMode
            ? CameraSmoothingMode.HasValue
            : GetBoolean(option).HasValue;

    public void Clear(MapSmoothingOption option)
    {
        switch (option)
        {
            case MapSmoothingOption.Enabled: Enabled = null; break;
            case MapSmoothingOption.SmoothBackground: SmoothBackground = null; break;
            case MapSmoothingOption.SmoothForeground: SmoothForeground = null; break;
            case MapSmoothingOption.RenderMadelineWithSubpixelPrecision:
                RenderMadelineWithSubpixelPrecision = null;
                break;
            case MapSmoothingOption.CameraSmoothingMode: CameraSmoothingMode = null; break;
        }
    }

    public MapSmoothingSuggestion Clone() => new()
    {
        Enabled = Enabled,
        SmoothBackground = SmoothBackground,
        SmoothForeground = SmoothForeground,
        RenderMadelineWithSubpixelPrecision = RenderMadelineWithSubpixelPrecision,
        CameraSmoothingMode = CameraSmoothingMode
    };
}

// The settings a map has asked for, applied as an *override layer* rather than by writing to the
// settings. The user's own saved values are never touched, which means (a) leaving the map restores
// them with no bookkeeping, and (b) a crash inside a map can't leave the player's settings
// rewritten. The settings getters consult this class, so every existing read of Settings.Enabled,
// Settings.UnlockCameraStrategy and friends transparently sees the map's value, and the settings
// *setters* drop the override, so a player who changes an option themselves takes control back.
//
// The suggestions come from MotionSmoothingController, which hands them over when Madeline touches
// it. Nothing here reads map data: an override exists only once the player has actually run into
// the controller that asks for it.
public static class MapSmoothingSuggestions
{
    public const string ControllerEntityName = "MotionSmoothing/MotionSmoothingController";

    private static readonly MapSmoothingOption[] FancyOnlyOptions =
    {
        MapSmoothingOption.SmoothBackground,
        MapSmoothingOption.SmoothForeground,
        MapSmoothingOption.RenderMadelineWithSubpixelPrecision
    };

    private static MapSmoothingSuggestion _active = new();

    // What the map last asked for, kept separately from what's in force so that turning Use
    // Suggested Map Settings off and back on puts it back rather than making the player walk
    // through the trigger again. Dropped when they leave the map.
    private static MapSmoothingSuggestion _lastSuggestion;

    // Set while Everest serializes our settings. The YAML serializer reads the same property
    // getters the game does, so the override has to step aside or it would be written to disk as
    // if it were the player's own choice.
    private static bool _suspended;

    public static void Load()
    {
        Everest.Events.Level.OnExit += LevelExit;
    }

    public static void Unload()
    {
        Everest.Events.Level.OnExit -= LevelExit;

        // Drop the override without re-applying settings: by the time the module unloads, every
        // smoothing feature has already been torn down and must stay that way.
        _active = new MapSmoothingSuggestion();
        _lastSuggestion = null;
    }

    // --- The override layer, read by MotionSmoothingSettings ---------------------------------

    public static bool TryGet(MapSmoothingOption option, out bool value)
    {
        if (!_suspended && _active.GetBoolean(option) is { } mapValue)
        {
            value = mapValue;
            return true;
        }

        value = false;
        return false;
    }

    public static bool TryGetCameraSmoothing(out UnlockCameraStrategy value)
    {
        if (!_suspended && _active.CameraSmoothingMode is { } mapValue)
        {
            value = mapValue;
            return true;
        }

        value = UnlockCameraStrategy.Hires;
        return false;
    }

    // Whether a map is currently deciding this option. The settings refuse to be written while it
    // is, so this is both what the menu tints and what actually enforces the lock.
    public static bool IsLocked(MapSmoothingOption option) => _active.HasPreference(option);

    public static bool AnyLocked => !_active.IsEmpty;

    // Runs the given action with the override stepped aside, so that it observes the player's own
    // saved settings. Used by MotionSmoothingModule.SaveSettings.
    public static void WithUserSettings(Action action)
    {
        _suspended = true;
        try
        {
            action();
        }
        finally
        {
            _suspended = false;
        }
    }

    // --- Applying -----------------------------------------------------------------------------

    // Called by MotionSmoothingController when Madeline enters it.
    public static void Apply(MapSmoothingSuggestion suggestion)
    {
        // Remembered even when the player has map settings turned off, so that turning them back
        // on picks up what the map asked for rather than waiting for another trigger.
        _lastSuggestion = suggestion.Clone();

        if (!MotionSmoothingModule.Settings.UseMapSettings) return;

        var wasEnabled = MotionSmoothingModule.Settings.Enabled;

        // A separate copy, so that whatever happens to what's in force can't eat into the record
        // of what the map asked for.
        _active = _lastSuggestion.Clone();

        Refresh(wasEnabled);
    }

    // Turns requests that couldn't have any effect back into "no preference": with Motion
    // Smoothing requested off nothing else does anything, and the three Fancy-only options do
    // nothing under Fast or Off camera smoothing. Applied to a controller's request once, when
    // it's read, so that nothing downstream has to think about it -- including the menu, which
    // would otherwise show an option as locked by a map that isn't really deciding it.
    public static void DropInapplicable(MapSmoothingSuggestion suggestion)
    {
        if (suggestion.Enabled == false)
        {
            suggestion.Clear(MapSmoothingOption.CameraSmoothingMode);
            foreach (var option in FancyOnlyOptions) suggestion.Clear(option);
            return;
        }

        if (suggestion.CameraSmoothingMode is UnlockCameraStrategy.Unlock or UnlockCameraStrategy.Off)
            foreach (var option in FancyOnlyOptions) suggestion.Clear(option);
    }

    // Toggling Use Suggested Map Settings takes effect immediately rather than at the next map:
    // off hands every setting straight back to the player, and on puts the map's last suggestion
    // back in force instead of waiting for them to walk through the trigger again.
    public static void UseMapSettingsChanged()
    {
        if (!MotionSmoothingModule.Settings.UseMapSettings)
        {
            Clear();
            return;
        }

        if (_lastSuggestion != null) Apply(_lastSuggestion);
    }

    private static void LevelExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session,
        HiresSnow snow)
    {
        // Leaving the map forgets what it asked for, so toggling the option elsewhere can't
        // resurrect it.
        _lastSuggestion = null;

        Clear();
    }

    private static void Clear()
    {
        if (_active.IsEmpty) return;

        var wasEnabled = MotionSmoothingModule.Settings.Enabled;

        _active = new MapSmoothingSuggestion();

        Refresh(wasEnabled);
    }

    private static void Refresh(bool wasEnabled)
    {
        MotionSmoothingModule.Instance.ApplySettings();

        var isEnabled = MotionSmoothingModule.Settings.Enabled;
        if (isEnabled != wasEnabled)
            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(isEnabled));
    }
}
