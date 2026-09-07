local trigger = {}

trigger.name = "MotionSmoothing/MotionSmoothingController2"

-- Loenn draws triggers itself: a resizable translucent rectangle labelled with this text. The
-- default label is the humanized entity name, which would read "Motion Smoothing Controller2", so
-- say it outright instead.
trigger.triggerText = "Motion Smoothing Controller"

trigger.placements = {
    name = "controller",
    data = {
        renderingMode = "NoPreference",
        cameraSmoothing = "NoPreference",
        frameRate = "NoPreference",
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

-- Framerates are kept as strings rather than numbers so that "User Default" can be one of the
-- values: an editable dropdown, so a mapper can pick that or type any framerate they like. The mod
-- treats anything that isn't a whole number of frames as "no preference", and uses whatever else it
-- is given exactly -- including framerates the in-game slider would never stop on (24) or reach (3).
local function isFrameRate(value)
    if value == "NoPreference" then
        return true
    end

    local number = tonumber(value)

    return number ~= nil and number == math.floor(number) and number >= 1
end

trigger.fieldInformation = {
    frameRate = {
        options = {
            {"User Default", "NoPreference"},
            {"30", "30"},
            {"60", "60"},
            {"120", "120"},
            {"240", "240"}
        },
        editable = true,
        validator = isFrameRate
    },
    renderingMode = {
        options = {
            {"User Default", "NoPreference"},
            {"Fancy", "Fancy"},
            {"Fast", "Fast"},
            {"Off", "Off"}
        },
        editable = false
    },
    cameraSmoothing = onOff,
    smoothBackground = onOff,
    smoothForeground = onOff,
    renderMadelineWithSubpixels = onOff
}

trigger.fieldOrder = {
    "x", "y", "width", "height",
    "renderingMode",
    "cameraSmoothing",
    "frameRate",
    "smoothBackground",
    "smoothForeground",
    "renderMadelineWithSubpixels"
}

return trigger
