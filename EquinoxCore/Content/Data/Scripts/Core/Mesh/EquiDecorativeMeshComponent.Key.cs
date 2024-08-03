using System;
using Equinox76561198048419394.Core.Util;
using VRage.Network;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        internal enum FeatureType
        {
            Decal,
            Line,
            Surface,
            Model,
        }

        public readonly struct FeatureKey : IEquatable<FeatureKey>
        {
            internal readonly FeatureType Type;
            internal readonly BlockAndAnchor A, B, C, D;

            internal FeatureKey(FeatureType? type, BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d)
            {
                BubbleSort.Sort(ref a, ref b, ref c, ref d, default(BlockAndAnchor.BubbleSorter));
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