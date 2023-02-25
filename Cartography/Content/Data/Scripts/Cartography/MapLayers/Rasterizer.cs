using System;
using System.Collections.Generic;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public struct RenderContext
    {
        public readonly byte[] Pixels;
        public readonly int PixelStride;
        public readonly int RowStride;
        public readonly Vector2I Size;

        public readonly byte[] Stencil;
        public StencilRule StencilRule;

        public RenderContext(byte[] pixels, byte[] stencil, Vector2I size, int rowStride, int pixelStride)
        {
            Pixels = pixels;
            Stencil = stencil;
            RowStride = rowStride;
            PixelStride = pixelStride;
            Size = size;
            StencilRule = StencilRule.AlwaysPass;
        }

        private void PaintPixel(int pos, Color color)
        {
            var stencilVal = Stencil[pos >> 2];
            if (stencilVal < StencilRule.MinValue || stencilVal >= StencilRule.MaxValue)
                return;
            // Don't bother doing alpha compositing, things don't really overlap
            Pixels[pos] = color.R;
            Pixels[pos + 1] = color.G;
            Pixels[pos + 2] = color.B;
            Pixels[pos + 3] = color.A;
        }

        public void DrawLine(Vector2I p0, Vector2I p1, Color color)
        {
            if (!CohenSutherlandClip(Size, ref p0, ref p1))
                return;
            if (p0 == p1)
                PaintPixel(p0.Y * RowStride + p0.X * PixelStride, color);
            else
                DrawLineInternal(p0, p1, color);
        }

        public void DrawPolygon(List<Vector2I> loop, Color? fillColor, FillStyle fillStyle, Color? strokeColor)
        {
            if (fillColor.HasValue)
            {
                using (PoolManager.Get(out PolygonRasterHelper helper))
                {
                    helper.Init(ref this, loop);
                    helper.Raster(ref this, fillColor.Value, fillStyle);
                }
            }

            if (!strokeColor.HasValue)
                return;
            var prev = loop[loop.Count - 1];
            foreach (var curr in loop)
            {
                DrawLine(prev, curr, strokeColor.Value);
                prev = curr;
            }
        }

        private sealed class PolygonRasterHelper
        {
            private struct Edge
            {
                public int MinY, MaxY;
                public float CurrentX;
                public float StepXPerY;
            }

            private Edge[] _edges;
            private int[] _crossings;
            private int _edgeCount;
            private int _edgeCursor;
            private int _crossingsCount;

            public void Init(ref RenderContext ctx, List<Vector2I> loop)
            {
                if (_edges == null || _edges.Length < loop.Count)
                    Array.Resize(ref _edges, MathHelper.GetNearestBiggerPowerOfTwo(loop.Count));
                if (_crossings == null || _crossings.Length < loop.Count)
                    Array.Resize(ref _crossings, MathHelper.GetNearestBiggerPowerOfTwo(loop.Count));
                _edgeCount = 0;
                var prev = loop[loop.Count - 1];
                foreach (var curr in loop)
                {
                    if (curr.Y != prev.Y && (curr.Y >= 0 || prev.Y >= 0) && (curr.Y < ctx.Size.Y || prev.Y < ctx.Size.Y))
                    {
                        ref var edge = ref _edges[_edgeCount++];
                        if (curr.Y < prev.Y)
                        {
                            edge.MinY = curr.Y;
                            edge.MaxY = prev.Y;
                            edge.CurrentX = curr.X;
                            edge.StepXPerY = (prev.X - curr.X) / (float)(prev.Y - curr.Y);
                        }
                        else
                        {
                            edge.MinY = prev.Y;
                            edge.MaxY = curr.Y;
                            edge.CurrentX = prev.X;
                            edge.StepXPerY = (curr.X - prev.X) / (float)(curr.Y - prev.Y);
                        }

                        if (edge.MinY < 0)
                        {
                            edge.CurrentX += edge.StepXPerY * -edge.MinY;
                            edge.MinY = 0;
                        }
                    }

                    prev = curr;
                }

                Array.Sort(_edges, 0, _edgeCount, EdgeSortByMinY);
                _edgeCursor = 0;
                _crossingsCount = 0;
            }

            public void Raster(ref RenderContext ctx, Color color, FillStyle style)
            {
                if (_edgeCount == 0)
                    return;
                var y = Math.Max(0, _edges[0].MinY);
                var offset = y * ctx.RowStride;
                while (y < ctx.Size.Y)
                {
                    // Drop expired crossings, advance non-expired
                    var dropped = 0;
                    for (var i = 0; i < _crossingsCount; i++)
                    {
                        var cross = _crossings[i];
                        ref var edge = ref _edges[cross];
                        if (edge.MaxY <= y) // End at EXCLUSIVE max y, otherwise vertices are counted as two edges.
                        {
                            dropped++;
                            continue;
                        }

                        edge.CurrentX += edge.StepXPerY;
                        if (dropped > 0)
                            _crossings[i - dropped] = cross;
                    }

                    _crossingsCount -= dropped;

                    // Collect new crossings
                    while (_edgeCursor < _edgeCount && _edges[_edgeCursor].MinY <= y)
                    {
                        _crossings[_crossingsCount++] = _edgeCursor;
                        _edgeCursor++;
                    }

                    if (_edgeCursor >= _edgeCount && _crossingsCount == 0)
                        break;

                    // Re-sort crossings
                    Array.Sort(_crossings, 0, _crossingsCount, CrossingSortByX);

                    for (var i = 0; i < _crossingsCount - 1; i += 2)
                    {
                        var from = Math.Max(0, (int)Math.Round(_edges[_crossings[i]].CurrentX));
                        var to = Math.Min(ctx.Size.X - 1, (int)Math.Round(_edges[_crossings[i + 1]].CurrentX));
                        var pos = offset + from * ctx.PixelStride;
                        for (var x = from; x <= to; x++)
                        {
                            if (TestFillStyle(x, y, style))
                                ctx.PaintPixel(pos, color);

                            pos += ctx.PixelStride;
                        }
                    }

                    offset += ctx.RowStride;
                    y++;
                }
            }

            private static bool TestFillStyle(int x, int y, FillStyle style)
            {
                switch (style)
                {
                    case FillStyle.Solid:
                        return true;
                    case FillStyle.DashHorizontal:
                        return (y & 3) == 0;
                    case FillStyle.DashVertical:
                        return (x & 3) == 0;
                    case FillStyle.DashDiagonalTopLeftBottomRight:
                        return (x - y) % 9 == 0;
                    case FillStyle.DashDiagonalTopRightBottomLeft:
                        return (x + y) % 9 == 0;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(style), style, null);
                }
            }

            private sealed class EdgeSorter : IComparer<Edge>
            {
                public int Compare(Edge x, Edge y) => x.MinY.CompareTo(y.MinY);
            }

            private sealed class CrossingSorter : IComparer<int>
            {
                private readonly PolygonRasterHelper _helper;

                public CrossingSorter(PolygonRasterHelper helper) => _helper = helper;

                public int Compare(int x, int y) => _helper._edges[x].CurrentX.CompareTo(_helper._edges[y].CurrentX);
            }

            private static readonly EdgeSorter EdgeSortByMinY = new EdgeSorter();
            private readonly CrossingSorter CrossingSortByX;

            public PolygonRasterHelper()
            {
                CrossingSortByX = new CrossingSorter(this);
            }
        }

        #region Line Raster

        [Flags]
        private enum OutCode
        {
            Inside = 0,
            Left = 1,
            Right = 2,
            Bottom = 4,
            Top = 8
        }

        private static bool CohenSutherlandClip(Vector2I size, ref Vector2I p0, ref Vector2I p1)
        {
            var inclusiveSize = size - 1;

            // https://en.wikipedia.org/wiki/Cohen%E2%80%93Sutherland_algorithm
            OutCode ComputeOutCode(Vector2I pt)
            {
                var code = OutCode.Inside;
                if (pt.X < 0)
                    code |= OutCode.Left;
                else if (pt.X > inclusiveSize.X)
                    code |= OutCode.Right;
                if (pt.Y < 0)
                    code |= OutCode.Bottom;
                else if (pt.Y > inclusiveSize.Y)
                    code |= OutCode.Top;
                return code;
            }

            var code0 = ComputeOutCode(p0);
            var code1 = ComputeOutCode(p1);
            while (true)
            {
                if ((code0 | code1) == 0)
                    return true;
                if ((code0 & code1) != 0)
                    return false;
                Vector2I next = default;
                var codeOut = code1 > code0 ? code1 : code0;
                var delta = p1 - p0;
                if ((codeOut & OutCode.Top) != 0)
                {
                    next.X = p0.X + delta.X * (inclusiveSize.Y - p0.Y) / delta.Y;
                    next.Y = inclusiveSize.Y;
                }
                else if ((codeOut & OutCode.Bottom) != 0)
                {
                    next.X = p0.X + delta.X * -p0.Y / delta.Y;
                    next.Y = 0;
                }
                else if ((codeOut & OutCode.Right) != 0)
                {
                    next.Y = p0.Y + delta.Y * (inclusiveSize.X - p0.X) / delta.X;
                    next.X = inclusiveSize.X;
                }
                else if ((codeOut & OutCode.Left) != 0)
                {
                    next.Y = p0.Y + delta.Y * -p0.X / delta.X;
                    next.X = 0;
                }
                else
                {
                    throw new ArgumentException();
                }

                if (codeOut == code0)
                {
                    p0 = next;
                    code0 = ComputeOutCode(p0);
                }
                else
                {
                    p1 = next;
                    code1 = ComputeOutCode(p1);
                }
            }
        }

        private void DrawLineInternal(Vector2I p0, Vector2I p1, Color color)
        {
            var dx = Math.Abs(p1.X - p0.X);
            var dy = -Math.Abs(p1.Y - p0.Y);

            var pos = p0.Y * RowStride + p0.X * PixelStride;
            var end = p1.Y * RowStride + p1.X * PixelStride;

            var sx = p0.X < p1.X ? PixelStride : -PixelStride;
            var sy = p0.Y < p1.Y ? RowStride : -RowStride;
            var err = dx + dy;
            while (true)
            {
                if (pos == end) break;
                PaintPixel(pos, color);
                var e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    pos += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    pos += sy;
                }
            }
        }

        #endregion
    }

    public readonly struct StencilRule
    {
        public readonly int MinValue;
        public readonly int MaxValue;

        public static readonly StencilRule AlwaysPass = new StencilRule(0, 256);

        public StencilRule(int minValue, int maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }
    } 

    public enum FillStyle
    {
        Solid,
        DashHorizontal,
        DashVertical,
        DashDiagonalTopLeftBottomRight,
        DashDiagonalTopRightBottomLeft,
    }
}