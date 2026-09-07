module MotionSmoothingMotionSmoothingController2

using ..Ahorn, Maple

@mapdef Trigger "MotionSmoothing/MotionSmoothingController2" MotionSmoothingController2(x::Integer, y::Integer, width::Integer=16, height::Integer=16, renderingMode::String="NoPreference", cameraSmoothing::String="NoPreference", frameRate::String="NoPreference", smoothBackground::String="NoPreference", smoothForeground::String="NoPreference", renderMadelineWithSubpixels::String="NoPreference")

const placements = Ahorn.PlacementDict(
   "Motion Smoothing Controller (Motion Smoothing)" => Ahorn.EntityPlacement(
      MotionSmoothingController2,
      "rectangle"
   )
)

# Keyed by the label shown in the editor; the values are what get written to the map, so
# "NoPreference" stays as-is and already-placed triggers keep working.
const onOff = Dict{String, String}(
   "User Default" => "NoPreference",
   "On" => "On",
   "Off" => "Off"
)

const renderingModes = Dict{String, String}(
   "User Default" => "NoPreference",
   "Fancy" => "Fancy",
   "Fast" => "Fast",
   "Off" => "Off"
)

# frameRate is deliberately absent: it's a free-text field so a mapper can type any framerate, or
# "NoPreference" to leave it to the player. Ahorn's option lists can't be typed into.
Ahorn.editingOptions(trigger::MotionSmoothingController2) = Dict{String, Any}(
   "renderingMode" => renderingModes,
   "cameraSmoothing" => onOff,
   "smoothBackground" => onOff,
   "smoothForeground" => onOff,
   "renderMadelineWithSubpixels" => onOff
)

end
