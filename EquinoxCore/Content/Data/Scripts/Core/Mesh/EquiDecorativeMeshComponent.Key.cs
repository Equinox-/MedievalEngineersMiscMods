using System;
using System.Diagnostics.Contracts;
using Equinox76561198048419394.Core.Util;
using VRage.Components.Entity.CubeGrid;
using VRage.Entity.Block;
using VRage.Network;
using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        // Changing this breaks serialization.  Do not modify.
        public static readonly PackedBoundedVec LegacyAnchorPacking = new PackedBoundedVec(-0.5f, 1.5f, 10);
        public static readonly PackedBoundedVec NewAnchorPacking = new PackedBoundedVec(-1, 1, 10);
        public const uint NewAnchorPackingMask = 1u << 31;

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

            public bool IsNull => Block == BlockId.Null;

            private Vector3 NormalizedAnchor => (PackedAnchor & NewAnchorPackingMask) != 0
                ? NewAnchorPacking.Unpack(PackedAnchor)
                : LegacyAnchorPacking.Unpack(PackedAnchor);

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

        private enum FeatureType
        {
            Decal,
            Line,
            Surface,
            Model,
        }

        private readonly struct FeatureKey : IEquatable<FeatureKey>
        {
            public readonly FeatureType Type;
            public readonly BlockAndAnchor A, B, C, D;

            private struct BlockAndAnchorSorter : BubbleSort.IBubbleSorter<BlockAndAnchor>
            {
                public bool ShouldSwap(in BlockAndAnchor a, in BlockAndAnchor b)
                {
                    if (a.Block.Value != b.Block.Value)
                        return a.Block.Value < b.Block.Value;
                    return a.PackedAnchor < b.PackedAnchor;
                }
            }

            public FeatureKey(FeatureType? type, BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d)
            {
                BubbleSort.Sort(ref a, ref b, ref c, ref d, default(BlockAndAnchorSorter));
                A = a;
                B = b;
                C = c;
                D = d;
                Type = type ?? (B.IsNull ? FeatureType.Decal : C.IsNull ? FeatureType.Line : FeatureType.Surface);
            }

            public bool Equals(FeatureKey other) => Type.Equals(other.Type) && A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C) && D.Equals(other.D);

            public override bool Equals(object obj) => obj is FeatureKey other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = Type.GetHashCode();
                hashCode = (hashCode * 397) ^ A.GetHashCode();
                hashCode = (hashCode * 397) ^ B.GetHashCode();
                hashCode = (hashCode * 397) ^ C.GetHashCode();
                hashCode = (hashCode * 397) ^ D.GetHashCode();
                return hashCode;
            }
        }

        [RpcSerializable]
        public struct RpcBlockAndAnchor
        {
            // ReSharper disable MemberCanBePrivate.Global
            public BlockId Block;
            public uint PackedAnchor;
            // ReSharper restore MemberCanBePrivate.Global

            public static implicit operator RpcBlockAndAnchor(in BlockAndAnchor other) => new RpcBlockAndAnchor
            {
                Block = other.Block,
                PackedAnchor = other.PackedAnchor
            };

            public static implicit operator BlockAndAnchor(in RpcBlockAndAnchor other) => new BlockAndAnchor(other.Block, other.PackedAnchor);
        }

        [RpcSerializable]
        private struct RpcFeatureKey
        {
            // ReSharper disable MemberCanBePrivate.Local
            public FeatureType Type;
            public RpcBlockAndAnchor A, B, C, D;
            // ReSharper restore MemberCanBePrivate.Local

            public static implicit operator RpcFeatureKey(in FeatureKey other) => new RpcFeatureKey
            {
                Type = other.Type,
                A = other.A,
                B = other.B,
                C = other.C,
                D = other.D
            };

            public static implicit operator FeatureKey(in RpcFeatureKey other) => new FeatureKey(other.Type, other.A, other.B, other.C, other.D);
        }
    }
}