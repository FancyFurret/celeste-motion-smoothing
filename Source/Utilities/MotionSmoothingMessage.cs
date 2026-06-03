using System.Collections;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// A small self-contained on-screen popup, modeled on SpeedrunTool's Tooltip. Each message
// is keyed by an id so a new press of the same toggle replaces the old popup (resetting its
// timer) and the two different toggles can stack at their own y positions. This replaces the
// previous dependency on the DisplayMessageCommand mod's "display_message"/"hide_message".
[Tracked]
public class MotionSmoothingMessage : Entity
{
    private readonly string id;
    private readonly string message;
    private readonly float scale;

    private float alpha;
    private float unEasedAlpha;
    private readonly float duration;

    private MotionSmoothingMessage(string id, string message, float scale, float y, float duration)
    {
        this.id = id;
        this.message = message;
        this.scale = scale;
        this.duration = duration;

        // Rendered in HUD space (1920x1080), horizontally centered at the given y.
        Position = new Vector2(Engine.Width / 2f, y);
        Tag = Tags.HUD | Tags.Global | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.TransitionUpdate;

        Add(new Coroutine(Show()));
    }

    private IEnumerator Show()
    {
        while (alpha < 1f)
        {
            unEasedAlpha = Calc.Approach(unEasedAlpha, 1f, Engine.RawDeltaTime * 5f);
            alpha = Ease.SineOut(unEasedAlpha);
            yield return null;
        }

        yield return Dismiss();
    }

    private IEnumerator Dismiss()
    {
        yield return duration;
        while (alpha > 0f)
        {
            unEasedAlpha = Calc.Approach(unEasedAlpha, 0f, Engine.RawDeltaTime * 5f);
            alpha = Ease.SineIn(unEasedAlpha);
            yield return null;
        }

        RemoveSelf();
    }

    public override void Render()
    {
        base.Render();
        ActiveFont.DrawOutline(message, Position, new Vector2(0.5f, 0.5f), Vector2.One * scale,
            Color.White * alpha, 2, Color.Black * alpha * alpha * alpha);
    }

    public static void Show(string id, string message, float scale = 0.5f, float y = 980f, float duration = 1f)
    {
        if (Engine.Scene is not { } scene)
        {
            return;
        }

        Hide(id);
        scene.Add(new MotionSmoothingMessage(id, message, scale, y, duration));
    }

    public static void Hide(string id)
    {
        if (Engine.Scene is not { } scene)
        {
            return;
        }

        foreach (var entity in scene.Entities.FindAll<MotionSmoothingMessage>().Where(m => m.id == id))
        {
            entity.RemoveSelf();
        }
    }
}
