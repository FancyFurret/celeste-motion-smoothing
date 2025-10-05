using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using VanillaSaveData = Celeste.SaveData;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class SmoothParallaxRenderer : Renderer
{
    public static SmoothParallaxRenderer Instance { get; private set; }

    public VirtualRenderTarget LargeBuffer1 { get; }
    public VirtualRenderTarget LargeBuffer2 { get; }
    public VirtualRenderTarget LargeBuffer3 { get; }
    public VirtualRenderTarget SmallBuffer1 { get; }

    public VirtualRenderTarget LargeTempA { get; }
    public VirtualRenderTarget LargeTempB { get; }

    public Matrix ScaleMatrix;

    public SmoothParallaxRenderer(
        VirtualRenderTarget largeBuffer1,
        VirtualRenderTarget largeBuffer2,
        VirtualRenderTarget largeBuffer3,
        VirtualRenderTarget smallBuffer1,
        VirtualRenderTarget largeTempA,
        VirtualRenderTarget largeTempB
    )
    {
        LargeBuffer1 = largeBuffer1;
        LargeBuffer2 = largeBuffer2;
        LargeBuffer3 = largeBuffer3;
        SmallBuffer1 = smallBuffer1;

        LargeTempA = largeTempA;
        LargeTempB = largeTempB;

        ScaleMatrix = Matrix.CreateScale(6f);

        Visible = true;
    }

    public static void Load()
    {

    }

    public static SmoothParallaxRenderer Create()
    {
        Destroy();

        Instance = new SmoothParallaxRenderer(
            GameplayBuffers.Create(1920, 1080),
            GameplayBuffers.Create(1920, 1080),
            GameplayBuffers.Create(1920, 1080),
            GameplayBuffers.Create(320, 180),
            GameplayBuffers.Create(1920, 1080),
            GameplayBuffers.Create(1920, 1080)
        );

        return Instance;
    }

    public static void Destroy()
    {
        Instance = null;
    }
}