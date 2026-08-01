using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

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

// What a single map asks for. Every field is null when the map has no preference for it.
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
}

// Map-suggested settings, modeled on how ExtendedVariantMode picks its map defaults up: the map
// data is scanned for a controller entity before the level is built, rather than waiting for the
// entity to be instantiated in some room.
//
// A suggestion is applied as an *override layer* rather than by writing to the settings. The
// user's own saved values are never touched, which means (a) leaving the map restores them with
// no bookkeeping, and (b) a crash inside a map can't leave the player's settings rewritten. The
// settings getters consult this class, so every existing read of Settings.Enabled,
// Settings.UnlockCameraStrategy and friends transparently sees the map's value, and the settings
// *setters* drop the override, so a player who changes an option themselves takes control back.
public static class MapSmoothingSuggestions
{
    public const string ControllerEntityName = "MotionSmoothing/MotionSmoothingController";

    private const string ValueOn = "On";
    private const string ValueOff = "Off";
    private const string ValueFancy = "Fancy";
    private const string ValueFast = "Fast";

    private static readonly Dictionary<MapSmoothingOption, string> Attributes = new()
    {
        [MapSmoothingOption.Enabled] = "motionSmoothing",
        [MapSmoothingOption.SmoothBackground] = "smoothBackground",
        [MapSmoothingOption.SmoothForeground] = "smoothForeground",
        [MapSmoothingOption.RenderMadelineWithSubpixelPrecision] = "renderMadelineWithSubpixels",
        [MapSmoothingOption.CameraSmoothingMode] = "cameraSmoothingMode"
    };

    // Dialog key prefixes for the postcard; "_ON" or "_OFF" is appended for the boolean options.
    private static readonly Dictionary<MapSmoothingOption, string> PostcardKeys = new()
    {
        [MapSmoothingOption.Enabled] = "MOTIONSMOOTHING_POSTCARD_SMOOTHING",
        [MapSmoothingOption.SmoothBackground] = "MOTIONSMOOTHING_POSTCARD_BACKGROUND",
        [MapSmoothingOption.SmoothForeground] = "MOTIONSMOOTHING_POSTCARD_FOREGROUND",
        [MapSmoothingOption.RenderMadelineWithSubpixelPrecision] = "MOTIONSMOOTHING_POSTCARD_SUBPIXELS"
    };

    // The order the three Fancy-only options are listed on the postcard.
    private static readonly MapSmoothingOption[] FancyOnlyOptions =
    {
        MapSmoothingOption.SmoothBackground,
        MapSmoothingOption.SmoothForeground,
        MapSmoothingOption.RenderMadelineWithSubpixelPrecision
    };

    // The SID the current override was computed for. Kept so that reloads *within* a map (retry
    // from the pause menu, which rebuilds the LevelLoader) don't re-assert a suggestion the
    // player has since overridden by hand.
    private static string _appliedSid;

    // The answer the player gave on the postcard, and the SID they gave it for. Static rather than
    // per-Session so that a chapter restart -- which builds a fresh Session out of Session.Restart
    // and never passes back through LevelEnter -- keeps it. Rewritten every time the postcard asks,
    // so there's nothing to expire: another map's SID simply won't match.
    private static string _answeredSid;
    private static bool _accepted;

    // Where the answer is stashed so it survives quitting the game. Vanilla serializes
    // Session.Flags into the save file, and a Session started fresh from the chapter select has
    // none of them -- which is exactly when the player should be asked again. Two flags rather
    // than one so that "never asked" is distinguishable from "asked, and said no": a session saved
    // before the map grew a controller (or before this mod had one) still gets the prompt.
    private const string AnsweredFlag = "MotionSmoothing/SuggestionAnswered";
    private const string AnswerFlag = "MotionSmoothing/UseSuggestedSettings";

    private static MapSmoothingSuggestion _active = new();

    // Set while Everest serializes our settings. The YAML serializer reads the same property
    // getters the game does, so the override has to step aside or it would be written to disk as
    // if it were the player's own choice.
    private static bool _suspended;

    public static void Load()
    {
        MotionSmoothingModule.DisableInliningConstructor(typeof(LevelLoader), typeof(Session), typeof(Vector2?));
        MotionSmoothingModule.DisableInlining(typeof(LevelEnter), "Routine");
        MotionSmoothingModule.DisableInlining(typeof(LevelEnter), "BeforeRender");

        On.Celeste.LevelLoader.ctor += LevelLoaderCtorHook;
        On.Celeste.LevelEnter.Routine += LevelEnterRoutineHook;
        On.Celeste.LevelEnter.BeforeRender += LevelEnterBeforeRenderHook;
        Everest.Events.Level.OnExit += LevelExit;
    }

    public static void Unload()
    {
        On.Celeste.LevelLoader.ctor -= LevelLoaderCtorHook;
        On.Celeste.LevelEnter.Routine -= LevelEnterRoutineHook;
        On.Celeste.LevelEnter.BeforeRender -= LevelEnterBeforeRenderHook;
        Everest.Events.Level.OnExit -= LevelExit;

        // Drop the override without re-applying settings: by the time the module unloads, every
        // smoothing feature has already been torn down and must stay that way.
        Reset();
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

    public static void UserChanged(MapSmoothingOption option) => _active.Clear(option);

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

    // --- Reading the map ----------------------------------------------------------------------

    // Reads what the map's controllers ask for.
    public static MapSmoothingSuggestion Read(Session session)
    {
        var suggestion = new MapSmoothingSuggestion();

        var mapData = GetMapData(session);
        if (mapData?.Levels == null) return suggestion;

        foreach (var levelData in mapData.Levels)
        {
            if (levelData?.Entities == null) continue;

            foreach (var entityData in levelData.Entities)
            {
                if (entityData?.Name != ControllerEntityName) continue;

                // Multiple controllers are legal (a mapper might put one in each room while
                // building). The first explicit preference for an option wins.
                suggestion.Enabled ??= ParseBoolean(entityData, MapSmoothingOption.Enabled);
                suggestion.SmoothBackground ??= ParseBoolean(entityData, MapSmoothingOption.SmoothBackground);
                suggestion.SmoothForeground ??= ParseBoolean(entityData, MapSmoothingOption.SmoothForeground);
                suggestion.RenderMadelineWithSubpixelPrecision ??=
                    ParseBoolean(entityData, MapSmoothingOption.RenderMadelineWithSubpixelPrecision);
                suggestion.CameraSmoothingMode ??= ParseCameraSmoothing(entityData);
            }
        }

        return suggestion;
    }

    // Anything that isn't explicitly On or Off -- including the "NoPreference" the editors write,
    // a missing attribute, and a controller saved by an older version of the mod -- means the map
    // doesn't care about that option.
    private static bool? ParseBoolean(EntityData entityData, MapSmoothingOption option) =>
        entityData.Attr(Attributes[option]) switch
        {
            ValueOn => true,
            ValueOff => false,
            _ => null
        };

    private static UnlockCameraStrategy? ParseCameraSmoothing(EntityData entityData) =>
        entityData.Attr(Attributes[MapSmoothingOption.CameraSmoothingMode]) switch
        {
            ValueFancy => UnlockCameraStrategy.Hires,
            ValueFast => UnlockCameraStrategy.Unlock,
            ValueOff => UnlockCameraStrategy.Off,
            _ => null
        };

    // Session.MapData indexes straight into AreaData.Areas, which throws for a map that isn't
    // there any more (Everest shows its own "level gone" postcard for that case).
    private static MapData GetMapData(Session session)
    {
        if (session == null) return null;
        if (AreaData.Get(session) is not { } areaData) return null;

        var mode = (int)session.Area.Mode;
        if (areaData.Mode == null || areaData.Mode.Length <= mode) return null;

        return areaData.Mode[mode]?.MapData;
    }

    // --- Applying -----------------------------------------------------------------------------

    private static void LevelLoaderCtorHook(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self,
        Session session, Vector2? startPosition)
    {
        ApplyFor(session);
        orig(self, session, startPosition);
    }

    private static void LevelExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session,
        HiresSnow snow)
    {
        Clear();
    }

    private static void ApplyFor(Session session)
    {
        var sid = session?.Area.GetSID();

        // Reloading the same map (a pause-menu retry) keeps whatever is currently in force, so a
        // player who turned the map's suggestion back off doesn't get it re-applied under them.
        if (sid != null && sid == _appliedSid) return;

        var wasEnabled = MotionSmoothingModule.Settings.Enabled;
        var answered = sid != null && sid == _answeredSid;

        Reset();
        _appliedSid = sid;

        // Apply unless the player turned this map's settings down. Paths that never show a
        // postcard -- the console `load` command, mainly -- have no answer and fall through to
        // applying.
        if (!answered || _accepted) _active = Read(session);

        // Re-stamp the answer: a chapter restart hands us a brand new Session, and Session.Restart
        // doesn't carry flags across, so without this a save-and-quit after a restart would forget
        // what the player said.
        if (answered) RememberAnswer(session, _accepted);

        Refresh(wasEnabled);
    }

    private static void Clear()
    {
        var wasEnabled = MotionSmoothingModule.Settings.Enabled;

        Reset();

        Refresh(wasEnabled);
    }

    // AnsweredFlag is what tells a session that was asked apart from one that never was, so it goes
    // on unconditionally; only AnswerFlag carries the answer itself.
    private static void RememberAnswer(Session session, bool accepted)
    {
        session.SetFlag(AnsweredFlag);
        session.SetFlag(AnswerFlag, accepted);
    }

    private static void Reset()
    {
        _active = new MapSmoothingSuggestion();
        _appliedSid = null;
    }

    private static void Refresh(bool wasEnabled)
    {
        MotionSmoothingModule.Instance.ApplySettings();

        var isEnabled = MotionSmoothingModule.Settings.Enabled;
        if (isEnabled != wasEnabled)
            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(isEnabled));
    }

    // --- The postcard -------------------------------------------------------------------------

    private static IEnumerator LevelEnterRoutineHook(On.Celeste.LevelEnter.orig_Routine orig, LevelEnter self)
    {
        // Vanilla diverts to an error postcard in these cases; don't stack ours on top of it.
        if (LevelEnter.ErrorMessage != null || AreaData.Get(self.session) == null) return orig(self);

        // Continuing a saved session the player has already answered for: pick that answer back up
        // instead of asking again. Save and quit is the only way here -- every other route to a map
        // builds a fresh Session, which has no flags and so gets asked. A saved session that was
        // never asked (one from before the map or the mod had a controller) falls through and is.
        if (self.fromSaveData && self.session.GetFlag(AnsweredFlag))
        {
            _answeredSid = self.session.Area.GetSID();
            _accepted = self.session.GetFlag(AnswerFlag);
            return orig(self);
        }

        var message = GetPostcardMessage(Read(self.session));
        return message == null ? orig(self) : PostcardRoutine(orig, self, message);
    }

    // LevelEnter.BeforeRender would call Postcard.BeforeRender, which isn't virtual and draws the
    // message at vanilla's larger scale. Hide ours from it and render it ourselves instead.
    private static void LevelEnterBeforeRenderHook(On.Celeste.LevelEnter.orig_BeforeRender orig, LevelEnter self)
    {
        if (self.postcard is not MotionSmoothingPostcard ours)
        {
            orig(self);
            return;
        }

        self.postcard = null;
        orig(self);
        self.postcard = ours;

        ours.BeforeRender();
    }

    private static IEnumerator PostcardRoutine(On.Celeste.LevelEnter.orig_Routine orig, LevelEnter self,
        string message)
    {
        yield return 1f;

        var postcard = new MotionSmoothingPostcard(message);

        // LevelEnter renders the postcard's text through this field, so it has to be set for the
        // card to come out with anything on it. Vanilla's own postcard routines do exactly the
        // same thing, and orig_Routine overwrites it if the map has a postcard too.
        self.postcard = postcard;
        self.Add(postcard);

        yield return postcard.PromptRoutine();

        // ApplyFor runs next, from the LevelLoader constructor, and reads both of these. The flags
        // ride along in the session so that saving and quitting, then continuing, doesn't ask
        // again -- see the fromSaveData branch in LevelEnterRoutineHook.
        _answeredSid = self.session.Area.GetSID();
        _accepted = postcard.Accepted;
        RememberAnswer(self.session, _accepted);

        // Hand off to the vanilla routine, which shows the map's own postcard (if it has one) and
        // then starts the LevelLoader.
        var inner = orig(self);
        while (inner.MoveNext()) yield return inner.Current;
    }

    // The message to show before entering this map, or null if the map has nothing to say that
    // isn't already how the player has things set up. Called before the LevelLoader exists, so
    // the settings still read back as the player's own.
    private static string GetPostcardMessage(MapSmoothingSuggestion suggestion)
    {
        if (suggestion.IsEmpty) return null;

        var settings = MotionSmoothingModule.Settings;
        var changes = new List<string>();

        AddChange(changes, suggestion, MapSmoothingOption.Enabled);

        // Nothing below this point does anything unless smoothing ends up on, so don't offer to
        // change settings the player would see no difference from.
        if (suggestion.Enabled ?? settings.UserEnabled)
        {
            if (suggestion.CameraSmoothingMode is { } camera && camera != settings.UserUnlockCameraStrategy)
                changes.Add(Dialog.Get(CameraPostcardKey(camera)));

            // ...and these three only do anything under Fancy camera smoothing.
            var fancy = (suggestion.CameraSmoothingMode ?? settings.UserUnlockCameraStrategy) ==
                        UnlockCameraStrategy.Hires;

            if (fancy)
                foreach (var option in FancyOnlyOptions)
                    AddChange(changes, suggestion, option);
        }

        if (changes.Count == 0) return null;

        return Dialog.Get("MOTIONSMOOTHING_POSTCARD_PROMPT")
            .Replace("((changes))", string.Join("{n}", changes));
    }

    private static string CameraPostcardKey(UnlockCameraStrategy camera) => camera switch
    {
        UnlockCameraStrategy.Hires => "MOTIONSMOOTHING_POSTCARD_CAMERA_FANCY",
        UnlockCameraStrategy.Unlock => "MOTIONSMOOTHING_POSTCARD_CAMERA_FAST",
        _ => "MOTIONSMOOTHING_POSTCARD_CAMERA_OFF"
    };

    private static void AddChange(List<string> changes, MapSmoothingSuggestion suggestion,
        MapSmoothingOption option)
    {
        if (suggestion.GetBoolean(option) is not { } wanted) return;
        if (wanted == GetUserValue(option)) return;

        changes.Add(Dialog.Get(PostcardKeys[option] + (wanted ? "_ON" : "_OFF")));
    }

    // The player's own saved value for an option, ignoring any override in force.
    private static bool GetUserValue(MapSmoothingOption option)
    {
        var settings = MotionSmoothingModule.Settings;

        return option switch
        {
            MapSmoothingOption.Enabled => settings.UserEnabled,
            MapSmoothingOption.SmoothBackground => settings.UserRenderBackgroundHires,
            MapSmoothingOption.SmoothForeground => settings.UserRenderForegroundHires,
            MapSmoothingOption.RenderMadelineWithSubpixelPrecision => settings.UserRenderMadelineWithSubpixels,
            _ => false
        };
    }
}
