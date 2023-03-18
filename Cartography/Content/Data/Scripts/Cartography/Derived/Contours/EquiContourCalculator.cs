using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Equinox76561198048419394.Cartography.Utils;
using Equinox76561198048419394.Core.Util.Memory;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Derived.Contours
{
    internal sealed class EquiContourCalculator
    {
        private const int ElevationRasterSize = 256;
        private const float SimplificationDistanceSq = 0.5f;

        private readonly LRUCache<ContourArgs, FutureContourData> _contoursCache = new LRUCache<ContourArgs, FutureContourData>(128);
        private readonly ConcurrentBag<ContourTempData> _tempDataPool = new ConcurrentBag<ContourTempData>();
        private readonly MyParallelTask _parallel = new MyParallelTask();

        public ContourData GetOrComputeAsync(in ContourArgs args)
        {
            lock (this)
            {
                if (_contoursCache.TryRead(args, out var data))
                    return data.Computed;

                data = new FutureContourData(this, in args);
                _contoursCache.Write(args, data);
                return data.Computed;
            }
        }

        #region Contouring

        private sealed class FutureContourData
        {
            private readonly ContourArgs _args;
            public volatile ContourData Computed;
            private readonly EquiContourCalculator _owner;

            public FutureContourData(EquiContourCalculator owner, in ContourArgs args)
            {
                _owner = owner;
                _args = args;
                _owner._parallel.Start(Compute);
            }


            private void Compute()
            {
                if (!_owner._tempDataPool.TryTake(out var tempData))
                    tempData = new ContourTempData();
                try
                {
                    tempData.FillEdgeMap(in _args);
                    var vertices = new List<uint>();
                    var loops = new List<ContourData.ContourLine>();
                    for (var contour = tempData.MinContour; contour <= tempData.MaxContour; contour++)
                    {
                        tempData.CollectEdges(contour, _args.ContourInterval, vertices, loops);
                    }

                    Computed = new ContourData(vertices, loops);
                }
                finally
                {
                    _owner._tempDataPool.Add(tempData);
                }
            }
        }

        private sealed class ContourTempData
        {
            private float _minContour;
            private float _maxContour;
            private readonly CellData[,] _cellData = new CellData[ElevationRasterSize + 1, ElevationRasterSize + 1];

            private struct EdgeData
            {
                private short _firstContour;
                private short _lastContour;
                private byte _firstContourPos;
                private byte _lastContourPos;

                public void Set(float contourFrom, float contourTo)
                {
                    const float epsilon = 1e-6f;
                    _firstContour = (short)Math.Ceiling(Math.Min(contourFrom, contourTo) + epsilon);
                    _lastContour = (short)Math.Floor(Math.Max(contourFrom, contourTo) - epsilon);
                    var widthPerContour = byte.MaxValue / (contourTo - contourFrom);
                    _firstContourPos = (byte)((_firstContour - contourFrom) * widthPerContour);
                    _lastContourPos = (byte)((_lastContour - contourFrom) * widthPerContour);
                }

                public bool TryGet(short id, out float pos)
                {
                    if (id >= _firstContour && id <= _lastContour)
                    {
                        pos = _firstContourPos;
                        if (id > _firstContour)
                            pos += (id - _firstContour) * (_lastContourPos - (float)_firstContourPos) / (_lastContour - _firstContour);
                        pos /= byte.MaxValue;
                        return true;
                    }

                    pos = float.NaN;
                    return false;
                }
            }

            private struct CellData
            {
                public float TopLeftFractionalContour;

                // Contours along the top of this cell
                public EdgeData Top;

                // Contours along the left side of this cell.
                public EdgeData Left;

                public int ConsumedMask;
            }

            public void FillEdgeMap(in ContourArgs args)
            {
                _minContour = float.PositiveInfinity;
                _maxContour = float.NegativeInfinity;
                var planet = args.Planet;
                for (var y = 0; y <= ElevationRasterSize; y++)
                {
                    var texY = args.Area.Position.Y + y * args.Area.Size.Y / ElevationRasterSize;
                    for (var x = 0; x <= ElevationRasterSize; x++)
                    {
                        var texX = args.Area.Position.X + x * args.Area.Size.X / ElevationRasterSize;
                        var uv = new Vector2D(texX, texY);
                        MyEnvironmentCubemapHelper.UniformAngleToProjectionUVs(ref uv);
                        var world = new Vector3D(uv, -1);
                        Vector3D.Transform(world, MyEnvironmentCubemapHelper.Orientations[args.Face], out world);
                        var relHeight = (float)(planet.GetClosestSurfacePointLocal(world).Length() - planet.AverageRadius);
                        var here = relHeight / args.ContourInterval;
                        _cellData[y, x].TopLeftFractionalContour = here;
                        if (here < _minContour)
                            _minContour = here;
                        if (here > _maxContour)
                            _maxContour = here;
                        if (y > 0)
                        {
                            ref var aboveCell = ref _cellData[y - 1, x];
                            aboveCell.Left.Set(aboveCell.TopLeftFractionalContour, here);
                        }

                        if (x > 0)
                        {
                            ref var leftCell = ref _cellData[y, x - 1];
                            leftCell.Top.Set(leftCell.TopLeftFractionalContour, here);
                        }
                    }
                }
            }

            public short MinContour => (short)Math.Ceiling(_minContour);
            public short MaxContour => (short)Math.Floor(_maxContour);

            private enum Side : byte
            {
                Top = 0,
                Left = 1,
                Bottom = 2,
                Right = 3,
            }

            private static int SideFlag(Side side) => 1 << (int)side;

            private bool TryFindOutgoing(
                int x, int y, Side incoming, short contour,
                out Side outgoing, out Vector2 outgoingPos,
                out int nextX, out int nextY, out Side nextIncoming)
            {
                ref var here = ref _cellData[y, x];
                ref var below = ref _cellData[y + 1, x];
                ref var right = ref _cellData[y, x + 1];
                for (var i = 1; i <= 3; i++)
                {
                    outgoing = (Side)(((int)incoming + i) % 4);
                    // Check if the node was already consumed.
                    if ((here.ConsumedMask & SideFlag(outgoing)) != 0)
                        continue;
                    float pos;
                    switch (outgoing)
                    {
                        case Side.Top:
                            if (here.Top.TryGet(contour, out pos))
                            {
                                outgoingPos = new Vector2(x + pos, y);
                                nextX = x;
                                nextY = y - 1;
                                nextIncoming = Side.Bottom;
                                return true;
                            }

                            break;
                        case Side.Left:
                            if (here.Left.TryGet(contour, out pos))
                            {
                                outgoingPos = new Vector2(x, y + pos);
                                nextX = x - 1;
                                nextY = y;
                                nextIncoming = Side.Right;
                                return true;
                            }

                            break;
                        case Side.Bottom:
                            if (below.Top.TryGet(contour, out pos))
                            {
                                outgoingPos = new Vector2(x + pos, y + 1);
                                nextX = x;
                                nextY = y + 1;
                                nextIncoming = Side.Top;
                                return true;
                            }

                            break;
                        case Side.Right:
                            if (right.Left.TryGet(contour, out pos))
                            {
                                outgoingPos = new Vector2(x + 1, y + pos);
                                nextX = x + 1;
                                nextY = y;
                                nextIncoming = Side.Left;
                                return true;
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                outgoing = default;
                outgoingPos = default;
                nextX = default;
                nextY = default;
                nextIncoming = default;
                return false;
            }

            public void CollectEdges(short contour, float contourInterval, List<uint> vertices, List<ContourData.ContourLine> loops)
            {
                void CollectLoop(int x, int y, Side incoming, List<Vector2> sequence, Stack<MyTuple<int, int>> ranges)
                {
                    while (true)
                    {
                        var okay = TryFindOutgoing(x, y, incoming, contour,
                            out var outgoing, out var outgoingPos,
                            out var nextX, out var nextY, out var nextIncoming);
                        if (okay)
                            sequence.Add(outgoingPos);
                        if (!okay || nextX < 0 || nextY < 0 || nextX >= ElevationRasterSize || nextY >= ElevationRasterSize)
                        {
                            SimplifyAndCollectSequence(sequence, ranges);
                            return;
                        }

                        ref var here = ref _cellData[y, x];
                        here.ConsumedMask |= 1 << (int)incoming;
                        here.ConsumedMask |= 1 << (int)outgoing;

                        x = nextX;
                        y = nextY;
                        incoming = nextIncoming;
                    }
                }

                void SimplifyAndCollectSequence(List<Vector2> sequence, Stack<MyTuple<int, int>> ranges)
                {
                    if (sequence.Count <= 1)
                        return;
                    var simplified = LineSimplifier.SimplifySequence(sequence.AsEqSpan(), SimplificationDistanceSq, ranges);

                    // Commit to vertex list.
                    var vertexOffset = vertices.Count;
                    foreach (var vert in simplified)
                        vertices.Add(ContourData.PackVertex(vert / ElevationRasterSize));

                    loops.Add(new ContourData.ContourLine(contour, contour * contourInterval, 
                        vertexOffset, vertexOffset + simplified.Length - 1));
                }

                for (var y = 0; y < ElevationRasterSize; y++)
                for (var x = 0; x < ElevationRasterSize; x++)
                    _cellData[y, x].ConsumedMask = 0;

                using (PoolManager.Get(out List<Vector2> sequence))
                using (PoolManager.Get(out Stack<MyTuple<int, int>> ranges))
                    for (var y = 0; y < ElevationRasterSize; y++)
                    for (var x = 0; x < ElevationRasterSize; x++)
                    {
                        ref var cell = ref _cellData[y, x];

                        if ((cell.ConsumedMask & SideFlag(Side.Top)) == 0 && cell.Top.TryGet(contour, out var pos))
                        {
                            sequence.Clear();
                            sequence.Add(new Vector2(x + pos, y));
                            CollectLoop(x, y, Side.Top, sequence, ranges);
                        }

                        if ((cell.ConsumedMask & SideFlag(Side.Left)) == 0 && cell.Left.TryGet(contour, out pos))
                        {
                            sequence.Clear();
                            sequence.Add(new Vector2(x, y + pos));
                            CollectLoop(x, y, Side.Left, sequence, ranges);
                        }
                    }
            }
        }

        #endregion
    }

    public sealed class ContourData : IEquatable<ContourData>
    {
        public readonly struct ContourLine
        {
            public readonly int ContourId;
            public readonly float ContourElevation;

            /// <summary>
            /// First vertex index in line string.
            /// </summary>
            public readonly int StartVertex;

            /// <summary>
            /// Last vertex index in line string, inclusive.
            /// </summary>
            public readonly int EndVertex;

            public ContourLine(int contourId, float contourElevation, int startVertex, int endVertex)
            {
                ContourId = contourId;
                ContourElevation = contourElevation;
                StartVertex = startVertex;
                EndVertex = endVertex;
            }
        }

        private readonly ListReader<uint> _packedVertices;
        public readonly ListReader<ContourLine> Lines;
        public int VertexCount => _packedVertices.Count;

        internal static uint PackVertex(Vector2 norm)
        {
            var x = (ushort)MathHelper.Clamp(norm.X * ushort.MaxValue, 0, ushort.MaxValue);
            var y = (ushort)MathHelper.Clamp(norm.Y * ushort.MaxValue, 0, ushort.MaxValue);
            return ((uint)x << 16) | y;
        }

        internal static Vector2 UnpackVertex(uint vertex)
        {
            var x = (float)(vertex >> 16);
            var y = (float)(vertex & 0xFFFF);
            return new Vector2(x / ushort.MaxValue, y / ushort.MaxValue);
        }

        public ContourData(ListReader<uint> packedVertices, ListReader<ContourLine> lines)
        {
            _packedVertices = packedVertices;
            Lines = lines;
        }

        public Vector2 GetVertex(int vertex) => UnpackVertex(_packedVertices[vertex]);

        public bool Equals(ContourData other) => this == other;
    }

    public readonly struct ContourArgs : IEquatable<ContourArgs>
    {
        public readonly MyPlanet Planet;
        public readonly int Face;
        public readonly RectangleF Area;
        public readonly float ContourInterval;

        public ContourArgs(MyPlanet planet, int face, RectangleF area, float contourInterval)
        {
            Planet = planet;
            Face = face;
            Area = area;
            ContourInterval = contourInterval;
        }

        public bool Equals(ContourArgs other)
        {
            return Equals(Planet, other.Planet) && Face == other.Face && Area.Equals(other.Area) && ContourInterval.Equals(other.ContourInterval);
        }

        public override bool Equals(object obj)
        {
            return obj is ContourArgs other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Planet != null ? Planet.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Face;
                hashCode = (hashCode * 397) ^ Area.GetHashCode();
                hashCode = (hashCode * 397) ^ ContourInterval.GetHashCode();
                return hashCode;
            }
        }
    }
}