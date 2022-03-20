using System;
using System.Diagnostics.Contracts;
using Equinox76561198048419394.Core.Util;
using VRage.Components.Entity.CubeGrid;
using VRage.Entity.Block;
using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        // Changing this breaks serialization.  Do not modify.
        public static readonly PackedBoundedVec AnchorPacking = new PackedBoundedVec(-0.5f, 1.5f, 10);

        public readonly struct BlockAndAnchor : IEquatable<BlockAndAnchor>
        {
            public readonly BlockId Block;
            public readonly uint PackedAnchor;

            public static readonly BlockAndAnchor Null = new BlockAndAnchor(BlockId.Null, 0);

            public BlockAndAnchor(BlockId block, uint packedAnchor)
            {
                Block = block;
                PackedAnchor = packedAnchor;
            }

            public Vector3 NormalizedAnchor => AnchorPacking.Unpack(PackedAnchor);

            [Pure]
            public Vector3 GetBlockLocalAnchor(MyGridDataComponent gridData, MyBlock block) => NormalizedAnchor * block.Definition.Size * gridData.Size;

            [Pure]
            public Vector3 GetGridLocalAnchor(MyGridDataComponent gridData, MyBlock block)
            {
                var transform = gridData.GetBlockLocalMatrix(block);
                var local = GetBlockLocalAnchor(gridData, block);
                Vector3.Transform(ref local, ref transform, out var pos);
                return pos;
            }

            [Pure]
            public bool TryGetGridLocalAnchor(MyGridDataComponent gridData, out Vector3 pos)
            {
                if (gridData.TryGetBlock(Block, out var block))
                {
                    pos = GetGridLocalAnchor(gridData, block);
                    return true;
                }

                pos = default;
                return false;
            }

            [Pure]
            public bool Equals(BlockAndAnchor other) => BlockId.Comparer.Equals(Block, other.Block) && PackedAnchor == other.PackedAnchor;

            [Pure]
            public override bool Equals(object obj) => obj is BlockAndAnchor other && Equals(other);

            [Pure]
            public override int GetHashCode() => (Block.GetHashCode() * 397) ^ PackedAnchor.GetHashCode();
        }

        private readonly struct FeatureKey : IEquatable<FeatureKey>
        {
            public readonly BlockAndAnchor A, B, C, D;

            public bool IsLine => C.Block == BlockId.Null;

            private struct BlockAndAnchorSorter : BubbleSort.IBubbleSorter<BlockAndAnchor>
            {
                public bool ShouldSwap(in BlockAndAnchor a, in BlockAndAnchor b)
                {
                    if (a.Block.Value != b.Block.Value)
                        return a.Block.Value < b.Block.Value;
                    return a.PackedAnchor < b.PackedAnchor;
                }
            }

            public FeatureKey(BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d)
            {
                BubbleSort.Sort(ref a, ref b, ref c, ref d, default(BlockAndAnchorSorter));
                A = a;
                B = b;
                C = c;
                D = d;
            }

            public bool Equals(FeatureKey other) => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C) && D.Equals(other.D);

            public override bool Equals(object obj) => obj is FeatureKey other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = A.GetHashCode();
                hashCode = (hashCode * 397) ^ B.GetHashCode();
                hashCode = (hashCode * 397) ^ C.GetHashCode();
                hashCode = (hashCode * 397) ^ D.GetHashCode();
                return hashCode;
            }
        }
    }
}