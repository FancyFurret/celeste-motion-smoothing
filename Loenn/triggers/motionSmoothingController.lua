local trigger = {}

trigger.name = "MotionSmoothing/MotionSmoothingController"

-- Loenn draws triggers itself: a resizable translucent rectangle labelled with this text. The
-- default label is the humanized entity name, which already reads "Motion Smoothing Controller",
-- but say it outright so a rename can't quietly change it.
trigger.triggerText = "Motion Smoothing Controller"

trigger.placements = {
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
        {"User Default", "NoPreference"},
        {"On", "On"},
        {"Off", "Off"}
    },
    editable = false
}

trigger.fieldInformation = {
    motionSmoothing = onOff,
    smoothBackground = onOff,
    smoothForeground = onOff,
    renderMadelineWithSubpixels = onOff,
    cameraSmoothingMode = {
        options = {
            {"User Default", "NoPreference"},
            {"Fancy", "Fancy"},
            {"Fast", "Fast"},
            {"Off", "Off"}
        },
        editable = false
    }
}

trigger.fieldOrder = {
    "x", "y", "width", "height",
    "motionSmoothing",
    "cameraSmoothingMode",
    "smoothBackground",
    "smoothForeground",
    "renderMadelineWithSubpixels"
}

return trigger
