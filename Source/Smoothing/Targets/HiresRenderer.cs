using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using VanillaSaveData = Celeste.SaveData;
using System;
using System.IO;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class HiresRenderer : Renderer
{
    public static HiresRenderer Instance { get; private set; }

    public VirtualRenderTarget LargeGameplayBuffer { get; }
    public VirtualRenderTarget LargeDisplacementBuffer { get; }
    public VirtualRenderTarget LargeDisplacedGameplayBuffer { get; }
    public VirtualRenderTarget LargeLevelBuffer { get; }
    public VirtualRenderTarget LargeTempABuffer { get; }
    public VirtualRenderTarget LargeTempBBuffer { get; }

    public VirtualRenderTarget SmallLevelBuffer { get; }

    public Matrix ScaleMatrix;

    public bool FixMatrices = false;
    public bool FixMatricesWithoutOffset = false;
    public bool ScaleMatricesForBloom = true;
    public bool AllowParallaxOneBackdrops = false;
    public bool CurrentlyRenderingBackground = false;
    public bool UseModifiedBlur = true;
    public bool DisableFloorFunctions = false;

    private static VirtualRenderTarget OriginalLevelBuffer = null;
    private static VirtualRenderTarget OriginalTempABuffer = null;

    public HiresRenderer(
        VirtualRenderTarget largeGameplayBuffer,
        VirtualRenderTarget largeDisplacementBuffer,
        VirtualRenderTarget largeDisplacedGameplayBuffer,
        VirtualRenderTarget largeLevelBuffer,
        VirtualRenderTarget largeTempABuffer,
        VirtualRenderTarget largeTempBBuffer,
        VirtualRenderTarget smallLevelBuffer
    )
    {
        LargeGameplayBuffer = largeGameplayBuffer;
        LargeDisplacementBuffer = largeDisplacementBuffer;
        LargeDisplacedGameplayBuffer = largeDisplacedGameplayBuffer;
        LargeLevelBuffer = largeLevelBuffer;
        LargeTempABuffer = largeTempABuffer;
        LargeTempBBuffer = largeTempBBuffer;

        SmallLevelBuffer = smallLevelBuffer;

        ScaleMatrix = Matrix.CreateScale(6f);

        Visible = true;
    }

    public static void Load()
    {
    }

    public static void EnableLargeLevelBuffer()
    {
        if (Instance == null)
        {
            return;
        }

        OriginalLevelBuffer = GameplayBuffers.Level;
        GameplayBuffers.Level = Instance.LargeLevelBuffer;
    }

    public static void DisableLargeLevelBuffer()
    {
        if (OriginalLevelBuffer == null)
        {
            return;
        }

        GameplayBuffers.Level = OriginalLevelBuffer;
        OriginalLevelBuffer = null;
    }

    public static void EnableLargeTempABuffer()
    {
        if (Instance == null)
        {
            return;
        }

        OriginalTempABuffer = GameplayBuffers.TempA;
        GameplayBuffers.TempA = Instance.LargeTempABuffer;
        Instance.UseModifiedBlur = true;
    }

    public static void DisableLargeTempABuffer()
    {
        if (OriginalTempABuffer == null)
        {
            return;
        }

        GameplayBuffers.TempA = OriginalTempABuffer;
        OriginalTempABuffer = null;
        Instance.UseModifiedBlur = false;
    }

    public static HiresRenderer Create()
    {
        Destroy();
        
        // Make the buffers extra large to support mods that try to zoom out the camera
        var zoomOutWidth = 960f;
        var zoomOutHeight = 540f;
        var scaleW = zoomOutWidth / 320f;
        var scaleH = zoomOutHeight / 180f;

        Instance = new HiresRenderer(
            GameplayBuffers.Create((int)(1920 * scaleW), (int)(1080 * scaleH)),
            GameplayBuffers.Create((int)(1920 * scaleW), (int)(1080 * scaleH)),
            GameplayBuffers.Create((int)(1920 * scaleW), (int)(1080 * scaleH)),
            GameplayBuffers.Create((int)(1920 * scaleW), (int)(1080 * scaleH)),
            GameplayBuffers.Create((int)(1920 * scaleW), (int)(1080 * scaleH)),
            GameplayBuffers.Create((int)(1920 * scaleW), (int)(1080 * scaleH)),
            GameplayBuffers.Create((int)(320 * scaleW), (int)(180 * scaleH))
        );

        return Instance;
    }

    public static void Destroy()
    {
        if (OriginalLevelBuffer != null)
        {
            GameplayBuffers.Level = OriginalLevelBuffer;
            OriginalLevelBuffer = null;
        }

        if (Instance != null)
        {
            Instance.LargeLevelBuffer?.Dispose();

            Instance = null;
        }
    }
}