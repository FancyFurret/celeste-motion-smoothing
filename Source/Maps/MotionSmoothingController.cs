using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Maps;

// A trigger a mapper drops in their map to suggest Motion Smoothing settings for it. It applies
// them when Madeline enters, so a mapper can decide where in the map the settings should take hold
// -- the room they matter in, a checkpoint, or the whole spawn area.
//
// The settings are only ever an override layer: they last until the player leaves the map, and they
// don't touch anything the player has saved. See MapSmoothingSuggestions.
//
// There are two of these. MotionSmoothingController2 below is the one the editors offer, and it
// names the settings the mod settings show. MotionSmoothingControllerV1 is the original, from when
// Motion Smoothing had an on/off toggle and a three-way Camera Smoothing setting rather than
// Rendering Mode and a Camera Smoothing toggle; it stays loadable so maps that already place it
// keep working, and translates its own settings into today's.
public abstract class MotionSmoothingControllerBase : Trigger
{
    protected const string ValueOn = "On";
    protected const string ValueOff = "Off";
    protected const string ValueFancy = "Fancy";
    protected const string ValueFast = "Fast";

    private MapSmoothingSuggestion _suggestion;

    protected MotionSmoothingControllerBase(EntityData data, Vector2 offset) : base(data, offset)
    {
    }

    // Called by the subclass constructor once it has read its own attributes. Not done here off a
    // virtual call, which would run before the subclass's own constructor body.
    protected void SetSuggestion(MapSmoothingSuggestion suggestion)
    {
        MapSmoothingSuggestions.DropInapplicable(suggestion);

        _suggestion = suggestion;
    }

    // Trigger fires this once as Madeline enters rather than every frame she's inside, so nothing
    // here fights a player who changes a setting from the pause menu while standing in it. Walking
    // out and back in applies it again.
    public override void OnEnter(Player player)
    {
        base.OnEnter(player);

        MapSmoothingSuggestions.Apply(_suggestion);
    }

    // Anything that isn't explicitly On or Off -- including the "NoPreference" the editors write,
    // a missing attribute, and a controller saved by an older version of the mod -- means the map
    // doesn't care about that option.
    protected static bool? ParseBoolean(string value) => value switch
    {
        ValueOn => true,
        ValueOff => false,
        _ => null
    };

    // The framerate is written as a string rather than a number so that "NoPreference" can be one
    // of the values a mapper picks -- so anything that isn't an integer means the map doesn't care.
    // Whatever they do ask for is used exactly, including framerates the in-game slider would never
    // stop on (24) or reach (3). Only zero and negatives are refused, having no meaning as a
    // framerate at all.
    protected static int? ParseFrameRate(string value)
    {
        if (!int.TryParse(value, out var frameRate)) return null;

        return frameRate >= 1 ? frameRate : null;
    }
}

// The controller mappers place today. Its options are the ones in Mod Settings.
[CustomEntity(MapSmoothingSuggestions.ControllerV2EntityName)]
public class MotionSmoothingController2 : MotionSmoothingControllerBase
{
    public MotionSmoothingController2(EntityData data, Vector2 offset) : base(data, offset)
    {
        var renderingMode = ParseRenderingMode(data.Attr("renderingMode"));

        SetSuggestion(new MapSmoothingSuggestion
        {
            // Rendering Mode is the master switch and the choice of renderer in one, so it splits
            // back into the two the override layer is written in terms of -- exactly the way
            // MotionSmoothingSettings.RenderingMode does. Off names no renderer: with the mod off
            // there is nothing for one to do.
            Enabled = renderingMode.HasValue ? renderingMode.Value != RenderingMode.Off : null,
            CameraStrategy = renderingMode switch
            {
                RenderingMode.Fancy => UnlockCameraStrategy.Hires,
                RenderingMode.Fast => UnlockCameraStrategy.Unlock,
                _ => null
            },
            CameraSmoothing = ParseBoolean(data.Attr("cameraSmoothing")),
            SmoothBackground = ParseBoolean(data.Attr("smoothBackground")),
            SmoothForeground = ParseBoolean(data.Attr("smoothForeground")),
            RenderMadelineWithSubpixelPrecision = ParseBoolean(data.Attr("renderMadelineWithSubpixels")),
            FrameRate = ParseFrameRate(data.Attr("frameRate"))
        });
    }

    private static RenderingMode? ParseRenderingMode(string value) => value switch
    {
        ValueFancy => RenderingMode.Fancy,
        ValueFast => RenderingMode.Fast,
        ValueOff => RenderingMode.Off,
        _ => null
    };
}

// The original controller. No editor offers it any more, so nothing new can place one, but maps
// that already have one keep working.
[CustomEntity(MapSmoothingSuggestions.ControllerEntityName)]
public class MotionSmoothingControllerV1 : MotionSmoothingControllerBase
{
    public MotionSmoothingControllerV1(EntityData data, Vector2 offset) : base(data, offset)
    {
        var cameraSmoothingMode = ParseCameraSmoothing(data.Attr("cameraSmoothingMode"));

        SetSuggestion(new MapSmoothingSuggestion
        {
            Enabled = ParseBoolean(data.Attr("motionSmoothing")),

            // The old three-way Camera Smoothing setting is today's pair: Fancy and Fast pick the
            // renderer, and its Off is Fast with the camera left on the pixel grid, which is what
            // that setting did.
            CameraStrategy = cameraSmoothingMode switch
            {
                UnlockCameraStrategy.Hires => UnlockCameraStrategy.Hires,
                UnlockCameraStrategy.Unlock or UnlockCameraStrategy.Off => UnlockCameraStrategy.Unlock,
                _ => null
            },

            // Only its Off has anything to say about the toggle. Fancy and Fast leave it to the
            // player: a map written before the toggle existed can't have had an opinion on it, and
            // both of those modes work either way.
            CameraSmoothing = cameraSmoothingMode == UnlockCameraStrategy.Off ? false : null,

            SmoothBackground = ParseBoolean(data.Attr("smoothBackground")),
            SmoothForeground = ParseBoolean(data.Attr("smoothForeground")),
            RenderMadelineWithSubpixelPrecision = ParseBoolean(data.Attr("renderMadelineWithSubpixels")),
            FrameRate = ParseFrameRate(data.Attr("frameRate"))
        });
    }

    private static UnlockCameraStrategy? ParseCameraSmoothing(string value) => value switch
    {
        ValueFancy => UnlockCameraStrategy.Hires,
        ValueFast => UnlockCameraStrategy.Unlock,
        ValueOff => UnlockCameraStrategy.Off,
        _ => null
    };
}
