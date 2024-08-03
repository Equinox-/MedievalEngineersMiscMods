using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using Sandbox.ModAPI;
using VRage.Entity.Block;
using VRage.ObjectBuilder;
using VRage.Serialization;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        public struct LineArgs<TPos> where TPos : struct
        {
            public TPos A, B;
            public float CatenaryFactor;
            public SharedArgs Shared;

            public float? WidthA;
            public float? WidthB;
        }

        public static EquiMeshHelpers.LineData CreateLineData(
            EquiDecorativeLineToolDefinition.LineMaterialDef material,
            in LineArgs<Vector3> args,
            bool ghost = false)
        {
            var def = material.Owner;
            var length = Vector3.Distance(args.A, args.B);
            var defaultSegmentsPerMeterSqrt = 0f;
            var cf = args.CatenaryFactor;
            if (cf > 0)
                defaultSegmentsPerMeterSqrt = MathHelper.Lerp(4, 8, MathHelper.Clamp(cf, 0, 1));
            var segmentsPerMeterSqrt = def.SegmentsPerMeterSqrt ?? defaultSegmentsPerMeterSqrt;
            var segmentCount = Math.Max(1, (int)Math.Ceiling(length * def.SegmentsPerMeter + Math.Sqrt(length) * segmentsPerMeterSqrt));
            var catenaryLength = cf > 0 ? length * (1 + cf) : 0;

            var width0 = args.WidthA >= 0 ? args.WidthA.Value : def.DefaultWidth;
            var width1 = args.WidthB >= 0 ? args.WidthB.Value : width0;
            return new EquiMeshHelpers.LineData
            {
                Material = material.Material.MaterialName,
                Pt0 = args.A,
                Pt1 = args.B,
                Width0 = width0,
                Width1 = width1,
                UvOffset = material.UvOffset,
                UvTangent = material.UvTangentPerMeter * Math.Max(catenaryLength, length),
                UvNormal = material.UvNormal,
                Segments = segmentCount,
                HalfSideSegments = def.HalfSideSegments(Math.Max(width0, width1)),
                CatenaryLength = catenaryLength,
                UseNaturalGravity = true,
                ColorMask = args.Shared.Color,
                Ghost = ghost,
            };
        }

        public void AddLine(EquiDecorativeLineToolDefinition.LineMaterialDef def, LineArgs<BlockAndAnchor> args)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Line, args.A, args.B, BlockAndAnchor.Null, BlockAndAnchor.Null);
            // Sorting re-ordered the keys, so reorder the widths too.
            if (!args.A.Equals(key.A))
                MyUtils.Swap(ref args.WidthA, ref args.WidthB);
            var rpcArgs = new FeatureArgs
            {
                MaterialId = def.Id,
                CatenaryFactor = args.CatenaryFactor,
                Shared = args.Shared,
                WidthA = args.WidthA >= 0 ? args.WidthA.Value : -1,
                WidthB = args.WidthB >= 0 ? args.WidthB.Value : -1,
            };
            if (TryAddFeatureInternal(in key, def.Owner, in rpcArgs))
                RaiseAddFeature_Sync(key, def.Owner.Id, in rpcArgs);
        }

        public void RemoveLine(BlockAndAnchor a, BlockAndAnchor b) => RemoveFeature(new FeatureKey(FeatureType.Line, a, b, BlockAndAnchor.Null, BlockAndAnchor.Null));
    }

    public partial class MyObjectBuilder_EquiDecorativeMeshComponent
    {
        public struct LineBuilder
        {
            [XmlAttribute("A")]
            public ulong A;

            [XmlAttribute("AO")]
            public uint AOffset;

            [XmlAttribute("B")]
            public ulong B;

            [XmlAttribute("BO")]
            public uint BOffset;

            [XmlAttribute("CF")]
            public float CatenaryFactor;

            [Nullable]
            private float? _wa;

            [Nullable]
            private float? _wb;

            [XmlAttribute("AW")]
            [NoSerialize]
            public float WidthA
            {
                get => _wa ?? -1;
                set => _wa = value < 0 ? null : (float?)value;
            }

            [XmlAttribute("BW")]
            [NoSerialize]
            public float WidthB
            {
                get => _wb ?? -1;
                set => _wb = value < 0 ? null : (float?)value;
            }

            public bool ShouldSerializeWidthA() => WidthA >= 0;
            public bool ShouldSerializeWidthB() => WidthB >= 0;

#pragma warning disable CS0612 // Type or member is obsolete, backcompat
            [XmlIgnore]
            [Nullable]
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

        public class DecorativeLines : DecorativeGroup
        {
            [XmlElement("MaterialId")]
            [Nullable]
            public string MaterialId;

            [XmlElement("Line")]
            public List<LineBuilder> Lines;

            public override void Remap(IMySceneRemapper remapper)
            {
                if (Lines == null) return;
                for (var i = 0; i < Lines.Count; i++)
                {
                    var line = Lines[i];
                    remapper.RemapObject(MyBlock.SceneType, ref line.A);
                    remapper.RemapObject(MyBlock.SceneType, ref line.B);
                    Lines[i] = line;
                }
            }
        }
    }
}