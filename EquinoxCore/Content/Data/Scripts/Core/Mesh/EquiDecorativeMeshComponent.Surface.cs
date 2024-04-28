
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Sandbox.ModAPI;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Import;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.Serialization;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        public struct SurfaceArgs<TPos> where TPos : struct
        {
            public TPos A, B, C;
            public TPos? D;

            public SharedArgs Shared;
            public UvProjectionMode? UvProjection;
            public UvBiasMode? UvBias;
            public float? UvScale;
        }

        private const UvProjectionMode DefaultUvProjection = UvProjectionMode.Bevel;
        private const UvBiasMode DefaultUvBias = UvBiasMode.XAxis;
        private const float DefaultUvScale = 1;

        public static EquiMeshHelpers.SurfaceData CreateSurfaceData(
            EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef materialDef,
            in SurfaceArgs<Vector3> args,
            Vector3 alignNormal,
            bool ghost = false)
        {
            Vector3 norm;
            const float eps = 1e-6f;
            var a = args.A;
            var b = args.B;
            var c = args.C;
            var d = args.D;
            if (d.HasValue
                && !a.Equals(b, eps) && !a.Equals(c, eps) && !a.Equals(d.Value, eps)
                && !b.Equals(c, eps) && !b.Equals(d.Value, eps)
                && !c.Equals(d.Value, eps))
            {
                var tmpD = d.Value;
                EquiMeshHelpers.SortSurfacePositions(alignNormal, ref a, ref b, ref c, ref tmpD);
                d = tmpD;
                var norm1 = Vector3.Cross(b - a, c - a);
                norm1.Normalize();
                var norm2 = Vector3.Cross(c - a, tmpD - a);
                norm2.Normalize();
                norm = norm1 + norm2;
                norm.Normalize();
            }
            else
            {
                EquiMeshHelpers.SortSurfacePositions(alignNormal, ref a, ref b, ref c);
                norm = Vector3.Cross(b - a, c - a);
                norm.Normalize();
            }

            FindUvProjection(args.UvProjection ?? DefaultUvProjection, args.UvBias ?? DefaultUvBias, norm, out var uvX, out var uvY);

            var trueUvScale = 1 / (args.UvScale ?? DefaultUvScale);

            var tangent = IdealTriangleTangent(a, b, c);
            if (d.HasValue)
                tangent += IdealTriangleTangent(a, c, d.Value);

            if (tangent.Normalize() < 1e-6f)
                tangent = Vector3.CalculatePerpendicularVector(norm);

            var snappedTangent = Vector3.Cross(Vector3.Cross(norm, tangent), norm);
            if (snappedTangent.Normalize() <= 1e-6)
                norm.CalculatePerpendicularVector(out snappedTangent);

            return new EquiMeshHelpers.SurfaceData
            {
                Material = materialDef.Material.MaterialName,
                Pt0 = CreateVertex(a),
                Pt1 = CreateVertex(b),
                Pt2 = CreateVertex(c),
                Pt3 = d.HasValue ? (EquiMeshHelpers.VertexData?)CreateVertex(d.Value) : null,
                FlipRearNormals = materialDef.FlipRearNormals,
                ColorMask = args.Shared.Color,
                Ghost = ghost,
            };

            Vector2 ComputeUv(in Vector3 pos) => new Vector2(trueUvScale * uvX.Dot(pos), trueUvScale * uvY.Dot(pos)) / materialDef.TextureSize;

            Vector3 IdealTriangleTangent(Vector3 pt0, Vector3 pt1, Vector3 pt2)
            {
                var uv0 = ComputeUv(pt0);
                var duv1 = ComputeUv(pt1) - uv0;
                var duv2 = ComputeUv(pt2) - uv0;
                return EquiMeshHelpers.ComputeTriangleTangent(pt1 - pt0, duv1, pt2 - pt0, duv2);
            }

            EquiMeshHelpers.VertexData CreateVertex(Vector3 pos) => new EquiMeshHelpers.VertexData
            {
                Position = pos,
                Uv = new HalfVector2(ComputeUv(in pos)),
                Normal = VF_Packer.PackNormal(norm),
                Tangent = VF_Packer.PackNormal(snappedTangent),
            };
        }

        public void AddSurface(EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef def, SurfaceArgs<BlockAndAnchor> args)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Surface, args.A, args.B, args.C, args.D ?? BlockAndAnchor.Null);
            var rpcArgs = new FeatureArgs
            {
                MaterialId = def.Id,
                Shared = args.Shared,
                UvProjection = args.UvProjection ?? DefaultUvProjection,
                UvBias = args.UvBias ?? DefaultUvBias,
                UvScale = args.UvScale ?? DefaultUvScale
            };
            if (TryAddFeatureInternal(in key, def.Owner, rpcArgs))
                RaiseAddFeature_Sync(key, def.Owner.Id, rpcArgs);
        }

        public void RemoveSurface(BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Surface, a, b, c, d);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }

        private static void FindUvProjection(UvProjectionMode projection, UvBiasMode bias, Vector3 normal, out Vector3 uvX, out Vector3 uvY)
        {
            Vector3 biasVector;
            switch (bias)
            {
                case UvBiasMode.XAxis:
                    biasVector = Vector3.UnitX;
                    break;
                case UvBiasMode.YAxis:
                    biasVector = Vector3.UnitY;
                    break;
                case UvBiasMode.ZAxis:
                    biasVector = Vector3.UnitZ;
                    break;
                case UvBiasMode.Count:
                default:
                    throw new ArgumentOutOfRangeException(nameof(bias), bias, null);
            }

            var uvPlaneNormal = FindUvNormal(projection, normal, biasVector);
            Vector3.Cross(ref biasVector, ref uvPlaneNormal, out uvY);
            var yLength = uvY.LengthSquared();
            if (yLength <= 1e-6f)
                uvPlaneNormal.CalculatePerpendicularVector(out uvY);
            else
                uvY /= (float)Math.Sqrt(yLength);

            Vector3.Cross(ref uvPlaneNormal, ref uvY, out uvX);
        }

        private static readonly Vector3[] FirstQuadrantUvProjections =
        {
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, 0, 1),
            new Vector3(0.7071f, 0.7071f, 0),
            new Vector3(0, 0.7071f, 0.7071f),
            new Vector3(0.7071f, 0, 0.7071f),
            new Vector3(0.57735f, 0.57735f, 0.57735f),
        };

        private static Vector3 FindUvNormal(UvProjectionMode mode, Vector3 normal, Vector3 except)
        {
            switch (mode)
            {
                case UvProjectionMode.Cube:
                    return FindCubeUvNormal(normal, except);
                case UvProjectionMode.Bevel:
                    return FindBeveledUvNormal(normal, except);
                case UvProjectionMode.Count:
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static Vector3 FindCubeUvNormal(Vector3 normal, Vector3 except)
        {
            var rejected = Vector3.Reject(normal, except);
            if (rejected.LengthSquared() < 1e-6)
                rejected = normal;
            var best = Vector3.DominantAxisProjection(rejected);
            best.Normalize();
            return best;
        }

        private static Vector3 FindBeveledUvNormal(Vector3 normal, Vector3 except)
        {
            var exceptAbs = Vector3.Abs(except);
            exceptAbs.Normalize();

            var abs = Vector3.Abs(normal);
            // First octant, 8 possibilities.
            var bestI = 0;
            var bestDot = float.NegativeInfinity;
            for (var i = 0; i < FirstQuadrantUvProjections.Length; i++)
            {
                ref var candidate = ref FirstQuadrantUvProjections[i];
                if (candidate.Dot(ref exceptAbs) > 0.99999f) continue;
                Vector3.Dot(ref abs, ref candidate, out var dot);
                if (dot <= bestDot) continue;
                bestDot = dot;
                bestI = i;
            }

            // Move back to original octant.
            return MiscMath.SafeSign(normal) * FirstQuadrantUvProjections[bestI];
        }
    }

    public partial class MyObjectBuilder_EquiDecorativeMeshComponent
    {

        public struct SurfaceBuilder
        {
            [XmlAttribute("A")]
            public ulong A;

            [XmlAttribute("AO")]
            public uint AOffset;

            [XmlAttribute("B")]
            public ulong B;

            [XmlAttribute("BO")]
            public uint BOffset;

            [XmlAttribute("C")]
            public ulong C;

            [XmlAttribute("CO")]
            public uint COffset;

            [XmlAttribute("D")]
            public ulong D;

            [XmlAttribute("DO")]
            public uint DOffset;

            public bool ShouldSerializeD() => D != 0;
            public bool ShouldSerializeDOffset() => ShouldSerializeD();

            [XmlIgnore]
            [Nullable]
            public float? UvScaleRaw;

            [XmlAttribute("S")]
            [NoSerialize]
            public float UvScale
            {
                get => UvScaleRaw ?? 1;
                set
                {
                    if (Math.Abs(value) < 1e-6 || Math.Abs(value - 1) < 1e-6)
                        UvScaleRaw = null;
                    else
                        UvScaleRaw = value;
                }
            }

            public bool ShouldSerializeUvScale() => UvScaleRaw.HasValue;

#pragma warning disable CS0612 // Type or member is obsolete, backcompat
            [XmlIgnore]
            [NoSerialize]
            [Obsolete]
            public PackedHsvShift? ColorRaw;

            [XmlAttribute("Color")]
            [NoSerialize]
            public string Color
            {
                set => ColorRaw = ModifierDataColor.Deserialize(value).Color;
                get => ColorRaw.HasValue ? ModifierDataColor.Serialize(ColorRaw.Value) : null;
            }

            public bool ShouldSerializeColor() => ColorRaw.HasValue;
#pragma warning restore CS0612 // Type or member is obsolete
        }

        public class DecorativeSurfaces : DecorativeGroup
        {
            [XmlElement("MaterialId")]
            [Nullable]
            public string MaterialId;

            [XmlElement("UvProjection")]
            [Nullable]
            public UvProjectionMode? UvProjection;

            [XmlElement("UvBias")]
            [Nullable]
            public UvBiasMode? UvBias;

            [XmlElement("Surf")]
            public List<SurfaceBuilder> Surfaces;

            public override void Remap(IMySceneRemapper remapper)
            {
                if (Surfaces == null) return;
                for (var i = 0; i < Surfaces.Count; i++)
                {
                    var surf = Surfaces[i];
                    remapper.RemapObject(MyBlock.SceneType, ref surf.A);
                    remapper.RemapObject(MyBlock.SceneType, ref surf.B);
                    remapper.RemapObject(MyBlock.SceneType, ref surf.C);
                    if (surf.ShouldSerializeD())
                        remapper.RemapObject(MyBlock.SceneType, ref surf.D);
                    Surfaces[i] = surf;
                }
            }
        }
    }
}