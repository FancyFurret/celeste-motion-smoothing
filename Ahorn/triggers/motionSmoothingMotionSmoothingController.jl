module MotionSmoothingMotionSmoothingController

using ..Ahorn, Maple

@mapdef Trigger "MotionSmoothing/MotionSmoothingController" MotionSmoothingController(x::Integer, y::Integer, width::Integer=16, height::Integer=16, motionSmoothing::String="NoPreference", frameRate::String="NoPreference", cameraSmoothingMode::String="NoPreference", smoothBackground::String="NoPreference", smoothForeground::String="NoPreference", renderMadelineWithSubpixels::String="NoPreference")

const placements = Ahorn.PlacementDict(
   "Motion Smoothing Controller (Motion Smoothing)" => Ahorn.EntityPlacement(
      MotionSmoothingController,
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

const cameraModes = Dict{String, String}(
   "User Default" => "NoPreference",
   "Fancy" => "Fancy",
   "Fast" => "Fast",
   "Off" => "Off"
)

# frameRate is deliberately absent: it's a free-text field so a mapper can type any framerate, or
# "NoPreference" to leave it to the player. Ahorn's option lists can't be typed into.
Ahorn.editingOptions(trigger::MotionSmoothingController) = Dict{String, Any}(
   "motionSmoothing" => onOff,
   "cameraSmoothingMode" => cameraModes,
   "smoothBackground" => onOff,
   "smoothForeground" => onOff,
   "renderMadelineWithSubpixels" => onOff
)

end
