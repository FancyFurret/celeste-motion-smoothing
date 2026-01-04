# Celeste Motion Smoothing

[![GameBanana](https://gamebanana.com/mods/embeddables/514173?type=sd_image)](https://gamebanana.com/mods/514173)

A mod for Celeste that raises the FPS above 60, *without* breaking physics. Settings are available to toggle the mod,
change the target framerate, and change the player smoothing mode. This essentially works by keeping the physics update
at a fixed 60 FPS, and smoothing entity/camera/etc positions during render at 120+ FPS. Should work with most modded
maps, I've been using it successfully with Strawberry Jam levels.

## Implementation Details for Mod Authors

The primary feature that can affect compatibility with other mods is camera smoothing: rendering the gameplay offset by a fraction of a pixel to greatly improve smoothness. The "Most Compatible" mode works by drawing the entire level buffer at a fractional offset when upscaling to the screen. While this correctly offsets the gameplay layer, it has the unfortunate drawback of making the background jitter, since unless a background object has parallax one (moving in lockstep with the camera), every time the camera moves a whole pixel, the background will effectively snap a whole pixel back. The solution is to do the offsetting in the compositing step, which is the "Highest Quality" mode; the remainder of this guide explains how this works.

### Hires Rendering in Vanilla

When this mode is enabled, `GameplayBuffers.Gameplay` and `GameplayBuffers.Level` are both converted to 1920x1080 instead of 320x180 (a 6x scale increase) at some point in `Level.Render`. The background is drawn either to a small buffer and scaled up, or drawn directly with a 6x scale into the large level buffer, depending on the Smooth Background option. Similarly, the gameplay is either drawn at 320x180 and scaled up, or drawn in parts and scaled up (so that Madeline and anything she's holding can be drawn with a subpixel offset), depending on the Render Madeline with Subpixel Precision setting. Either way, the gameplay is composited onto the background with a fractional-pixel offset to smooth the camera. The foreground must be drawn with a 6x scale matrix, since there is no way to draw it small and scale it up later due to it relying on potentially complicated blending states.

There are a few small fixes that help here: first, if the background is rendered at low resolution, anything with parallax one (e.g. the black hole background in Farewell) won't be able to move in lockstep with the camera since it can only move in whole-pixel increments, and so it has a jitter. To fix this, we delay drawing the parallax-one backgrounds until after everything else in the background has rendered, then draw them after the upscaling with a fractional-pixel offset. This works well enough, but there is the occasional color inaccuracy due to the layers being out of order. The Smooth Background option obviates this issue.

The second quirk is that many vanilla and modded entities that render at high-resolution (talk indicators, titles in SJ gyms, etc), deliberately floor the camera position before they render, and similarly for parallax background objects. To fix this, we use the somewhat blunt approach of disabling all calls to `Calc.Floor`, `Calc.Ceiling`, and `Calc.Round` when applied to XNA `Vector2`s during these two parts of rendering.

### Interactions with Other Mods

The large buffers **automatically scale up anything drawn into them that is not itself a large buffer**. This means that any code which draws into the gameplay or level buffers assuming they're 320x180 should work with no modifications at all.

Issues can arise when mods attempt to draw one of these large buffers out to something else — that is, they're used as the source rather than the target. Since any buffer to which one is drawn is very likely not big enough to contain it, we create a buffer on the fly that is 6x the size of the source and proceed to use that large one whenever its small equivalent is used as a source or target from then on, until the small buffer is disposed or the Hires Camera Smoother is unloaded. We also mark the newly-created large buffer as large (unsurprisingly), so that it is exempted from being upscaled when drawn into another large buffer. This all involves hooking `SpriteBatch.Begin`, `SpriteBatch.Draw`, `SpriteBatch.End`, the internal method `SpriteBatch.PushSprite`, and `SetRenderTargets`, and it's very reasonable to be unsettled by all this. In practice, however, it works beautifully! These hot-created buffers are necessary in SJ's advanced heart side, for example, which uses bloom masks created by drawing the level buffer into their own 320x180 buffer.

There are a few minor exceptions to this hot-creation of large buffers. If the small version of the buffer is larger than 640 pixels in width or height, we skip the creation to avoid hitting a maximum texture size limit that can be as low as 4096x4096. We also then skip the scaling of the content. This adds compatibility with CelesteNet, for example, which requests a buffer the size of the screen and draws the level buffer into it.

Finally, if we can't successfully create a large buffer, but the dimensions of the target closely match the dimensions of the source, then we can assume that something else has been messing with buffer sizes, and we just draw straight into it, without any scaling, and mark the target as a large buffer. This adds compatibility with DBBHelper, which draws the gameplay buffer all over the place, to and from the TempA buffer.

### Interop

There are a small collection of methods to help avoid jitter in drawing situations that aren't covered by our hooks.

- `Vector2 GetFractionalCameraOffset()`: Returns the fractional camera offset in [0, 1) for camera smoothing, or zero if camera smoothing is disabled. Available in v1.3.1+.

- `Matrix GetLevelZoomMatrix()`: Returns the camera zoom matrix (typically a 181/180 scale) for camera smoothing. Available in v1.3.1+.

- `VirtualRenderTarget GetResizableBuffer(VirtualRenderTarget largeRenderTarget)`: When passed a VirtualRenderTarget to one of our internal large buffers, returns the corresponding small large render target which can safely be resized (or the input if none exists). Available in v1.3.2+.

- `void ReloadLargeTextures()`: Recreates the large texture data. This is necessary if you've called VirtualRenderTarget.Reload() on the Gameplay, Level, TempA, or TempB buffers. Available in v1.3.2+.