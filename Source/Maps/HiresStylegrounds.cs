using System;
using System.Collections;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Maps;

// A styleground can be flagged hires in the editor, which says its art is already drawn at the
// scale Fancy mode composites at rather than at 320x180. Motion Smoothing then draws it into the
// large buffer at its own resolution, un-upscaled, so a mapper can hand-draw a sky, a gradient or a
// vignette that stays sharp however far the camera is from a whole pixel.
//
// Only Fancy mode has a buffer big enough to draw one into. The game-space view below means a
// styleground still comes out at the right size in the other modes -- downsampled, the way it would
// look if the mapper had drawn it at 320x180 in the first place -- but that throws the whole point
// of it away, so a map that has one forces Fancy on for as long as the player is in it. The forcing
// works the same way auspicioushelper's material layers force Fancy *off*
// (see MotionSmoothingSettings.IsAuspiciousHelperLoaded): the setting's getters report the forced
// value while the map is loaded and the player's own is untouched underneath, rather than going
// through the map-suggestion layer, which is for things a map is merely asking for.
//
// The two forces are irreconcilable, so a map that would trip both is refused outright with a
// postcard rather than being played with one of them quietly losing.
//
// The flag rides on the map data rather than on the mod's settings: it describes the art, not
// something a player would choose. Read once, when the map is parsed -- so this is hooked for the
// life of the mod (see MotionSmoothingModule.Load) rather than with the Fancy renderer, which comes
// and goes with the setting. HiresCameraSmoother is what acts on it.
public static class HiresStylegrounds
{
    // Namespaced, because this goes into the same attribute bag as every other styleground
    // property and nothing else is going to reserve a name this specific.
    public const string Attribute = "motionSmoothingHires";

    private static Hook _parseBackdropHook;

    // Weakly keyed: a map's MapData owns these Backdrops, and nothing here should be what keeps
    // one alive.
    private static readonly ConditionalWeakTable<Backdrop, object> Flagged = new();

    // The factor Fancy mode multiplies a hires Parallax's drawn scale by, boxed once here rather
    // than recomputed per draw. See ApplyGameSpaceView.
    private static readonly ConditionalWeakTable<Backdrop, object> ScaleCorrections = new();

    // Whether the level the player is in has a hires styleground anywhere in it. Read by the
    // settings on every access, so it's a field rather than a walk of the backdrop lists.
    private static bool _currentLevelHasHires;

    // The message the postcard is to show, handed from the refusal to the LevelEnter it creates.
    private static string _refusalMessage;

    public static void Load()
    {
        var parseBackdrop = typeof(MapData).GetMethod("ParseBackdrop", MotionSmoothingModule.AllFlags);

        if (parseBackdrop == null)
        {
            Logger.Log(LogLevel.Warn, nameof(MotionSmoothingModule),
                "Couldn't find MapData.ParseBackdrop; hires stylegrounds will be ignored.");
        }

        else
        {
            MotionSmoothingModule.TryDisableInlining(parseBackdrop);
            _parseBackdropHook = new Hook(parseBackdrop, ParseBackdropHook);
        }

        MotionSmoothingModule.DisableInlining(typeof(LevelEnter), "Routine");
        MotionSmoothingModule.DisableInlining(typeof(LevelEnter), "BeforeRender");

        On.Celeste.LevelEnter.Routine += LevelEnterRoutineHook;
        On.Celeste.LevelEnter.BeforeRender += LevelEnterBeforeRenderHook;

        Everest.Events.Level.OnLoadLevel += LevelLoadLevel;
        Everest.Events.Level.OnExit += LevelExit;
    }

    public static void Unload()
    {
        _parseBackdropHook?.Dispose();
        _parseBackdropHook = null;

        On.Celeste.LevelEnter.Routine -= LevelEnterRoutineHook;
        On.Celeste.LevelEnter.BeforeRender -= LevelEnterBeforeRenderHook;

        Everest.Events.Level.OnLoadLevel -= LevelLoadLevel;
        Everest.Events.Level.OnExit -= LevelExit;

        _currentLevelHasHires = false;
    }

    public static bool IsHires(Backdrop backdrop) =>
        backdrop != null && Flagged.TryGetValue(backdrop, out _);

    // What to multiply this styleground's drawn scale by to undo the ScaleFix its game-space view
    // carries, so its art lands at one texel per hires pixel. 1 for anything that hasn't got one --
    // a backdrop type other than Parallax, which draws whatever it likes at whatever size it likes.
    public static float ScaleCorrection(Backdrop backdrop) =>
        backdrop != null && ScaleCorrections.TryGetValue(backdrop, out var correction)
            ? (float)correction
            : 1f;

    // Whether the level the player is in has to be rendered in Fancy mode. The settings getters
    // consult this, so every existing read of Settings.Enabled and Settings.RenderingMode sees the
    // forced value; the menu says why, and the hotkey refuses to cycle past it.
    //
    // The scene check is what makes this self-correcting. LevelExit clears the flag on every
    // ordinary way out of a map, but a route that skips it would otherwise leave the overworld's
    // own Rendering Mode option pinned to Fancy, explained by a map the player is no longer in.
    public static bool RequiresFancy => _currentLevelHasHires && Engine.Scene is Level;

    // --- Reading the flag ----------------------------------------------------------------------

    private delegate Backdrop orig_ParseBackdrop(MapData self, BinaryPacker.Element child,
        BinaryPacker.Element above);

    private static Backdrop ParseBackdropHook(orig_ParseBackdrop orig, MapData self,
        BinaryPacker.Element child, BinaryPacker.Element above)
    {
        var backdrop = orig(self, child, above);

        if (backdrop == null) return null;

        // Child first, then the enclosing <apply>, which is the precedence every other styleground
        // attribute is read with -- so a whole group can be flagged in one place.
        bool hires = child.HasAttr(Attribute)
            ? child.AttrBool(Attribute)
            : above != null && above.HasAttr(Attribute) && above.AttrBool(Attribute);

        if (!hires) return backdrop;

        // A Parallax is only hires if it can be given the game-space view below; if it can't, it
        // isn't hires after all, because its own arithmetic would be six times out.
        if (backdrop is Parallax && !ApplyGameSpaceView(backdrop)) return backdrop;

        Flagged.AddOrUpdate(backdrop, null);

        return backdrop;
    }

    // Parallax does all of its own arithmetic -- where it scrolls to, where it wraps, how wide a
    // slice of the texture to ask for -- in 320x180 game pixels, off MTexture.Width and Height. A
    // 1920x1080 asset handed to it as-is would tile six times too far apart and be cut off at a
    // sixth of its width, so it gets an MTexture that *reports* the game-space size while still
    // pointing at the whole hires image. Looping, wrapping and scrolling then all behave exactly as
    // they would for the 320x180 styleground the mapper would otherwise have drawn.
    //
    // The ScaleFix that comes with it is what draws the art at that game-space size everywhere the
    // hires path isn't running: another mod's renderer pulling the styleground into a buffer of its
    // own, or the moment SpeedrunTool forces the mod off to restore a state. Fancy mode multiplies
    // that scale back out -- see HiresCameraSmoother.SpriteBatch_Draw3 -- when it draws the art into
    // the large buffer at 1:1.
    private static bool ApplyGameSpaceView(Backdrop backdrop)
    {
        if (backdrop is not Parallax parallax || parallax.Texture is not { } texture) return false;

        // Rounded rather than required to divide exactly. There's no reason a mapper's image has to
        // be a whole number of game pixels across, and whatever it rounds to is simply the size the
        // styleground behaves as; the correction below keeps the art itself at one texel per hires
        // pixel either way. A looping texture whose width isn't a multiple of the scale can leave up
        // to half a game pixel of seam between tiles, which is the only thing the rounding costs.
        var width = Math.Max(1, (int)Math.Round(texture.Width / HiresCameraSmoother.Scale));
        var height = Math.Max(1, (int)Math.Round(texture.Height / HiresCameraSmoother.Scale));

        // A view that reports the size it already is isn't a view at all, and Parallax would take
        // its other render path for one -- which measures its source rectangle in game pixels
        // rather than texels, and so needs different handling. Only an image a few pixels across
        // can round back to itself, so this is a guard rather than a real restriction.
        if (width == texture.Width && height == texture.Height)
        {
            Logger.Log(LogLevel.Warn, nameof(MotionSmoothingModule),
                $"Styleground texture {texture.AtlasPath ?? "(unnamed)"} is flagged hires but is " +
                $"only {texture.Width}x{texture.Height}. Drawing it as an ordinary styleground.");

            return false;
        }

        var view = new MTexture(texture, null, new Rectangle(0, 0, texture.Width, texture.Height),
            Vector2.Zero, width, height);

        // What the view reports over what it actually holds. ClipRect rather than Width because
        // this is the factor MTexture.Draw applies to the rectangle it hands SpriteBatch, and that
        // rectangle is in texels; the two only differ if the styleground's texture is itself a view
        // of a bigger one, which a hand-drawn hires backdrop won't be. The setter writes the factor
        // relative to the parent's own ScaleFix, which the getter multiplies back in.
        var gameSpaceScale = width / (float)view.ClipRect.Width;
        view.ScaleFix = gameSpaceScale / texture.ScaleFix;

        parallax.Texture = view;

        ScaleCorrections.AddOrUpdate(backdrop, 1f / gameSpaceScale);

        return true;
    }

    // --- Forcing Fancy, and refusing the map that can't have it --------------------------------

    private static void LevelLoadLevel(Level level, Player.IntroTypes playerIntro, bool isFromLoader)
    {
        // Recomputed per room rather than cached per map: the lists are short, and a mod that adds
        // or removes a styleground partway through a map is then accounted for.
        _currentLevelHasHires = HasHires(level.Background) || HasHires(level.Foreground);

        if (!_currentLevelHasHires || !MotionSmoothingSettings.IsAuspiciousHelperLoaded) return;

        // Both forces are in play and they point opposite ways. Checked here rather than before the
        // map loads because whether auspicioushelper has a material layer active is only knowable
        // once its entities exist -- which also means a map whose layers only appear partway
        // through is refused at the room they appear in rather than at its first.
        Logger.Log(LogLevel.Warn, nameof(MotionSmoothingModule),
            $"{level.Session.Area.GetSID()} has a hires styleground and an active auspicioushelper " +
            "material layer, which need Fancy mode on and off at the same time. Refusing the map.");

        Refuse(level.Session);
    }

    private static void LevelExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session,
        HiresSnow snow)
    {
        _currentLevelHasHires = false;
    }

    private static bool HasHires(BackdropRenderer renderer)
    {
        if (renderer?.Backdrops == null) return false;

        foreach (var backdrop in renderer.Backdrops)
            if (IsHires(backdrop))
                return true;

        return false;
    }

    // Hands the player straight back to a LevelEnter, which shows the postcard below and then
    // returns them to the overworld. Nothing of the level is drawn in between: this runs inside
    // Level.LoadLevel, while the screen wipe that brought us here is still covering everything.
    private static void Refuse(Session session)
    {
        _currentLevelHasHires = false;
        // ((player)) is substituted by whoever shows the card, not by Dialog -- the same as
        // LevelEnter does for its own error postcards.
        _refusalMessage = Dialog.Get("MOTIONSMOOTHING_HIRES_AUSPICIOUSHELPER_POSTCARD")
            .Replace("((player))", SaveData.Instance?.Name ?? Dialog.Clean("FILE_DEFAULT"));

        Engine.Scene = LevelEnter.ForceCreate(session, false);
    }

    // --- The postcard --------------------------------------------------------------------------

    private static IEnumerator LevelEnterRoutineHook(On.Celeste.LevelEnter.orig_Routine orig, LevelEnter self)
    {
        if (_refusalMessage == null) return orig(self);

        var message = _refusalMessage;
        _refusalMessage = null;

        return RefusalRoutine(self, message);
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

    // LevelEnter.ErrorRoutine, with our own card. Not routed through LevelEnter.ErrorMessage
    // because that one builds a vanilla Postcard, which draws this much text off the bottom edge.
    private static IEnumerator RefusalRoutine(LevelEnter self, string message)
    {
        Audio.SetMusic(null);
        Audio.SetAmbience(null);

        yield return 1f;

        var postcard = new MotionSmoothingPostcard(message);

        // LevelEnter renders the postcard's text through this field, so it has to be set for the
        // card to come out with anything on it. Vanilla's own postcard routines do the same.
        self.postcard = postcard;
        self.Add(postcard);

        yield return postcard.DisplayRoutine();

        var session = new Session(AreaData.Get(self.session) != null
            ? self.session.Area
            : new AreaKey(1).SetSID(""));

        SaveData.Instance.CurrentSession_Safe = session;
        SaveData.Instance.LastArea_Safe = session.Area;

        Engine.Scene = new OverworldLoader(Overworld.StartMode.AreaQuit);
    }
}
