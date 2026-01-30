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

        OriginalGameplayBuffer = GameplayBuffers.Gameplay;
        OriginalLevelBuffer = GameplayBuffers.Level;
        OriginalTempABuffer = GameplayBuffers.TempA;
        OriginalTempBBuffer = GameplayBuffers.TempB;

        SmallBuffer = smallBuffer;

		GaussianBlurTempBuffer = gaussianBlurTempBuffer;

        Visible = true;
    }

    public static void Load()
    {

    }

    public static HiresRenderer Create()
    {
        Destroy();

		int vanillaWidth = GameplayBuffers.Gameplay.Width;
		int vanillaHeight = GameplayBuffers.Gameplay.Height;

        int scale = (int) Math.Ceiling(1920f / vanillaWidth);

		int largeWidth = vanillaWidth * scale;
		int largeHeight = vanillaHeight * scale;

		HiresCameraSmoother.Scale = scale;

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