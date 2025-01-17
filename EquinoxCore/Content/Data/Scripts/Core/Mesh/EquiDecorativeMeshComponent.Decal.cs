using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using Sandbox.ModAPI;
using VRage.Entity.Block;
using VRage.Import;
using VRage.ObjectBuilder;
using VRage.Serialization;
using VRageMath;
using VRageMath.PackedVector;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        [Flags]
        public enum DecalFlags : uint
        {
            Mirrored = 1 << 0,
        }

        public struct DecalArgs<TPos> where TPos : struct
        {
            public TPos Position;
            public Vector3 Normal;
            public Vector3 Up;
            public float Height;
            public DecalFlags Flags;
            public SharedArgs Shared;
        }

        public static EquiMeshHelpers.DecalData CreateDecalData(
            EquiDecorativeDecalToolDefinition.DecalDef decal,
            in DecalArgs<Vector3> args,
            bool ghost = false)
        {
            var left = Vector3.Cross(args.Normal, args.Up);
            left *= args.Height * decal.AspectRatio / left.Length() / 2;
            var mirrored = (args.Flags & DecalFlags.Mirrored) != 0;
            return new EquiMeshHelpers.DecalData
            {
                Material = decal.Material,
                Position = args.Position + args.Normal * 0.005f,
                TopLeftUv = mirrored ? decal.TopLeftUvMirrored : decal.TopLeftUv,
                BottomRightUv = mirrored ? decal.BottomRightUvMirrored : decal.BottomRightUv,
                Normal = VF_Packer.PackNormal(args.Normal),
                Up = new HalfVector3(args.Up * args.Height / 2),
                Left = new HalfVector3(left),
                ColorMask = args.Shared.Color,
                Ghost = ghost,
            };
        }

        public void AddDecal(EquiDecorativeDecalToolDefinition.DecalDef def, DecalArgs<BlockAndAnchor> args)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Decal, args.Position, BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var rpcArgs = new FeatureArgs
            {
                MaterialId = def.Id,
                DecalNormal = VF_Packer.PackNormal(args.Normal),
                DecalUp = VF_Packer.PackNormal(args.Up),
                DecalHeight = args.Height,
                DecalFlags = args.Flags,
                Shared = args.Shared,
            };
            if (TryAddFeatureInternal(in key, def.Owner, in rpcArgs))
                RaiseAddFeature_Sync(key, def.Owner.Id, in rpcArgs);
        }

        public void RemoveDecal(BlockAndAnchor a)
        {
            RemoveFeature(new FeatureKey(FeatureType.Decal, a, BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null));
        }
    }

    public partial class MyObjectBuilder_EquiDecorativeMeshComponent
    {
        public struct DecalBuilder
        {
            [XmlAttribute("A")]
            public ulong A;

            [XmlAttribute("AO")]
            public uint AOffset;

            [XmlAttribute("N")]
            public uint Normal;

            [XmlAttribute("U")]
            public uint Up;

            [XmlAttribute("H")]
            public float Height;

            [XmlAttribute("F")]
            public uint Flags;

            public bool ShouldSerializeFlags() => Flags != 0;

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

        public class DecorativeDecals : DecorativeGroup
        {
            [XmlElement("DecalId")]
            [Nullable]
            public string DecalId;

            [XmlElement("Decal")]
            public List<DecalBuilder> Decals;

            public override void Remap(IMySceneRemapper remapper)
            {
                if (Decals == null) return;
                for (var i = 0; i < Decals.Count; i++)
                {
                    var surf = Decals[i];
                    remapper.RemapObject(MyBlock.SceneType, ref surf.A);
                    Decals[i] = surf;
                }
            }
        }
    }
}