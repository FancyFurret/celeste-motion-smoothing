-- Adds a "High Resolution" checkbox to image stylegrounds.
--
-- Parallax is one of Loenn's own plugins rather than a mod's, and its property list isn't something
-- a mod can extend from the outside, so this wraps the three functions the styleground window asks
-- it for. Everything else about the plugin is left alone, and a map that never ticks the box is
-- written exactly as it was before.
--
-- The attribute is namespaced because it goes into the same bag as every other styleground
-- property; see HiresStylegrounds.Attribute on the mod side, which has to agree with it.

local parallax = require("parallax")

local library = {}

library.name = "motionSmoothingStylegrounds"

local attribute = "motionSmoothingHires"

local function copy(t)
    local result = {}

    for key, value in pairs(t or {}) do
        result[key] = value
    end

    return result
end

local function copyList(t)
    local result = {}

    for i, value in ipairs(t or {}) do
        result[i] = value
    end

    return result
end

-- Kept on the plugin itself rather than in a local, so that reloading plugins (which re-runs this
-- file) wraps the originals again instead of wrapping the wrappers.
parallax._motionSmoothingOriginals = parallax._motionSmoothingOriginals or {
    defaultData = parallax.defaultData,
    fieldOrder = parallax.fieldOrder,
    fieldInformation = parallax.fieldInformation
}

local originals = parallax._motionSmoothingOriginals

function parallax.defaultData(style)
    local data = copy(originals.defaultData(style))

    data[attribute] = false

    return data
end

function parallax.fieldOrder(style)
    local order = copyList(originals.fieldOrder(style))

    table.insert(order, attribute)

    return order
end

function parallax.fieldInformation(style)
    local information = copy(originals.fieldInformation(style))

    information[attribute] = {
        fieldType = "boolean"
    }

    return information
end

return library
