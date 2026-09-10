using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Maps;

// Vanilla's Postcard with smaller text, because a message that has to explain a conflict between
// two mods runs off the bottom of the card at vanilla's 0.7 scale. Everything else about it --
// easing in, waiting for Confirm, easing out -- is inherited.
//
// The change doesn't fit through an override: Postcard.BeforeRender isn't virtual and has the text
// scale as a literal. So HiresStylegrounds hooks LevelEnter.BeforeRender to call the version below
// in its place.
public class MotionSmoothingPostcard : Postcard
{
    // Vanilla draws the message at 0.7.
    private const float MessageScale = 0.55f;

    public MotionSmoothingPostcard(string message) : base(message)
    {
        // Re-lay-out the message for the smaller scale. The base constructor wrapped it to fit the
        // card at 0.7, so drawing that layout at 0.55 would break lines well short of the edge.
        text = FancyText.Parse(message, (int)((this.Postcard.Width - 120) / MessageScale), -1, 1f,
            Color.Black * 0.6f);
    }

    // Postcard.BeforeRender with the message drawn at MessageScale. Called from
    // HiresStylegrounds' LevelEnter.BeforeRender hook rather than by LevelEnter itself.
    public new void BeforeRender()
    {
        if (target == null)
            target = VirtualContent.CreateRenderTarget("postcard", this.Postcard.Width, this.Postcard.Height);

        Engine.Graphics.GraphicsDevice.SetRenderTarget(target);
        Engine.Graphics.GraphicsDevice.Clear(Color.Transparent);
        Draw.SpriteBatch.Begin();

        var name = Dialog.Clean("FILE_DEFAULT");
        if (SaveData.Instance != null && Dialog.Language.CanDisplay(SaveData.Instance.Name))
            name = SaveData.Instance.Name;

        this.Postcard.Draw(Vector2.Zero);
        ActiveFont.Draw(name, new Vector2(115f, 30f), Vector2.Zero, Vector2.One * 0.9f, Color.Black * 0.7f);
        text.DrawJustifyPerLine(new Vector2(this.Postcard.Width, this.Postcard.Height) / 2f + new Vector2(0f, 40f),
            new Vector2(0.5f, 0.5f), Vector2.One * MessageScale, 1f);

        Draw.SpriteBatch.End();
    }
}
