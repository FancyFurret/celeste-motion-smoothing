using System.Collections;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Maps;

// Vanilla's Postcard with two changes:
//
//  * smaller text, because a map that changes a lot of settings runs off the bottom of the card at
//    vanilla's 0.7 scale;
//  * a yes/no prompt, so the player answers on the card itself rather than having to go into the
//    mod options afterwards.
//
// Celeste has no yes/no postcard to inherit from, so the prompt reuses the affordance the postcard
// already has: the Confirm glyph it eases into the bottom-right corner, plus a labelled Cancel one
// beside it. Both are drawn in screen space like vanilla's, which also keeps them clear of the card
// no matter how long the message runs.
//
// Neither change fits through an override -- Postcard.BeforeRender isn't virtual and has the text
// scale as a literal -- so MapSmoothingSuggestions hooks LevelEnter.BeforeRender to call the
// version below in its place.
public class MotionSmoothingPostcard : Postcard
{
    // Vanilla draws the message at 0.7.
    private const float MessageScale = 0.55f;

    private const float PromptLabelScale = 0.7f;
    private const float PromptLabelSpacing = 16f;
    private const float PromptGap = 60f;

    public bool Accepted { get; private set; }

    public MotionSmoothingPostcard(string message) : base(message)
    {
        // Re-lay-out the message for the smaller scale. The base constructor wrapped it to fit the
        // card at 0.7, so drawing that layout at 0.55 would break lines well short of the edge.
        text = FancyText.Parse(message, (int)((this.Postcard.Width - 120) / MessageScale), -1, 1f,
            Color.Black * 0.6f);
    }

    // Postcard.BeforeRender with the message drawn at MessageScale. Called from
    // MapSmoothingSuggestions' LevelEnter.BeforeRender hook rather than by LevelEnter itself.
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

    public override void Render()
    {
        if (target != null)
        {
            Draw.SpriteBatch.Draw((RenderTarget2D)target, Position, target.Bounds, Color.White * alpha, rotation,
                new Vector2(target.Width, target.Height) / 2f, scale, SpriteEffects.None, 0f);
        }

        if (buttonEase <= 0f) return;

        var ease = Ease.CubeOut(buttonEase);
        var y = Engine.Height - 100f - 20f * ease;

        var right = DrawPrompt(Input.MenuConfirm, "MOTIONSMOOTHING_POSTCARD_ACCEPT", Engine.Width - 80f, y, ease);
        DrawPrompt(Input.MenuCancel, "MOTIONSMOOTHING_POSTCARD_DECLINE", right, y, ease);
    }

    // Draws "[glyph] Label" with its right edge at `right`, and returns the right edge the next
    // prompt along should use.
    private static float DrawPrompt(VirtualButton button, string labelKey, float right, float y, float ease)
    {
        var color = Color.White * ease;
        var label = Dialog.Clean(labelKey);
        var labelWidth = ActiveFont.Measure(label).X * PromptLabelScale;

        ActiveFont.DrawOutline(label, new Vector2(right - labelWidth, y), new Vector2(0f, 0.5f),
            Vector2.One * PromptLabelScale, color, 2f, Color.Black * ease);

        var glyph = Input.GuiButton(button, Input.PrefixMode.Latest);
        var glyphRight = right - labelWidth - PromptLabelSpacing;
        glyph.DrawJustified(new Vector2(glyphRight, y), new Vector2(1f, 0.5f), color);

        return glyphRight - glyph.Width - PromptGap;
    }

    // Postcard.DisplayRoutine, except Cancel is an answer rather than being ignored.
    public IEnumerator PromptRoutine()
    {
        yield return EaseIn();
        yield return 0.75f;

        while (true)
        {
            if (Input.MenuConfirm.Pressed)
            {
                Accepted = true;
                break;
            }

            if (Input.MenuCancel.Pressed) break;

            yield return null;
        }

        Audio.Play(Accepted ? "event:/ui/main/button_select" : "event:/ui/main/button_back");

        yield return EaseOut();
        yield return 1.2f;
    }
}
