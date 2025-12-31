using Monocle;
using System;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public class HiresRenderer : Renderer
{
    public static HiresRenderer Instance { get; private set; }

    public VirtualRenderTarget LargeGameplayBuffer { get; }
    public VirtualRenderTarget LargeLevelBuffer { get; }
    public VirtualRenderTarget LargeTempABuffer { get; }
    public VirtualRenderTarget LargeTempBBuffer { get; }

    public VirtualRenderTarget SmallBuffer { get; }

	// Used when some other code got itself confused and is trying
	// to use a missized buffer as a temp buffer.
	public VirtualRenderTarget GaussianBlurTempBuffer { get; }

    public static VirtualRenderTarget OriginalGameplayBuffer = null;
	public static VirtualRenderTarget OriginalLevelBuffer = null;
    public static VirtualRenderTarget OriginalTempABuffer = null;

    public static VirtualRenderTarget OriginalTempBBuffer = null;

    public HiresRenderer(
        VirtualRenderTarget largeGameplayBuffer,
        VirtualRenderTarget largeLevelBuffer,
        VirtualRenderTarget largeTempABuffer,
        VirtualRenderTarget largeTempBBuffer,
        VirtualRenderTarget smallBuffer,
		VirtualRenderTarget gaussianBlurTempBuffer
    ) {
        LargeGameplayBuffer = largeGameplayBuffer;
        LargeLevelBuffer = largeLevelBuffer;
        LargeTempABuffer = largeTempABuffer;
        LargeTempBBuffer = largeTempBBuffer;

        SmallBuffer = smallBuffer;

		GaussianBlurTempBuffer = gaussianBlurTempBuffer;

        Visible = true;
    }

    public static void Load()
    {

    }

	public static void EnableLargeGameplayBuffer()
    {
        if (Instance == null || GameplayBuffers.Gameplay == Instance.LargeGameplayBuffer)
        {
            return;
        }

        OriginalGameplayBuffer = GameplayBuffers.Gameplay;
        GameplayBuffers.Gameplay = Instance.LargeGameplayBuffer;
    }

    public static void DisableLargeGameplayBuffer()
    {
        if (OriginalGameplayBuffer == null) { return; }

        GameplayBuffers.Gameplay = OriginalGameplayBuffer;
        OriginalGameplayBuffer = null;
    }

    public static void EnableLargeLevelBuffer()
    {
        if (Instance == null || GameplayBuffers.Level == Instance.LargeLevelBuffer)
        {
            return;
        }

        OriginalLevelBuffer = GameplayBuffers.Level;
        GameplayBuffers.Level = Instance.LargeLevelBuffer;
    }

    public static void DisableLargeLevelBuffer()
    {
        if (OriginalLevelBuffer == null) { return; }

        GameplayBuffers.Level = OriginalLevelBuffer;
        OriginalLevelBuffer = null;
    }

    public static void EnableLargeTempABuffer()
    {
        if (Instance == null || GameplayBuffers.TempA == Instance.LargeTempABuffer)
        {
            return;
        }

        OriginalTempABuffer = GameplayBuffers.TempA;
        GameplayBuffers.TempA = Instance.LargeTempABuffer;
    }

    public static void DisableLargeTempABuffer()
    {
        if (OriginalTempABuffer == null) { return; }

        GameplayBuffers.TempA = OriginalTempABuffer;
        OriginalTempABuffer = null;
    }

    public static void EnableLargeTempBBuffer()
    {
        if (Instance == null || GameplayBuffers.TempB == Instance.LargeTempBBuffer)
        {
            return;
        }

        OriginalTempBBuffer = GameplayBuffers.TempB;
        GameplayBuffers.TempB = Instance.LargeTempBBuffer;
    }

    public static void DisableLargeTempBBuffer()
    {
        if (OriginalTempBBuffer == null) { return; }

        GameplayBuffers.TempB = OriginalTempBBuffer;
        OriginalTempBBuffer = null;
    }

    public static HiresRenderer Create()
    {
        Destroy();

		int vanillaWidth = GameplayBuffers.Gameplay.Width;
		int vanillaHeight = GameplayBuffers.Gameplay.Height;

		int largeWidth = vanillaWidth * 6;
		int largeHeight = vanillaHeight * 6;

		Instance = new HiresRenderer(
			GameplayBuffers.Create(largeWidth, largeHeight),
			GameplayBuffers.Create(largeWidth, largeHeight),
			GameplayBuffers.Create(largeWidth, largeHeight),
			GameplayBuffers.Create(largeWidth, largeHeight),
			GameplayBuffers.Create(vanillaWidth, vanillaHeight),
			GameplayBuffers.Create(vanillaWidth, vanillaHeight)
		);

		return Instance;
    }

    public static void Destroy()
    {
        DisableLargeLevelBuffer();
		DisableLargeGameplayBuffer();
		DisableLargeTempABuffer();
        DisableLargeTempBBuffer();

        if (Instance != null)
        {
            Instance.LargeLevelBuffer?.Dispose();
            Instance.LargeGameplayBuffer?.Dispose();
            Instance.LargeTempABuffer?.Dispose();
            Instance.LargeTempBBuffer?.Dispose();

            Instance.SmallBuffer?.Dispose();

			Instance.GaussianBlurTempBuffer?.Dispose();

            Instance = null;
        }
    }
}