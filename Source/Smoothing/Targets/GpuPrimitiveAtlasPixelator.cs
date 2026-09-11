using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

/// <summary>
/// Pixelates independent triangle groups on a fixed low-resolution phase, packs the
/// visible pieces into a low-resolution atlas, then composites them in source order.
/// </summary>
internal sealed class GpuPrimitiveAtlasPixelator : IDisposable
{
    private const int AtlasWidth = 1024;
    private const int AtlasHeight = 1024;
    // Keep neighboring packed regions from bleeding into point-sampled UV rectangles.
    private const int TilePadding = 1;
    // Restoring the fractional phase may move an edge by less than one logical pixel.
    private const int ViewportGuard = 1;
    private const int InitialVertexCapacity = 64;
    // A triangle clipped against an axis-aligned rectangle produces at most seven vertices.
    private const int ClipBufferCapacity = 8;
    private static readonly Matrix AtlasProjection = CreatePixelToClipMatrix(AtlasWidth, AtlasHeight);
    // dest = src*0 + dest*0, so covered pixels become zero no matter what the pixel shader
    // emits. Used to blank a page region without depending on FxPrimitive's output.
    private static readonly BlendState ZeroBlend = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.Zero
    };

    private RenderTarget2D _atlas;
    private BasicEffect _composite;
    private VertexPositionColor[] _clippedVertices = Array.Empty<VertexPositionColor>();
    private VertexPositionColor[] _rasterVertices = Array.Empty<VertexPositionColor>();
    private VertexPositionColor[] _expandedIndexedVertices = Array.Empty<VertexPositionColor>();
    private VertexPositionColorTexture[] _compositeVertices = Array.Empty<VertexPositionColorTexture>();
    private readonly VertexPositionColor[] _clipA = new VertexPositionColor[ClipBufferCapacity];
    private readonly VertexPositionColor[] _clipB = new VertexPositionColor[ClipBufferCapacity];
    private readonly VertexPositionColor[] _pageClearQuad = new VertexPositionColor[6];
    private readonly List<PrimitiveGroup> _groups = new();
    private readonly List<AtlasPlacement> _placements = new();
    private int _clippedVertexCount;
    private int _pageUsedWidth;
    private int _pageUsedHeight;

    /// <summary>
    /// Set when a draw failed against the graphics device. The path retires itself rather
    /// than letting the exception escape through the hooked GFX.DrawVertices, and stays
    /// retired until <see cref="Dispose"/> releases the resources.
    /// </summary>
    internal bool Faulted { get; private set; }

    /// <summary>
    /// Lets a faulted path try again without discarding the atlas. Called at level
    /// transitions, so a one-off device problem doesn't cost pixelation for the whole
    /// session while a genuinely broken device still retires after one logged failure.
    /// </summary>
    internal void ClearFault()
    {
        Faulted = false;
    }

    internal static bool SupportsMatrix(Matrix matrix)
    {
        // Bake any finite 2D affine transform into the logical-space geometry before
        // phase selection and clipping. Perspective and XY/Z coupling still use the
        // normal high-resolution fallback. M33 is intentionally unrestricted because
        // Matrix.CreateScale(float) also scales Z even when all 2D vertices have Z=0.
        return matrix is
        {
            M13: 0f, M14: 0f,
            M23: 0f, M24: 0f,
            M31: 0f, M32: 0f, M34: 0f,
            M43: 0f, M44: 1f
        } && IsFinite(matrix);
    }

    internal bool TryExpandIndexed(VertexPositionColor[] vertices, int vertexCount,
        int[] indices, int primitiveCount, out VertexPositionColor[] expanded,
        out int expandedCount)
    {
        expanded = _expandedIndexedVertices;
        expandedCount = 0;
        if (vertices == null || indices == null || primitiveCount < 0
            || vertexCount < 0 || vertexCount > vertices.Length
            || primitiveCount > int.MaxValue / 3 || primitiveCount > indices.Length / 3)
            return false;

        expandedCount = primitiveCount * 3;
        if (_expandedIndexedVertices.Length < expandedCount)
            Array.Resize(ref _expandedIndexedVertices,
                Math.Max(expandedCount, _expandedIndexedVertices.Length * 2 + InitialVertexCapacity));
        for (int i = 0; i < expandedCount; i++)
        {
            int index = indices[i];
            if ((uint)index >= (uint)vertexCount)
            {
                expandedCount = 0;
                return false;
            }
            _expandedIndexedVertices[i] = vertices[index];
        }

        expanded = _expandedIndexedVertices;
        return true;
    }

    internal bool TryDraw(
        GraphicsDevice device,
        RenderTargetBinding[] targets,
        Matrix matrix,
        VertexPositionColor[] vertices,
        int vertexCount,
        int lowWidth,
        int lowHeight,
        float scale)
    {
        int usableVertexCount = vertexCount - vertexCount % 3;
        if (!TryBuildGroups(vertices, usableVertexCount, lowWidth, lowHeight, matrix)
            || _groups.Count == 0 || !CanFitEveryGroupOnEmptyPage())
            return false;

        Viewport viewport = device.Viewport;
        Rectangle scissor = device.ScissorRectangle;
        DepthStencilState depth = device.DepthStencilState;
        Texture texture = device.Textures[0];
        SamplerState sampler = device.SamplerStates[0];
        // Match the state GFX.DrawVertices would leave behind when the interception
        // returns early from that method.
        Matrix finalWorld = matrix * Matrix.CreateScale(scale)
            * CreatePixelToClipMatrix(viewport.Width, viewport.Height);

        bool anyPageComposited = false;
        try
        {
            EnsureResources(device);
            int groupIndex = 0;
            while (groupIndex < _groups.Count)
            {
                int nextGroup = PackPage(groupIndex);
                RasterizePage(device);
                CompositePage(device, targets, viewport, scale);
                anyPageComposited = true;
                groupIndex = nextGroup;
            }
        }
        catch (Exception e)
        {
            // This runs inside a hooked GFX.DrawVertices, so an escaping graphics
            // exception would take the game down mid-frame. Retire the path instead and
            // let every later call fall back to the normal high-resolution draw.
            Faulted = true;
            Logger.Log(LogLevel.Error, "MotionSmoothingModule",
                $"GPU primitive atlas disabled after a draw failure: {e}");
            // Pages already composited are on the target. Claiming the draw avoids
            // blending the same geometry a second time through the fallback.
            return anyPageComposited;
        }
        finally
        {
            // An exception thrown from here would replace the one being handled above and
            // still escape into the game's render loop, so restoration is best-effort.
            try
            {
                device.SetRenderTargets(targets);
                device.Viewport = viewport;
                device.ScissorRectangle = scissor;
                device.DepthStencilState = depth;
                device.RasterizerState = RasterizerState.CullNone;
                device.BlendState = BlendState.AlphaBlend;
                GFX.FxPrimitive.Parameters["World"].SetValue(finalWorld);
                GFX.FxPrimitive.CurrentTechnique.Passes[0].Apply();
                device.Textures[0] = texture;
                device.SamplerStates[0] = sampler;
            }
            catch (Exception e)
            {
                Faulted = true;
                Logger.Log(LogLevel.Error, "MotionSmoothingModule",
                    $"GPU primitive atlas failed to restore render state: {e}");
            }
        }

        return true;
    }

    private bool CanFitEveryGroupOnEmptyPage()
    {
        foreach (PrimitiveGroup group in _groups)
        {
            int packedWidth = group.Bounds.Width + TilePadding * 2;
            int packedHeight = group.Bounds.Height + TilePadding * 2;
            if (TilePadding + packedWidth > AtlasWidth
                || TilePadding + packedHeight > AtlasHeight)
                return false;
        }

        return true;
    }

    private bool TryBuildGroups(VertexPositionColor[] vertices, int vertexCount,
        int lowWidth, int lowHeight, Matrix matrix)
    {
        _groups.Clear();
        _clippedVertexCount = 0;
        if (vertexCount < 3)
            return true;

        int groupStart = 0;
        for (int triangle = 3; triangle < vertexCount; triangle += 3)
        {
            if (!SharesVertexWithPreviousTriangle(vertices, triangle))
            {
                if (!TryAddClippedGroup(vertices, groupStart, triangle - groupStart,
                        lowWidth, lowHeight, matrix))
                {
                    _groups.Clear();
                    _clippedVertexCount = 0;
                    return false;
                }
                groupStart = triangle;
            }
        }

        if (!TryAddClippedGroup(vertices, groupStart, vertexCount - groupStart,
                lowWidth, lowHeight, matrix))
        {
            _groups.Clear();
            _clippedVertexCount = 0;
            return false;
        }

        return true;
    }

    private static bool SharesVertexWithPreviousTriangle(VertexPositionColor[] vertices, int start)
    {
        for (int current = start; current < start + 3; current++)
            for (int previous = start - 3; previous < start; previous++)
                if (vertices[current].Position == vertices[previous].Position)
                    return true;
        return false;
    }

    private bool TryAddClippedGroup(VertexPositionColor[] source, int sourceStart,
        int sourceCount, int lowWidth, int lowHeight, Matrix matrix)
    {
        // Transform first, then select phase and clip in the same logical coordinate
        // system that a native low-resolution GFX.DrawVertices call would use.
        Vector3 anchor = Vector3.Transform(source[sourceStart].Position, matrix);
        if (!HasFiniteXy(anchor))
            return false;
        Vector2 phase = new(anchor.X - MathF.Floor(anchor.X), anchor.Y - MathF.Floor(anchor.Y));
        int outputStart = _clippedVertexCount;
        float left = -ViewportGuard;
        float top = -ViewportGuard;
        float right = lowWidth + ViewportGuard;
        float bottom = lowHeight + ViewportGuard;

        for (int triangle = sourceStart; triangle < sourceStart + sourceCount; triangle += 3)
        {
            for (int i = 0; i < 3; i++)
            {
                VertexPositionColor vertex = source[triangle + i];
                vertex.Position = Vector3.Transform(vertex.Position, matrix);
                if (!HasFiniteXy(vertex.Position))
                {
                    _clippedVertexCount = outputStart;
                    return false;
                }
                vertex.Position.X -= phase.X;
                vertex.Position.Y -= phase.Y;
                _clipA[i] = vertex;
            }

            int count = ClipEdge(_clipA, 3, _clipB, ClipAxis.X, left, keepGreater: true);
            count = ClipEdge(_clipB, count, _clipA, ClipAxis.X, right, keepGreater: false);
            count = ClipEdge(_clipA, count, _clipB, ClipAxis.Y, top, keepGreater: true);
            count = ClipEdge(_clipB, count, _clipA, ClipAxis.Y, bottom, keepGreater: false);
            if (count < 3)
                continue;

            EnsureClippedCapacity(_clippedVertexCount + (count - 2) * 3);
            for (int i = 1; i < count - 1; i++)
            {
                _clippedVertices[_clippedVertexCount++] = _clipA[0];
                _clippedVertices[_clippedVertexCount++] = _clipA[i];
                _clippedVertices[_clippedVertexCount++] = _clipA[i + 1];
            }
        }

        int outputCount = _clippedVertexCount - outputStart;
        if (outputCount == 0)
            return true;

        Rectangle allowed = new(-ViewportGuard, -ViewportGuard,
            lowWidth + ViewportGuard * 2, lowHeight + ViewportGuard * 2);
        Rectangle bounds = Rectangle.Intersect(
            GetRasterBounds(_clippedVertices, outputStart, outputCount), allowed);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _clippedVertexCount = outputStart;
            return true;
        }

        _groups.Add(new PrimitiveGroup(outputStart, outputCount, bounds, phase));
        return true;
    }

    private static int ClipEdge(VertexPositionColor[] input, int inputCount,
        VertexPositionColor[] output, ClipAxis axis, float value, bool keepGreater)
    {
        if (inputCount == 0)
            return 0;

        int outputCount = 0;
        VertexPositionColor previous = input[inputCount - 1];
        bool previousInside = IsInside(previous, axis, value, keepGreater);
        for (int i = 0; i < inputCount; i++)
        {
            VertexPositionColor current = input[i];
            bool currentInside = IsInside(current, axis, value, keepGreater);
            if (currentInside != previousInside)
                output[outputCount++] = Intersect(previous, current, axis, value);
            if (currentInside)
                output[outputCount++] = current;
            previous = current;
            previousInside = currentInside;
        }
        return outputCount;
    }

    private static bool IsInside(VertexPositionColor vertex, ClipAxis axis, float value, bool keepGreater)
    {
        float coordinate = axis == ClipAxis.X ? vertex.Position.X : vertex.Position.Y;
        return keepGreater ? coordinate >= value : coordinate <= value;
    }

    private static VertexPositionColor Intersect(VertexPositionColor a, VertexPositionColor b,
        ClipAxis axis, float value)
    {
        float aCoordinate = axis == ClipAxis.X ? a.Position.X : a.Position.Y;
        float bCoordinate = axis == ClipAxis.X ? b.Position.X : b.Position.Y;
        float t = (float)(((double)value - aCoordinate)
            / ((double)bCoordinate - aCoordinate));
        Vector3 position = Vector3.Lerp(a.Position, b.Position, t);
        if (axis == ClipAxis.X)
            position.X = value;
        else
            position.Y = value;
        Color color = new(Vector4.Lerp(a.Color.ToVector4(), b.Color.ToVector4(), t));
        return new VertexPositionColor(position, color);
    }

    private static Rectangle GetRasterBounds(VertexPositionColor[] vertices, int start, int count)
    {
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        for (int i = start; i < start + count; i++)
        {
            Vector3 position = vertices[i].Position;
            minX = Math.Min(minX, position.X);
            minY = Math.Min(minY, position.Y);
            maxX = Math.Max(maxX, position.X);
            maxY = Math.Max(maxY, position.Y);
        }

        int x = (int)MathF.Floor(minX) - 1;
        int y = (int)MathF.Floor(minY) - 1;
        int right = (int)MathF.Ceiling(maxX) + 1;
        int bottom = (int)MathF.Ceiling(maxY) + 1;
        return new Rectangle(x, y, right - x, bottom - y);
    }

    private int PackPage(int firstGroup)
    {
        _placements.Clear();
        int cursorX = TilePadding;
        int cursorY = TilePadding;
        int rowHeight = 0;
        int groupIndex = firstGroup;
        bool wrapped = false;

        while (groupIndex < _groups.Count)
        {
            PrimitiveGroup group = _groups[groupIndex];
            int packedWidth = group.Bounds.Width + TilePadding * 2;
            int packedHeight = group.Bounds.Height + TilePadding * 2;

            if (cursorX + packedWidth > AtlasWidth)
            {
                cursorX = TilePadding;
                cursorY += rowHeight;
                rowHeight = 0;
                wrapped = true;
            }
            if (cursorY + packedHeight > AtlasHeight)
                break;

            _placements.Add(new AtlasPlacement(group,
                new Rectangle(cursorX + TilePadding, cursorY + TilePadding,
                    group.Bounds.Width, group.Bounds.Height)));
            cursorX += packedWidth;
            rowHeight = Math.Max(rowHeight, packedHeight);
            groupIndex++;
        }

        // Every tile sits inside this box, padding included: tiles begin at TilePadding
        // and the cursors advance past each tile's trailing padding.
        _pageUsedWidth = Math.Min(wrapped ? AtlasWidth : cursorX, AtlasWidth);
        _pageUsedHeight = Math.Min(cursorY + rowHeight, AtlasHeight);

        // CanFitEveryGroupOnEmptyPage guarantees that a fresh page always consumes
        // at least one group, so pagination cannot stall here.
        return groupIndex;
    }

    private void RasterizePage(GraphicsDevice device)
    {
        int count = 0;
        foreach (AtlasPlacement placement in _placements)
            count += placement.Group.VertexCount;
        EnsureRasterCapacity(count);

        int output = 0;
        foreach (AtlasPlacement placement in _placements)
        {
            Vector2 shift = new(
                placement.Tile.X - placement.Group.Bounds.X,
                placement.Tile.Y - placement.Group.Bounds.Y);
            for (int i = 0; i < placement.Group.VertexCount; i++)
            {
                VertexPositionColor input = _clippedVertices[placement.Group.VertexStart + i];
                input.Position.X += shift.X;
                input.Position.Y += shift.Y;
                _rasterVertices[output++] = input;
            }
        }

        // CompositePage leaves the atlas bound as Texture[0]. Unbind it before the
        // next page makes that same resource a render target again.
        device.Textures[0] = null;
        device.SetRenderTarget(_atlas);
        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.None;
        GFX.FxPrimitive.Parameters["World"].SetValue(AtlasProjection);
        GFX.FxPrimitive.CurrentTechnique.Passes[0].Apply();

        // Blank only what this page packs. GraphicsDevice.Clear would cost a megapixel of
        // fill on every call, even for a page holding a handful of small tiles. ZeroBlend
        // makes the result independent of what the shader writes, so the vertex colors
        // here are irrelevant.
        device.BlendState = ZeroBlend;
        WriteQuad(_pageClearQuad, 0f, 0f, _pageUsedWidth, _pageUsedHeight);
        device.DrawUserPrimitives(PrimitiveType.TriangleList, _pageClearQuad, 0, 2);

        device.BlendState = BlendState.AlphaBlend;
        device.DrawUserPrimitives(PrimitiveType.TriangleList, _rasterVertices, 0, count / 3);
    }

    private static void WriteQuad(VertexPositionColor[] target, float left, float top,
        float right, float bottom)
    {
        Vector3 topLeft = new(left, top, 0f);
        Vector3 topRight = new(right, top, 0f);
        Vector3 bottomRight = new(right, bottom, 0f);
        Vector3 bottomLeft = new(left, bottom, 0f);
        target[0] = new VertexPositionColor(topLeft, Color.Transparent);
        target[1] = new VertexPositionColor(topRight, Color.Transparent);
        target[2] = new VertexPositionColor(bottomRight, Color.Transparent);
        target[3] = new VertexPositionColor(topLeft, Color.Transparent);
        target[4] = new VertexPositionColor(bottomRight, Color.Transparent);
        target[5] = new VertexPositionColor(bottomLeft, Color.Transparent);
    }

    private void CompositePage(GraphicsDevice device, RenderTargetBinding[] targets,
        Viewport viewport, float scale)
    {
        int count = _placements.Count * 6;
        EnsureCompositeCapacity(count);
        int output = 0;
        foreach (AtlasPlacement placement in _placements)
        {
            PrimitiveGroup group = placement.Group;
            float left = (group.Bounds.X + group.Phase.X) * scale;
            float top = (group.Bounds.Y + group.Phase.Y) * scale;
            float right = left + group.Bounds.Width * scale;
            float bottom = top + group.Bounds.Height * scale;
            float clipLeft = left * 2f / viewport.Width - 1f;
            float clipRight = right * 2f / viewport.Width - 1f;
            float clipTop = 1f - top * 2f / viewport.Height;
            float clipBottom = 1f - bottom * 2f / viewport.Height;
            float u0 = placement.Tile.X / (float)AtlasWidth;
            float v0 = placement.Tile.Y / (float)AtlasHeight;
            float u1 = placement.Tile.Right / (float)AtlasWidth;
            float v1 = placement.Tile.Bottom / (float)AtlasHeight;

            _compositeVertices[output++] = new(new Vector3(clipLeft, clipTop, 0f), Color.White, new Vector2(u0, v0));
            _compositeVertices[output++] = new(new Vector3(clipRight, clipTop, 0f), Color.White, new Vector2(u1, v0));
            _compositeVertices[output++] = new(new Vector3(clipRight, clipBottom, 0f), Color.White, new Vector2(u1, v1));
            _compositeVertices[output++] = new(new Vector3(clipLeft, clipTop, 0f), Color.White, new Vector2(u0, v0));
            _compositeVertices[output++] = new(new Vector3(clipRight, clipBottom, 0f), Color.White, new Vector2(u1, v1));
            _compositeVertices[output++] = new(new Vector3(clipLeft, clipBottom, 0f), Color.White, new Vector2(u0, v1));
        }

        device.SetRenderTargets(targets);
        device.Viewport = viewport;
        device.BlendState = BlendState.AlphaBlend;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        _composite.Texture = _atlas;
        _composite.CurrentTechnique.Passes[0].Apply();
        // After Apply, so that an effect which commits its own sampler state cannot leave
        // the atlas being filtered. Point sampling is what makes the tiles read as pixels.
        device.SamplerStates[0] = SamplerState.PointClamp;
        device.DrawUserPrimitives(PrimitiveType.TriangleList, _compositeVertices, 0, count / 3);
    }

    private void EnsureResources(GraphicsDevice device)
    {
        if (_atlas == null || _atlas.IsDisposed || !ReferenceEquals(_atlas.GraphicsDevice, device))
        {
            _atlas?.Dispose();
            _atlas = new RenderTarget2D(device, AtlasWidth, AtlasHeight, false, SurfaceFormat.Color,
                DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        }
        if (_composite == null || _composite.IsDisposed
            || !ReferenceEquals(_composite.GraphicsDevice, device))
        {
            _composite?.Dispose();
            _composite = new BasicEffect(device)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                World = Matrix.Identity,
                View = Matrix.Identity,
                Projection = Matrix.Identity
            };
        }
    }

    private void EnsureClippedCapacity(int count)
    {
        EnsureCapacity(ref _clippedVertices, count);
    }

    private void EnsureRasterCapacity(int count)
    {
        EnsureCapacity(ref _rasterVertices, count);
    }

    private void EnsureCompositeCapacity(int count)
    {
        EnsureCapacity(ref _compositeVertices, count);
    }

    private static void EnsureCapacity<T>(ref T[] buffer, int count)
    {
        if (buffer.Length < count)
            Array.Resize(ref buffer, Math.Max(count, buffer.Length * 2 + InitialVertexCapacity));
    }

    private static Matrix CreatePixelToClipMatrix(int width, int height)
    {
        return Matrix.CreateScale(2f / width, -2f / height, 1f)
            * Matrix.CreateTranslation(-1f, 1f, 0f);
    }

    private static bool HasFiniteXy(Vector3 position)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y);
    }

    private static bool IsFinite(Matrix matrix)
    {
        return float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12)
            && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
            && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22)
            && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
            && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32)
            && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
            && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42)
            && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
    }

    /// <summary>
    /// Releases the graphics resources and the scratch buffers. The instance stays usable:
    /// the next <see cref="TryDraw"/> rebuilds whatever it needs, and a previous fault is
    /// cleared so a transient device problem gets another chance.
    /// </summary>
    public void Dispose()
    {
        _atlas?.Dispose();
        _atlas = null;
        _composite?.Dispose();
        _composite = null;
        Faulted = false;

        // These grow to the worst case a session has seen and never shrink on their own.
        // Clipping can emit up to five triangles per input triangle, so a heavy scene can
        // strand several megabytes here for the rest of the run.
        _clippedVertices = Array.Empty<VertexPositionColor>();
        _rasterVertices = Array.Empty<VertexPositionColor>();
        _expandedIndexedVertices = Array.Empty<VertexPositionColor>();
        _compositeVertices = Array.Empty<VertexPositionColorTexture>();
        _clippedVertexCount = 0;
        _groups.Clear();
        _groups.TrimExcess();
        _placements.Clear();
        _placements.TrimExcess();
    }

    private enum ClipAxis
    {
        X,
        Y
    }

    private readonly record struct PrimitiveGroup(int VertexStart, int VertexCount,
        Rectangle Bounds, Vector2 Phase);
    private readonly record struct AtlasPlacement(PrimitiveGroup Group, Rectangle Tile);
}
