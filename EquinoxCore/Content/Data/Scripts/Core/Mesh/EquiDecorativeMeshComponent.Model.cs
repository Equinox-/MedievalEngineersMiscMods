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

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        public struct ModelArgs<TPos> where TPos : struct
        {
            public TPos Position;
            public Vector3 Forward;
            public Vector3 Up;
            public float Scale;
            public SharedArgs Shared;
        }

        public static EquiDynamicModelsComponent.ModelData CreateModelData(
            EquiDecorativeModelToolDefinition.ModelDef model,
            in ModelArgs<Vector3> args,
            bool ghost = false)
        {
            var data = new EquiDynamicModelsComponent.ModelData
            {
                Model = model.Model,
                Matrix = Matrix.CreateWorld(args.Position, args.Forward, args.Up),
                ColorMask = args.Shared.Color,
                Ghost = ghost,
            };
            Matrix.Rescale(ref data.Matrix, model.Scale.Clamp(args.Scale));
            return data;
        }

        public void AddModel(EquiDecorativeModelToolDefinition.ModelDef def, ModelArgs<BlockAndAnchor> args)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Model, args.Position, BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var rpcArgs = new FeatureArgs
            {
                MaterialId = def.Id,
                ModelForward = VF_Packer.PackNormal(args.Forward),
                ModelUp = VF_Packer.PackNormal(args.Up),
                ModelScale = args.Scale,
                Shared = args.Shared,
            };
            if (TryAddFeatureInternal(in key, def.Owner, rpcArgs))
                RaiseAddFeature_Sync(key, def.Owner.Id, rpcArgs);
        }

        public void RemoveModel(BlockAndAnchor a)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Model, a, BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }
    }

    public partial class MyObjectBuilder_EquiDecorativeMeshComponent
    {
        public struct ModelBuilder
        {
            [XmlAttribute("A")]
            public ulong A;

            [XmlAttribute("AO")]
            public uint AOffset;

            [XmlAttribute("F")]
            public uint Forward;

            [XmlAttribute("U")]
            public uint Up;

            [XmlAttribute("S")]
            public float Scale;

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

        public class DecorativeModels : DecorativeGroup
        {
            [XmlElement("ModelId")]
            [Nullable]
            public string ModelId;

            [XmlElement("Model")]
            public List<ModelBuilder> Models;

            public override void Remap(IMySceneRemapper remapper)
            {
                if (Models == null) return;
                for (var i = 0; i < Models.Count; i++)
                {
                    var model = Models[i];
                    remapper.RemapObject(MyBlock.SceneType, ref model.A);
                    Models[i] = model;
                }
            }
        }
    }
}