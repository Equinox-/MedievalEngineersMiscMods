using System;
using System.Collections.Generic;
using Equinox76561198048419394.Cartography.Utils;
using VRage.Library.Collections;
using VRage.Logging;
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
            if (!StencilRule.Test(stencilVal))
                return;
            // Don't bother doing alpha compositing, things don't really overlap
            Pixels[pos] = color.R;
            Pixels[pos + 1] = color.G;
            Pixels[pos + 2] = color.B;
            Pixels[pos + 3] = color.A;
        }

        private void PaintPixel(int x, int y, Color color, bool boundsCheck = false)
        {
            if (boundsCheck && (x < 0 || y < 0 || x >= Size.X || y >= Size.Y))
                return;
            PaintPixel(x * PixelStride + y * RowStride, color);
        }

        private void PaintPixel(Vector2I pos, Color color, bool boundsCheck = false) => PaintPixel(pos.X, pos.Y, color, boundsCheck);

        private void PaintRect(int x, int y, int width, int height, Color color, bool boundsCheck = false)
        {
            if (boundsCheck)
            {
                if (x >= Size.X || y >= Size.Y)
                    return;
                if (x < 0)
                {
                    width += x;
                    x = 0;
                }

                if (y < 0)
                {
                    height += y;
                    y = 0;
                }

                if (width <= 0 || height <= 0)
                    return;
            }

            var pos = x * PixelStride + y * RowStride;
            var modifiedRowStride = RowStride - width * PixelStride;
            for (var cy = 0; cy < height; cy++)
            {
                for (var cx = 0; cx < width; cx++)
                {
                    PaintPixel(pos, color);
                    pos += PixelStride;
                }

                pos += modifiedRowStride;
            }
        }

        public void DrawLine<TWidth>(Vector2I p0, Vector2I p1, Color color, ref TWidth widthPainter) where TWidth : struct, ILineWidthPainter
        {
            if (!CohenSutherlandClip(Size, ref p0, ref p1))
                return;
            if (p0 == p1)
                PaintPixel(p0.Y * RowStride + p0.X * PixelStride, color);
            else
                DrawLineInternal(p0, p1, color, ref widthPainter);
        }

        public void DrawPolygon<TWidth>(List<Vector2I> loop, Color? fillColor, FillStyle fillStyle, Color? strokeColor, ref TWidth widthPainter)
            where TWidth : struct, ILineWidthPainter
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
                DrawLine(prev, curr, strokeColor.Value, ref widthPainter);
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
                    Array.Sort(_crossings, 0, _crossingsCount, _crossingSortByX);

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
            private readonly CrossingSorter _crossingSortByX;

            public PolygonRasterHelper()
            {
                _crossingSortByX = new CrossingSorter(this);
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

        private void DrawLineInternal<TWidth>(Vector2I p0, Vector2I p1, Color color, ref TWidth widthPattern) where TWidth : struct, ILineWidthPainter
        {
            var delta = p1 - p0;
            var step = Vector2I.One;

            void FlipDelta(ref int deltaValue, ref int stepValue)
            {
                if (deltaValue < 0)
                {
                    stepValue = -1;
                    deltaValue = -deltaValue;
                }
                else if (deltaValue == 0)
                {
                    stepValue = 0;
                }
            }

            FlipDelta(ref delta.X, ref step.X);
            FlipDelta(ref delta.Y, ref step.Y);

            var constantWidth = widthPattern.ConstantWidth;
            // Vertical or horizontal line of constant width
            if (constantWidth.HasValue && (delta.X == 0 || delta.Y == 0))
            {
                var min = Vector2I.Min(p0, p1);
                var max = Vector2I.Max(p0, p1);
                var width = constantWidth.Value;
                var half = (constantWidth.Value - 1) / 2;
                if (delta.X == 0)
                    PaintRect(min.X - half, min.Y, width, max.Y - min.Y + 1, color, true);
                else
                    PaintRect(min.X, min.Y - half, max.X - min.X + 1, width, color, true);
            }
            // Complex line of 1 pixel width
            else if (constantWidth <= 1)
                DrawSinglePixelLineInternal(p0, p1, delta, step, color);
            // Complex line of constant >1 pixel width
            else if (constantWidth.HasValue)
                DrawConstantWidthLineInternal(p0, p1, delta, step, constantWidth.Value, color);
            // Horizontal line of variable width
            else if (delta.Y == 0)
            {
                for (var x = p0.X; x <= p1.X; x += step.X)
                {
                    var width = widthPattern.NextWidth();
                    var half = (width - 1) / 2;
                    PaintRect(x, p0.Y - half, 1, width, color, true);
                }
            }
            // Vertical line of variable width
            else if (delta.X == 0)
            {
                for (var y = p0.Y; y <= p1.Y; y += step.Y)
                {
                    var width = widthPattern.NextWidth();
                    var half = (width - 1) / 2;
                    PaintRect(p0.X - half, y, width, 1, color, true);
                }
            }
            // Complex line of variable width
            else
                new VariableWidthLinePainter<TWidth>(p0, delta, step, color).Execute(ref this, ref widthPattern);
        }

        private void DrawSinglePixelLineInternal(Vector2I p0, Vector2I p1, Vector2I delta, Vector2I step, Color color)
        {
            var err = delta.X - delta.Y;
            var stepPosX = step.X * PixelStride;
            var stepPosY = step.Y * RowStride;
            var pos = p0.Y * RowStride + p0.X * PixelStride;
            var end = p1.Y * RowStride + p1.X * PixelStride;

            while (true)
            {
                if (pos == end) break;
                PaintPixel(pos, color);
                var e2 = 2 * err;
                if (e2 >= -delta.Y)
                {
                    err -= delta.Y;
                    pos += stepPosX;
                }

                if (e2 <= delta.X)
                {
                    err += delta.X;
                    pos += stepPosY;
                }
            }
        }

        private void DrawConstantWidthLineInternal(Vector2I p0, Vector2I p1, Vector2I delta, Vector2I step, int width, Color color)
        {
            var err = delta.X - delta.Y;
            var pt = p0;
            var half = (width - 1) / 2;
            while (true)
            {
                if (pt == p1) break;

                PaintPixel(pt.X, pt.Y, color);

                var e2 = 2 * err;
                if (e2 >= -delta.Y)
                {
                    PaintRect(pt.X, pt.Y - half, 1, width, color, true);
                    err -= delta.Y;
                    pt.X += step.X;
                }

                if (e2 <= delta.X)
                {
                    PaintRect(pt.X - half, pt.Y, width, 1, color, true);
                    err += delta.X;
                    pt.Y += step.Y;
                }
            }
        }

        private readonly struct VariableWidthLinePainter<TWidth> where TWidth : struct, ILineWidthPainter
        {
            private readonly Vector2I p0;
            private readonly Vector2I delta;
            private readonly Vector2I step;
            private readonly Vector2I pStep;
            private readonly Color color;

            public VariableWidthLinePainter(Vector2I p0, Vector2I delta, Vector2I step, Color color)
            {
                this.p0 = p0;
                this.delta = delta;
                this.step = step;
                this.color = color;
                pStep = new Vector2I(step.Y, -step.X);
                // Weird edge cases...
                if (step.X == step.Y || step == new Vector2I(1, 0))
                    pStep = -pStep;
            }

            public void Execute(ref RenderContext render, ref TWidth widthPattern)
            {
                if (delta.X > delta.Y)
                    DrawVariableWidthLineInternal(ref render, ref widthPattern, 1, default(XAccessor), default(YAccessor));
                else
                    DrawVariableWidthLineInternal(ref render, ref widthPattern, -1, default(YAccessor), default(XAccessor));
            }

            private void DrawPerpendicular<TP, TS>(
                ref RenderContext render, Vector2I start, int pError, int halfWidth, int mainError,
                TP primary, TS secondary)
                where TP : struct, ICoordinateAccessor<Vector2I, int> where TS : struct, ICoordinateAccessor<Vector2I, int>
            {
                var threshold = primary.Read(in delta) - 2 * secondary.Read(in delta);
                var errorDiagonal = -2 * primary.Read(in delta);
                var errorSquare = 2 * secondary.Read(in delta);
                var q = 0;
                var p = 0;

                var pos = start;
                var error = pError;
                var tk = primary.Read(in delta) + secondary.Read(in delta) - mainError;

                while (tk <= halfWidth)
                {
                    render.PaintPixel(pos, color, true);
                    if (error >= threshold)
                    {
                        primary.Write(ref pos) += primary.Read(in pStep);
                        error += errorDiagonal;
                        tk += 2 * secondary.Read(in delta);
                    }

                    error += errorSquare;
                    secondary.Write(ref pos) += secondary.Read(in pStep);
                    tk += 2 * primary.Read(in delta);
                    q++;
                }

                pos = start;
                error = -pError;
                tk = primary.Read(in delta) + secondary.Read(in delta) + mainError;

                while (tk <= halfWidth)
                {
                    if (p != 0)
                        render.PaintPixel(pos, color, true);
                    if (error > threshold)
                    {
                        primary.Write(ref pos) -= primary.Read(in pStep);
                        error += errorDiagonal;
                        tk += 2 * secondary.Read(in delta);
                    }

                    error += errorSquare;
                    secondary.Write(ref pos) -= secondary.Read(in pStep);
                    tk += 2 * primary.Read(in delta);
                    p++;
                }

                if (q == 0 && p < 2) render.PaintPixel(start, color);
            }

            private void DrawVariableWidthLineInternal<TP, TS>(
                ref RenderContext render, ref TWidth widthPattern, int pErrorScale,
                TP primary, TS secondary)
                where TP : struct, ICoordinateAccessor<Vector2I, int>
                where TS : struct, ICoordinateAccessor<Vector2I, int>
            {
                var pError = 0;
                var error = 0;
                var pos = p0;
                var threshold = primary.Read(in delta) - 2 * secondary.Read(in delta);
                var errorDiagonal = -2 * primary.Read(in delta);
                var errorSquare = 2 * secondary.Read(in delta);
                var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

                for (var p = 0; p <= primary.Read(in delta); p++)
                {
                    var width = widthPattern.NextWidth();
                    var halfWidth = (int)((width - 1) * length * 2);
                    if (width > 0)
                        DrawPerpendicular(ref render, pos, pError * pErrorScale, halfWidth, error * pErrorScale, primary, secondary);
                    if (error >= threshold)
                    {
                        secondary.Write(ref pos) += secondary.Read(in step);
                        error += errorDiagonal;
                        if (pError >= threshold)
                        {
                            if (width > 0)
                                DrawPerpendicular(ref render, pos, pErrorScale * (pError + errorDiagonal + errorSquare), halfWidth, error * pErrorScale,
                                    primary, secondary);
                            pError += errorDiagonal;
                        }

                        pError += errorSquare;
                    }

                    error += errorSquare;
                    primary.Write(ref pos) += primary.Read(in step);
                }
            }
        }

        public interface ILineWidthPainter
        {
            int? ConstantWidth { get; }

            int NextWidth();
        }

        public struct LineWidthPattern : ILineWidthPainter
        {
            private int _pos;
            private readonly int[] _pattern;

            public LineWidthPattern(int[] pattern)
            {
                _pos = 0;
                _pattern = pattern;
            }

            public LineWidthPattern(string pattern, NamedLogger logger)
            {
                var tmp = new List<int>();
                var prevComma = -1;
                while (prevComma < pattern.Length)
                {
                    var nextComma = pattern.IndexOf(',', prevComma + 1);
                    if (nextComma < 0)
                        nextComma = pattern.Length;
                    var repeat = pattern.IndexOf('*', prevComma + 1);
                    var hasRepeats = repeat >= 0 && repeat < nextComma;
                    var width = pattern.Substring(prevComma + 1, (hasRepeats ? repeat : nextComma) - prevComma - 1);
                    if (!int.TryParse(width, out var widthValue))
                    {
                        logger.Warning($"Failed to parse line width {width}");
                        prevComma = nextComma;
                        continue;
                    }

                    var count = 1;
                    if (hasRepeats)
                    {
                        var repeats = pattern.Substring(repeat + 1, nextComma - repeat - 1);
                        if (!int.TryParse(repeats, out count))
                        {
                            count = 1;
                            logger.Warning($"Failed to parse line width repeats {repeats}");
                        }
                    }

                    for (var i = 0; i < count; i++)
                        tmp.Add(widthValue);
                    prevComma = nextComma;
                }

                _pos = 0;
                _pattern = tmp.ToArray();
            }

            public int? ConstantWidth
            {
                get
                {
                    if (_pattern == null || _pattern.Length == 0)
                        return 1;
                    if (_pattern.Length == 1)
                        return _pattern[0];
                    return null;
                }
            }

            public int NextWidth()
            {
                var value = _pattern[_pos];
                _pos++;
                if (_pos >= _pattern.Length)
                    _pos = 0;
                return value;
            }
        }

        #endregion
    }

    public readonly struct StencilRule : IEquatable<StencilRule>
    {
        public readonly int MinValue;
        public readonly int MaxValue;

        public static readonly StencilRule AlwaysPass = new StencilRule(0, 256);

        public StencilRule(int minValue, int maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public bool Test(byte stencilVal) => stencilVal >= MinValue && stencilVal < MaxValue;

        public bool Equals(StencilRule other) => MinValue == other.MinValue && MaxValue == other.MaxValue;

        public override bool Equals(object obj) => obj is StencilRule other && Equals(other);

        public override int GetHashCode() => (MinValue * 397) ^ MaxValue;
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