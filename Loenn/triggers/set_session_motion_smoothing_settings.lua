local SetSessionMotionSmoothingSettings = {}

SetSessionMotionSmoothingSettings.name = "MotionSmoothing/SetSessionMotionSmoothingSettings"

SetSessionMotionSmoothingSettings.placements = {
    name = "set_session_motion_smoothing_settings",
    data = {
        width = 16,
        height = 16,
		enabled = "Ignore",
		framerate = "Ignore",
		smoothCamera = "Ignore",
		renderMadelineWithSubpixelPrecision = "Ignore",
		smoothBackground = "Ignore",
		smoothForeground = "Ignore",
		hideStretchedLevelEdges = "Ignore",
		objectSmoothing = "Ignore",
		framerateIncreaseMethod = "Ignore",
		nastyMode = "Ignore"
    }
}

SetSessionMotionSmoothingSettings.fieldOrder = {
	"x", "y",
	"width", "height",
	"editorColor", "enabled",
	"framerate", "smoothCamera",
	"renderMadelineWithSubpixelPrecision", "smoothBackground",
	"smoothForeground", "hideStretchedLevelEdges",
	"objectSmoothing", "framerateIncreaseMethod",
	"nastyMode"
}

SetSessionMotionSmoothingSettings.fieldInformation = {
	enabled = {
		fieldType = "string",
		options = {
			"Ignore",
			"OFF",
			"ON"
		},
		editable = false
	},
	framerate = {
		fieldType = "string",
		options = {
			"Ignore",
			"60",
			"120",
			"180",
			"240",
			"300",
			"360",
			"420",
			"480"
		},
		editable = false
	},
	smoothCamera = {
		fieldType = "string",
		options = {
			"Ignore",
			"Fancy",
			"Fast",
			"Off"
		},
		editable = false
	},
	renderMadelineWithSubpixelPrecision = {
		fieldType = "string",
		options = {
			"Ignore",
			"OFF",
			"ON"
		},
		editable = false
	},
	smoothBackground = {
		fieldType = "string",
		options = {
			"Ignore",
			"OFF",
			"ON"
		},
		editable = false
	},
	smoothForeground = {
		fieldType = "string",
		options = {
			"Ignore",
			"OFF",
			"ON"
		},
		editable = false
	},
	hideStretchedLevelEdges = {
		fieldType = "string",
		options = {
			"Ignore",
			"OFF",
			"ON"
		},
		editable = false
	},
	objectSmoothing = {
		fieldType = "string",
		options = {
			"Ignore",
			"Extrapolate",
			"Interpolate",
			"Off"
		},
		editable = false
	},
	framerateIncreaseMethod = {
		fieldType = "string",
		options = {
			"Ignore",
			"Interval",
			"Dynamic"
		},
		editable = false
	},
	nastyMode = {
		fieldType = "string",
		options = {
			"Ignore",
			"OFF",
			"ON"
		},
		editable = false
	}
}

return SetSessionMotionSmoothingSettings