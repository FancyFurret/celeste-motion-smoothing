using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Maps;

// A marker a mapper drops anywhere in their map to *suggest* Motion Smoothing settings for it.
// It does nothing at runtime: the suggestion is read straight out of the map data before the
// level is built (see MapSmoothingSuggestions), so that it can be applied -- and the player told
// about it -- before gameplay starts. The entity still exists so that Everest has something to
// instantiate for it, and so it can be placed in Loenn and Ahorn.
[CustomEntity(MapSmoothingSuggestions.ControllerEntityName)]
public class MotionSmoothingController : Entity
{
    public MotionSmoothingController(EntityData data, Vector2 offset) : base(data.Position + offset)
    {
        Visible = false;
        Active = false;
        Collidable = false;
    }
}
