local drawableRectangle = require("structs.drawable_rectangle")
local utils = require("utils")

local controller = {}

controller.name = "MotionSmoothing/MotionSmoothingController"
controller.depth = -1000000

controller.placements = {
    name = "controller",
    data = {
        motionSmoothing = "NoPreference",
        cameraSmoothingMode = "NoPreference",
        smoothBackground = "NoPreference",
        smoothForeground = "NoPreference",
        renderMadelineWithSubpixels = "NoPreference"
    }
}

local onOff = {
    options = {
        {"No Preference", "NoPreference"},
        {"On", "On"},
        {"Off", "Off"}
    },
    editable = false
}

controller.fieldInformation = {
    motionSmoothing = onOff,
    smoothBackground = onOff,
    smoothForeground = onOff,
    renderMadelineWithSubpixels = onOff,
    cameraSmoothingMode = {
        options = {
            {"No Preference", "NoPreference"},
            {"Fancy", "Fancy"},
            {"Fast", "Fast"},
            {"Off", "Off"}
        },
        editable = false
    }
}

controller.fieldOrder = {
    "x", "y",
    "motionSmoothing",
    "cameraSmoothingMode",
    "smoothBackground",
    "smoothForeground",
    "renderMadelineWithSubpixels"
}

function controller.sprite(room, entity)
    return drawableRectangle.fromRectangle("bordered", entity.x, entity.y, 16, 16,
        {0.11, 0.30, 0.44, 0.8}, {0.49, 0.78, 0.96, 1.0}):getDrawableSprite()
end

function controller.selection(room, entity)
    return utils.rectangle(entity.x, entity.y, 16, 16)
end

return controller
