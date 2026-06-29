namespace Celeste.Mod.MotionSmoothing;

public class MotionSmoothingSession : EverestModuleSession
{
    public bool? MapWantsToForceSetSettings { get; set; }

    public bool MapSetEnabled { get; set; }

    public int MapSetFrameRate { get; set; }

    public UnlockCameraStrategy MapSetUnlockCameraStrategy { get; set; }

    public bool MapSetRenderMadelineWithSubpixels { get; set; }
    
    public bool MapSetRenderBackgroundHires  { get; set; }
    
    public bool MapSetRenderForegroundHires  { get; set; }
    
    public bool MapSetHideStretchedEdges  { get; set; }

    public SmoothingMode MapSetObjectSmoothing { get; set; }
    
    public UpdateMode MapSetFramerateIncreaseMethod { get; set; }

    public bool MapSetNastyMode { get; set; }
}