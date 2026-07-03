using System;
using VRageMath;

namespace Equinox76561198048419394.Core.ChunkLoader
{
    public readonly struct ChunkLoaderKey : IEquatable<ChunkLoaderKey>
    {
        private const double GridSize = 32;
        public readonly int Lod;
        public readonly BoundingBoxI Box;

        public ChunkLoaderKey(int lod, BoundingBoxI box)
        {
            Lod = lod;
            Box = box;
        }

        public static ChunkLoaderKey FromWorld(in BoundingBoxD box)
        {
            var boxI = new BoundingBoxI(
                Vector3I.Floor(box.Min / GridSize),
                Vector3I.Ceiling(box.Max / GridSize));
            var lod = 0;
            while (boxI.HalfExtents.AbsMax() > 2)
            {
                boxI.Min >>= 1;
                boxI.Max = (boxI.Max + 1) >> 1;
                lod++;
            }

            return new ChunkLoaderKey(lod, boxI);
        }

        public BoundingBoxD ToWorld()
        {
            var scale = GridSize * (1 << Lod);
            return new BoundingBoxD(Box.Min * scale, Box.Max * scale);
        }

        public bool Equals(ChunkLoaderKey other) => Lod == other.Lod && Box.Equals(other.Box);

        public override bool Equals(object obj) => obj is ChunkLoaderKey other && Equals(other);

        public override int GetHashCode() => (Lod * 397) ^ Box.GetHashCode();
    }
}