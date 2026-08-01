module MotionSmoothingMotionSmoothingController

using ..Ahorn, Maple

@mapdef Entity "MotionSmoothing/MotionSmoothingController" MotionSmoothingController(x::Integer, y::Integer, motionSmoothing::String="NoPreference", cameraSmoothingMode::String="NoPreference", smoothBackground::String="NoPreference", smoothForeground::String="NoPreference", renderMadelineWithSubpixels::String="NoPreference")

const placements = Ahorn.PlacementDict(
   "Motion Smoothing Controller (Motion Smoothing)" => Ahorn.EntityPlacement(
      MotionSmoothingController
   )
)

const onOff = String["NoPreference", "On", "Off"]
const cameraModes = String["NoPreference", "Fancy", "Fast", "Off"]

Ahorn.editingOptions(entity::MotionSmoothingController) = Dict{String, Any}(
   "motionSmoothing" => onOff,
   "cameraSmoothingMode" => cameraModes,
   "smoothBackground" => onOff,
   "smoothForeground" => onOff,
   "renderMadelineWithSubpixels" => onOff
)

function Ahorn.render(ctx::Ahorn.Cairo.CairoContext, entity::MotionSmoothingController, room::Maple.Room)
    Ahorn.drawRectangle(ctx, 0, 0, 16, 16, Ahorn.defaultBlackColor, Ahorn.defaultWhiteColor)
end

function Ahorn.selection(entity::MotionSmoothingController)
    x, y = Ahorn.position(entity)
    return Ahorn.Rectangle(x, y, 16, 16)
end

end
