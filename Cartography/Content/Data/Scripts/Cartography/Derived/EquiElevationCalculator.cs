using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.ModAPI;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Derived
{
    internal class EquiElevationCalculator
    {
        private readonly MyParallelTask _parallel = new MyParallelTask();
        private readonly LRUCache<ElevationArgs, FutureElevationData> _elevationCache = new LRUCache<ElevationArgs, FutureElevationData>(16);

        public ElevationData GetOrComputeAsync(in ElevationArgs args)
        {
            lock (this)
            {
                if (_elevationCache.TryRead(args, out var data))
                    return data.Computed;
                data = new FutureElevationData(this, in args);
                _elevationCache.Write(args, data);
                return data.Computed;
            }
        }

        private sealed class FutureElevationData
        {
            private readonly ElevationArgs _args;
            public volatile ElevationData Computed;

            public FutureElevationData(EquiElevationCalculator owner, in ElevationArgs args)
            {
                _args = args;
                owner._parallel.Start(Compute);
            }

            private void Compute()
            {
                var rasterSize = ElevationData.RasterSize;
                var rasterSizeMinusOne = rasterSize - 1;
                var data = new ushort[rasterSize, rasterSize];
                var minRadius = _args.Planet.MinimumRadius;
                var deltaRadius = _args.Planet.MaximumRadius - minRadius;
                var planet = _args.Planet;
                for (var y = 0; y < rasterSize; y++)
                {
                    var texY = _args.Area.Position.Y + y * _args.Area.Size.Y / rasterSizeMinusOne;
                    for (var x = 0; x < rasterSize; x++)
                    {
                        var texX = _args.Area.Position.X + x * _args.Area.Size.X / rasterSizeMinusOne;
                        var uv = new Vector2D(texX, texY);
                        MyEnvironmentCubemapHelper.UniformAngleToProjectionUVs(ref uv);
                        var world = new Vector3D(uv, -1);
                        Vector3D.Transform(world, MyEnvironmentCubemapHelper.Orientations[_args.Face], out world);
                        var height = planet.GetClosestSurfacePointLocal(world).Length();
                        var normHeight = (height - minRadius) / deltaRadius;
                        data[y, x] = (ushort)(normHeight * ushort.MaxValue);
                    }
                }

                Computed = new ElevationData(in _args, data);
            }
        }

        internal void Invalidate()
        {
            lock (this)
            {
                _elevationCache.Reset();
            }
        }
    }

    public readonly struct ElevationArgs : IEquatable<ElevationArgs>
    {
        public readonly MyPlanet Planet;
        public readonly int Face;
        public readonly RectangleF Area;

        public ElevationArgs(MyPlanet planet, int face, RectangleF area)
        {
            Planet = planet;
            Face = face;
            Area = area;
        }

        public bool Equals(ElevationArgs other) => Equals(Planet, other.Planet) && Face == other.Face && Area.Equals(other.Area);

        public override bool Equals(object obj) => obj is ElevationArgs other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Planet != null ? Planet.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Face;
                hashCode = (hashCode * 397) ^ Area.GetHashCode();
                return hashCode;
            }
        }
    }

    public sealed class ElevationData
    {
        private readonly ushort[,] _data;
        private readonly float _range;
        private readonly float _centerOnAverage;
        private readonly int _rasterSizeMinusOne;

        public static int RasterSize { get; internal set; } = 256;
        public static int RasterSizeMinusOne => RasterSize - 1;

        public ElevationData(in ElevationArgs args, ushort[,] data)
        {
            _data = data;
            _range = args.Planet.MaximumRadius - args.Planet.MinimumRadius;
            _centerOnAverage = args.Planet.MinimumRadius - args.Planet.AverageRadius;
            _rasterSizeMinusOne = _data.GetLength(0) - 1;
        }

        public float SampleRawNorm(int x, int y) => _data[y, x] / (float) ushort.MaxValue;

        public float SampleRawRelative(int x, int y) => SampleRawNorm(x, y) * _range + _centerOnAverage;

        public float SampleNorm(float x, float y)
        {
            x *= _rasterSizeMinusOne;
            y *= _rasterSizeMinusOne;
            var rawX = (int)Math.Floor(x);
            var rawY = (int)Math.Floor(y);
            var mixX = x - rawX;
            var mixY = y - rawY;

            float SampleAlongX(int col, int row)
            {
                if (col < 0)
                    return SampleRawNorm(0, row);
                if (col >= _rasterSizeMinusOne)
                    return SampleRawNorm(_rasterSizeMinusOne, row);
                return MathHelper.Lerp(SampleRawNorm(col, row), SampleRawNorm(col + 1, row), mixX);
            }

            if (rawY < 0)
                return SampleAlongX(rawX, 0);
            if (rawY >= _rasterSizeMinusOne)
                return SampleAlongX(rawX, _rasterSizeMinusOne);
            return MathHelper.Lerp(
                SampleAlongX(rawX, rawY),
                SampleAlongX(rawX, rawY + 1),
                mixY);
        }

        public float SampleRelative(float x, float y) => SampleNorm(x, y) * _range + _centerOnAverage;
    }
}