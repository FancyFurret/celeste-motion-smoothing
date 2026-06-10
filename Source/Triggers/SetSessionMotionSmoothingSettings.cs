using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

namespace Celeste.Mod.MotionSmoothing.Triggers;

[CustomEntity("MotionSmoothing/SetSessionMotionSmoothingSettings")]

public class SetSessionMotionSmoothingSettings : Trigger
{

    private readonly string Enabled;
    private readonly string Framerate;
    private readonly string SmoothCameraString;
    private readonly string RenderMadelineWithSubpixelPrecision;
    private readonly string SmoothBackground;
    private readonly string SmoothForeground;
    private readonly string HideStretchedLevelEdges;
    private readonly string ObjectSmoothingString;
    private readonly string FramerateIncreaseMethodString;
    private readonly string NastyMode;
    
    private UnlockCameraStrategy SmoothCamera;
    private SmoothingMode ObjectSmoothing;
    private UpdateMode FramerateIncreaseMethod;
    
    public SetSessionMotionSmoothingSettings(EntityData data, Vector2 offset) : base(data, offset)
    {
        Enabled = data.Attr("enabled");
        Framerate = data.Attr("framerate");
        SmoothCameraString = data.Attr("smoothCamera");
        RenderMadelineWithSubpixelPrecision = data.Attr("renderMadelineWithSubpixelPrecision");
        SmoothBackground = data.Attr("smoothBackground");
        SmoothForeground = data.Attr("smoothForeground");
        HideStretchedLevelEdges =  data.Attr("hideStretchedLevelEdges");
        ObjectSmoothingString = data.Attr("objectSmoothing");
        FramerateIncreaseMethodString = data.Attr("framerateIncreaseMethod");
        NastyMode = data.Attr("nastyMode");
    }
    
    public override void OnEnter(Player player)
    {
        base.OnEnter(player);

        if (!MotionSmoothingModule.Settings.AllowMapChanges) return;

        MotionSmoothingModule.Session.MapWantsToForceSetSettings = true;
        
        MotionSmoothingModule.Session.MapSetEnabled = Enabled switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetEnabled,
            "OFF" => false,
            "ON" => true
        };
        
        MotionSmoothingModule.Session.MapSetFrameRate = Framerate switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetFrameRate,
            "60" => 60,
            "120" => 120,
            "180" => 180,
            "240" => 240,
            "300" => 300,
            "360" => 360,
            "420" => 420,
            "480" => 480
        };

        SmoothCamera = SmoothCameraString switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetUnlockCameraStrategy,
            "Fancy" => UnlockCameraStrategy.Hires,
            "Fast" => UnlockCameraStrategy.Unlock,
            "Off" => UnlockCameraStrategy.Off
        };
        MotionSmoothingModule.Session.MapSetUnlockCameraStrategy = SmoothCamera;
        
        MotionSmoothingModule.Session.MapSetRenderMadelineWithSubpixels = RenderMadelineWithSubpixelPrecision switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetRenderMadelineWithSubpixels,
            "OFF" => false,
            "ON" => true
        };
        
        MotionSmoothingModule.Session.MapSetRenderBackgroundHires = SmoothBackground switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetRenderBackgroundHires,
            "OFF" => false,
            "ON" => true
        };
        
        MotionSmoothingModule.Session.MapSetRenderForegroundHires = SmoothForeground switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetRenderForegroundHires,
            "OFF" => false,
            "ON" => true
        };
        
        MotionSmoothingModule.Session.MapSetHideStretchedEdges = HideStretchedLevelEdges switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetHideStretchedEdges,
            "OFF" => false,
            "ON" => true
        };

        ObjectSmoothing = ObjectSmoothingString switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetObjectSmoothing,
            "Extrapolate" => SmoothingMode.Extrapolate,
            "Interpolate" => SmoothingMode.Interpolate,
            "Off" => SmoothingMode.Off
        };
        MotionSmoothingModule.Session.MapSetObjectSmoothing = ObjectSmoothing;
        
        FramerateIncreaseMethod = FramerateIncreaseMethodString switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetFramerateIncreaseMethod,
            "Interval" => UpdateMode.Interval,
            "Dynamic" => UpdateMode.Dynamic
        };
        MotionSmoothingModule.Session.MapSetFramerateIncreaseMethod = FramerateIncreaseMethod;

        MotionSmoothingModule.Session.MapSetNastyMode = NastyMode switch
        {
            "Ignore" => MotionSmoothingModule.Session.MapSetNastyMode,
            "OFF" => false,
            "ON" => true
        };
        
        MotionSmoothingModule.Instance.ApplySettings();
    }
}