using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.MotionSmoothing.Smoothing.Targets;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MotionSmoothing.Maps;

// A styleground can be flagged hires in the editor, which says its art is already drawn at the
// scale Fancy mode composites at rather than at 320x180. Motion Smoothing then draws it into the
// large buffer at its own resolution instead of nearest-neighbour upscaling a small one, so a
// mapper can hand-draw a sky, a gradient or a vignette that stays sharp however far the camera is
// from a whole pixel.
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
    private static readonly ConditionalWeakTable<Backdrop, object> GameSpaceViews = new();

    public static void Load()
    {
        var parseBackdrop = typeof(MapData).GetMethod("ParseBackdrop", MotionSmoothingModule.AllFlags);

        if (parseBackdrop == null)
        {
            Logger.Log(LogLevel.Warn, nameof(MotionSmoothingModule),
                "Couldn't find MapData.ParseBackdrop; hires stylegrounds will be ignored.");
            return;
        }

        MotionSmoothingModule.TryDisableInlining(parseBackdrop);
        _parseBackdropHook = new Hook(parseBackdrop, ParseBackdropHook);
    }

    public static void Unload()
    {
        _parseBackdropHook?.Dispose();
        _parseBackdropHook = null;
    }

    public static bool IsHires(Backdrop backdrop) =>
        backdrop != null && Flagged.TryGetValue(backdrop, out _);

    // Whether this backdrop is a Parallax whose MTexture was replaced with the game-space view
    // below. That view carries a 1/6 ScaleFix, which has to come back off when it is drawn hires.
    public static bool HasGameSpaceView(Backdrop backdrop) =>
        backdrop != null && GameSpaceViews.TryGetValue(backdrop, out _);

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

        // A Parallax is only hires if it can be given the game-space view below, and if its art
        // isn't the right shape for one it isn't hires after all: drawing an ordinary 320x180
        // styleground into the large buffer at 1:1 would leave it a sixth of the size it should be.
        // Other backdrop types draw whatever they like and are taken at their word.
        if (backdrop is Parallax && !ApplyGameSpaceView(backdrop)) return backdrop;

        Flagged.AddOrUpdate(backdrop, null);

        return backdrop;
    }

    // Parallax does all of its own arithmetic -- where it scrolls to, where it wraps, how wide a
    // slice of the texture to ask for -- in 320x180 game pixels, off MTexture.Width and Height. A
    // 1920x1080 asset handed to it as-is would tile six times too far apart and be cut off at a
    // sixth of its width, so it gets an MTexture that *reports* the game-space size while still
    // pointing at the whole hires image.
    //
    // The 1/6 ScaleFix that comes with it is what makes the styleground still look right when
    // nothing is drawing it hires: with the mod off, under Fast or Off, or when another mod's
    // renderer (StylegroundMasks, say) draws it into a buffer of its own. In all of those it lands
    // at its game-space size, just downsampled. Fancy mode takes that scale back off -- see
    // HiresCameraSmoother.SpriteBatch_Draw3 -- when it draws the art into the large buffer at 1:1.
    private static bool ApplyGameSpaceView(Backdrop backdrop)
    {
        if (backdrop is not Parallax parallax || parallax.Texture is not { } texture) return false;

        var scale = (int)HiresCameraSmoother.Scale;

        if (texture.Width % scale != 0 || texture.Height % scale != 0)
        {
            Logger.Log(LogLevel.Warn, nameof(MotionSmoothingModule),
                $"Styleground texture {texture.AtlasPath ?? "(unnamed)"} is flagged hires but is " +
                $"{texture.Width}x{texture.Height}, which isn't a multiple of {scale}. " +
                "Drawing it as an ordinary styleground.");

            return false;
        }

        parallax.Texture = new MTexture(
            texture,
            null,
            new Rectangle(0, 0, texture.Width, texture.Height),
            Vector2.Zero,
            texture.Width / scale,
            texture.Height / scale
        ) {
            ScaleFix = 1f / scale
        };

        GameSpaceViews.AddOrUpdate(backdrop, null);

        return true;
    }
}
