using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing;

public class PushSpriteSmoother : MotionSmoother
{
    private readonly Stack<object> _currentObjects = new();

    public void SmoothEntity(Entity entity) => SmoothObject(new EntitySmoothingState(entity));
    public void SmoothComponent(GraphicsComponent component) => SmoothObject(new ComponentSmoothingState(component));

    public void PreObjectRender(object obj)
    {
        _currentObjects.Push(obj);
    }

    public void PostObjectRender()
    {
        _currentObjects.Pop();
    }

    public Vector2 GetSpritePosition(Vector2 position)
    {
        if (_currentObjects.Count == 0) return position;

        var obj = _currentObjects.Peek();
        position += obj switch
        {
            GraphicsComponent graphicsComponent => GetOffset(graphicsComponent) + GetOffset(graphicsComponent.Entity),
            Component component => GetOffset(component.Entity),
            _ => GetOffset(obj)
        };

        return position;
    }
}