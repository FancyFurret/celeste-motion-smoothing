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
[CustomEntity(MapSmoothingSuggestions.ControllerEntityName)]
public class MotionSmoothingController : Trigger
{
    private const string ValueOn = "On";
    private const string ValueOff = "Off";
    private const string ValueFancy = "Fancy";
    private const string ValueFast = "Fast";

    private readonly MapSmoothingSuggestion _suggestion;

    public MotionSmoothingController(EntityData data, Vector2 offset) : base(data, offset)
    {
        _suggestion = new MapSmoothingSuggestion
        {
            Enabled = ParseBoolean(data.Attr("motionSmoothing")),
            SmoothBackground = ParseBoolean(data.Attr("smoothBackground")),
            SmoothForeground = ParseBoolean(data.Attr("smoothForeground")),
            RenderMadelineWithSubpixelPrecision = ParseBoolean(data.Attr("renderMadelineWithSubpixels")),
            CameraSmoothingMode = ParseCameraSmoothing(data.Attr("cameraSmoothingMode"))
        };

        MapSmoothingSuggestions.DropInapplicable(_suggestion);
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
    private static bool? ParseBoolean(string value) => value switch
    {
        ValueOn => true,
        ValueOff => false,
        _ => null
    };

    private static UnlockCameraStrategy? ParseCameraSmoothing(string value) => value switch
    {
        ValueFancy => UnlockCameraStrategy.Hires,
        ValueFast => UnlockCameraStrategy.Unlock,
        ValueOff => UnlockCameraStrategy.Off,
        _ => null
    };
}
