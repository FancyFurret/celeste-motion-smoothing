# Celeste Motion Smoothing

A mod for Celeste that raises the FPS above 60, *without* breaking physics. Settings are available to toggle the mod, 
change the target framerate, and change the player smoothing mode. This essentially works by keeping the physics update
at a fixed 60 FPS, and smoothing entity/camera/etc positions during render at 120+ FPS. Should work with most modded 
maps, I've been using it successfully with Strawberry Jam levels.

## Player Smoothing Modes
* **None**: No smoothing is applied to the player's movement, movement should feel just as snappy as vanilla.
* **Extrapolate**: [Recommended] This mode predicts the players position based off the current speed. This mode
feels very similar to vanilla, and looks quite smooth.
* **Interpolate**: This mode interpolates the player's position between the last two physics updates. This mode should
be a bit smoother than extrapolate, but there will be an extra 1-2 frames of delay.