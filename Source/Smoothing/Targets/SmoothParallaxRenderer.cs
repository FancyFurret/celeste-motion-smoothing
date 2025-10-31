using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using VanillaSaveData = Celeste.SaveData;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class SmoothParallaxRenderer : Renderer
{
    public static SmoothParallaxRenderer Instance { get; private set; }

    public VirtualRenderTarget LargeGameplayBuffer { get; }
    public VirtualRenderTarget LargeDisplacementBuffer { get; }
    public VirtualRenderTarget LargeDisplacedGameplayBuffer { get; }
    public VirtualRenderTarget LargeLevelBuffer { get; }

    public VirtualRenderTarget LargeTempABuffer { get; }
    public VirtualRenderTarget LargeTempBBuffer { get; }

    public Matrix ScaleMatrix;

    public bool FixMatrices = true;

    public SmoothParallaxRenderer(
        VirtualRenderTarget largeGameplayBuffer,
        VirtualRenderTarget largeDisplacementBuffer,
        VirtualRenderTarget largeDisplacedGameplayBuffer,
        VirtualRenderTarget largeLevelBuffer,
        VirtualRenderTarget largeTempABuffer,
        VirtualRenderTarget largeTempBBuffer
    )
    {
        LargeGameplayBuffer = largeGameplayBuffer;
        LargeDisplacementBuffer = largeDisplacementBuffer;
        LargeDisplacedGameplayBuffer = largeDisplacedGameplayBuffer;
        LargeLevelBuffer = largeLevelBuffer;

        LargeTempABuffer = largeTempABuffer;
        LargeTempBBuffer = largeTempBBuffer;

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
            GameplayBuffers.Create(1920, 1080),
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