using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Utilities;

// Shared behaviour for the two lockable menu items below. "Locked" means a map is currently
// deciding the setting: the item stays selectable and keeps showing its value, it just refuses to
// change. That's why locking overrides the input entry points rather than setting Disabled, which
// would make the item unhoverable and force it gray -- the same gray it gets when the setting
// simply doesn't apply.
internal static class LockableMenuItem
{
    public static readonly Color LockedColor = Calc.HexToColor("CC99FF");

    // Swallows the press, with the stock "can't do that" sound.
    public static bool Refuse(bool locked)
    {
        if (!locked) return false;

        Audio.Play("event:/ui/main/button_invalid");
        return true;
    }

    // TextMenu.Option<T>.Render, with one change: a locked item's < and > are drawn in the gray
    // vanilla uses for "there's nothing further this way", because there's nothing further any way.
    // That can't be done by hooking anything -- the arrow colours are decided inside the method
    // from Index alone -- so the body is reproduced here. The label and value are left to the
    // normal path, so a locked item still highlights in the menu's usual colour when selected.
    public static void Render<T>(TextMenu.Option<T> option, bool locked, Vector2 position, bool highlighted)
    {
        var alpha = option.Container.Alpha;
        var stroke = Color.Black * (alpha * alpha * alpha);
        var color = option.Disabled
            ? Color.DarkSlateGray
            : (highlighted ? option.Container.HighlightColor : option.UnselectedColor) * alpha;

        ActiveFont.DrawOutline(option.Label, position, new Vector2(0f, 0.5f), Vector2.One, color, 2f, stroke);

        if (option.Values.Count <= 0) return;

        var right = option.RightWidth();

        ActiveFont.DrawOutline(option.Values[option.Index].Item1,
            position + new Vector2(
                option.Container.Width - right * 0.5f + option.lastDir * option.ValueWiggler.Value * 8f, 0f),
            new Vector2(0.5f, 0.5f), Vector2.One * 0.8f, color, 2f, stroke);

        var wiggle = Vector2.UnitX * (highlighted ? (float)Math.Sin(option.sine * 4f) * 4f : 0f);
        var dimmed = Color.DarkSlateGray * alpha;

        var canGoLeft = !locked && option.Index > 0;
        ActiveFont.DrawOutline("<",
            position + new Vector2(
                option.Container.Width - right + 40f + (option.lastDir < 0 ? -option.ValueWiggler.Value * 8f : 0f),
                0f) - (canGoLeft ? wiggle : Vector2.Zero),
            new Vector2(0.5f, 0.5f), Vector2.One, canGoLeft ? color : dimmed, 2f, stroke);

        var canGoRight = !locked && option.Index < option.Values.Count - 1;
        ActiveFont.DrawOutline(">",
            position + new Vector2(
                option.Container.Width - 40f + (option.lastDir > 0 ? option.ValueWiggler.Value * 8f : 0f),
                0f) + (canGoRight ? wiggle : Vector2.Zero),
            new Vector2(0.5f, 0.5f), Vector2.One, canGoRight ? color : dimmed, 2f, stroke);
    }
}

public class LockableOnOff : TextMenu.OnOff
{
    private bool _locked;

    public LockableOnOff(string label, bool on) : base(label, on)
    {
    }

    public bool Locked
    {
        get => _locked;
        set
        {
            _locked = value;
            UnselectedColor = value ? LockableMenuItem.LockedColor : Color.White;
        }
    }

    public override void LeftPressed()
    {
        if (LockableMenuItem.Refuse(_locked)) return;
        base.LeftPressed();
    }

    public override void RightPressed()
    {
        if (LockableMenuItem.Refuse(_locked)) return;
        base.RightPressed();
    }

    public override void ConfirmPressed()
    {
        if (LockableMenuItem.Refuse(_locked)) return;
        base.ConfirmPressed();
    }

    public override void Render(Vector2 position, bool highlighted) =>
        LockableMenuItem.Render(this, _locked, position, highlighted);
}

public class LockableSlider : TextMenu.Slider
{
    private bool _locked;

    public LockableSlider(string label, Func<int, string> values, int min, int max, int value = -1)
        : base(label, values, min, max, value)
    {
    }

    public bool Locked
    {
        get => _locked;
        set
        {
            _locked = value;
            UnselectedColor = value ? LockableMenuItem.LockedColor : Color.White;
        }
    }

    public override void LeftPressed()
    {
        if (LockableMenuItem.Refuse(_locked)) return;
        base.LeftPressed();
    }

    public override void RightPressed()
    {
        if (LockableMenuItem.Refuse(_locked)) return;
        base.RightPressed();
    }

    public override void ConfirmPressed()
    {
        if (LockableMenuItem.Refuse(_locked)) return;
        base.ConfirmPressed();
    }

    public override void Render(Vector2 position, bool highlighted) =>
        LockableMenuItem.Render(this, _locked, position, highlighted);
}
