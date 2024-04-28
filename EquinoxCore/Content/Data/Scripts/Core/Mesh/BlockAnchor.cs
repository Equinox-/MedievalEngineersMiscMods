using System;
using System.Diagnostics.Contracts;
using Equinox76561198048419394.Core.Util;
using VRage.Components.Entity.CubeGrid;
using VRage.Entity.Block;
using VRage.Network;
using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    public readonly struct BlockAndAnchor : IEquatable<BlockAndAnchor>
    {
        // Changing this breaks serialization.  Do not modify.
        public static readonly PackedBoundedVec LegacyAnchorPacking = new PackedBoundedVec(-0.5f, 1.5f, 10);
        public static readonly PackedBoundedVec NewAnchorPacking = new PackedBoundedVec(-1, 1, 10);
        public const uint NewAnchorPackingMask = 1u << 31;

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

        public readonly struct BubbleSorter : BubbleSort.IBubbleSorter<BlockAndAnchor>
        {
            public bool ShouldSwap(in BlockAndAnchor a, in BlockAndAnchor b)
            {
                if (a.Block.Value != b.Block.Value)
                    return a.Block.Value < b.Block.Value;
                return a.PackedAnchor < b.PackedAnchor;
            }
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
}