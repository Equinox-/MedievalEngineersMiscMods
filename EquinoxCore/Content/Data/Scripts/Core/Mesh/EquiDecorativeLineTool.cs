using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Medieval.Constants;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeLineToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeLineTool : EquiDecorativeToolBase
    {
        private EquiDecorativeLineToolDefinition _definition;

        private EquiDecorativeLineToolDefinition.LineMaterialDef MaterialDef =>
            _definition.SortedMaterials[DecorativeToolSettings.LineMaterialIndex % _definition.SortedMaterials.Count];

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiDecorativeLineToolDefinition)definition;
        }

        protected override bool ValidateTarget() => HasPermission(MyPermissionsConstants.Build);

        protected override bool Start(MyHandItemActionEnum action) => HasPermission(MyPermissionsConstants.Build);

        protected override int RequiredPoints => 2;

        private float CorrectWidth(float width) => width < 0 ? -1 : _definition.WidthRange.Clamp(width); 

        private float WidthA => CorrectWidth(DecorativeToolSettings.LineWidthA);
        private float WidthB => CorrectWidth(DecorativeToolSettings.LineWidthB);

        protected override void HitWithEnoughPoints(ListReader<DecorAnchor> points)
        {
            if (points.Count < 2 || points[0].Equals(points[1])) return;
            var remove = ActiveAction == MyHandItemActionEnum.Secondary;
            if (!remove)
            {
                var length = Vector3.Distance(points[0].GridLocalPosition, points[1].GridLocalPosition);
                var durabilityCost = (int)Math.Ceiling(MaterialDef.DurabilityBase + MaterialDef.DurabilityPerMeter * length);
                if (!TryRemoveDurability(durabilityCost))
                    return;
            }

            if (MyMultiplayerModApi.Static.IsServer)
            {
                var gridDecor = points[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                if (remove)
                    gridDecor.RemoveLine(points[0].Anchor, points[1].Anchor);
                else
                    gridDecor.AddLine(
                        MaterialDef,
                        new EquiDecorativeMeshComponent.LineArgs<EquiDecorativeMeshComponent.BlockAndAnchor>
                        {
                            A = points[0].Anchor,
                            B = points[1].Anchor,
                            CatenaryFactor = DecorativeToolSettings.LineCatenaryFactor,
                            Color = PackedHsvShift,
                            WidthA = WidthA,
                            WidthB = WidthB,
                        });
                return;
            }

            MyMultiplayer.RaiseStaticEvent(
                x => PerformOp,
                new OpArgs
                {
                    Grid = points[0].Grid.Entity.Id,
                    MaterialId = MaterialDef.Id,
                    Pt0 = points[0].RpcAnchor,
                    Pt1 = points[1].RpcAnchor,
                    CatenaryFactor = DecorativeToolSettings.LineCatenaryFactor,
                    Color = PackedHsvShift,
                    WidthA = WidthA,
                    WidthB = WidthB,
                },
                remove);
        }

        [RpcSerializable]
        private struct OpArgs
        {
            public EntityId Grid;
            public MyStringHash MaterialId;
            public EquiDecorativeMeshComponent.RpcBlockAndAnchor Pt0, Pt1;
            public float CatenaryFactor;
            public PackedHsvShift Color;
            public float WidthA, WidthB;
        }

        [Event, Reliable, Server]
        private static void PerformOp(OpArgs args, bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeLineTool behavior)
                || !behavior._definition.Materials.TryGetValue(args.MaterialId, out var materialDef)
                || !behavior.Scene.TryGetEntity(args.Grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            EquiDecorativeMeshComponent.BlockAndAnchor pt0 = args.Pt0;
            EquiDecorativeMeshComponent.BlockAndAnchor pt1 = args.Pt1;

            if (!gridEntity.Components.TryGet(out MyGridDataComponent gridData)
                || !pt0.TryGetGridLocalAnchor(gridData, out var local0)
                || !pt1.TryGetGridLocalAnchor(gridData, out var local1))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!NetworkTrust.IsTrusted(gridData, Vector3D.Transform((local0 + local1) / 2, gridEntity.PositionComp.WorldMatrix)))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!remove)
            {
                var length = Vector3.Distance(local0, local1);
                var durabilityCost = (int)Math.Ceiling(materialDef.DurabilityBase + materialDef.DurabilityPerMeter * length);
                if (!behavior.TryRemoveDurability(durabilityCost))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            var gridDecor = gridEntity.Components.GetOrAdd<EquiDecorativeMeshComponent>();
            if (remove)
                gridDecor.RemoveLine(pt0, pt1);
            else
                gridDecor.AddLine(materialDef, new EquiDecorativeMeshComponent.LineArgs<EquiDecorativeMeshComponent.BlockAndAnchor>
                {
                    A = pt0,
                    B = pt1,
                    CatenaryFactor = args.CatenaryFactor,
                    Color = args.Color,
                    WidthA = behavior.CorrectWidth(args.WidthA),
                    WidthB = behavior.CorrectWidth(args.WidthB),
                });
        }

        protected override void RenderShape(MyGridDataComponent grid, ListReader<Vector3> positions)
        {
            var gridPos = grid.Container.Get<MyPositionComponentBase>();
            var line = EquiDecorativeMeshComponent.CreateLineData(MaterialDef, new EquiDecorativeMeshComponent.LineArgs<Vector3>
            {
                A = positions[0],
                B = positions[1],
                CatenaryFactor = DecorativeToolSettings.LineCatenaryFactor,
                Color = PackedHsvShift,
                WidthA = WidthA,
                WidthB = WidthB,
            });
            if (line.CatenaryLength > 0 && line.UseNaturalGravity)
                line.Gravity = Vector3.TransformNormal(
                    MyGravityProviderSystem.CalculateNaturalGravityInPoint(Holder.GetPosition()),
                    gridPos.WorldMatrixNormalizedInv);
            var length = Vector3.Distance(positions[0], positions[1]) * (1 + DecorativeToolSettings.LineCatenaryFactor);
            MyRenderProxy.DebugDrawText2D(DebugTextAnchor, $"Length: {length:F2} m", Color.White, 1f);

            
            var triangleMsg = MyRenderProxy.PrepareDebugDrawTriangles();
            var color = PackedHsvShift.ToRgb();
            using (PoolManager.Get(out MyModelData modelData))
            {
                modelData.Clear();
                EquiMeshHelpers.BuildLine(in line, modelData);
                triangleMsg.Vertices.EnsureCapacity(modelData.Positions.Count);
                foreach (var pos in modelData.Positions)
                    triangleMsg.AddVertex(pos, color);
                triangleMsg.Indices.AddCollection(modelData.Indices);
                modelData.Clear();
            }

            MyRenderProxy.DebugDrawTriangles(triangleMsg, gridPos.WorldMatrix);
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeLineToolDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiDecorativeLineToolDefinition : EquiDecorativeToolBaseDefinition
    {
        private const int MaxHalfSideSegments = 8;

        public DictionaryReader<MyStringHash, LineMaterialDef> Materials { get; private set; }
        public ListReader<LineMaterialDef> SortedMaterials { get; private set; }

        public float DefaultWidth { get; private set; }
        public ImmutableRange<float> WidthRange { get; private set; }
        public float SegmentsPerMeter { get; private set; }

        public float? SegmentsPerMeterSqrt { get; private set; }
        private int _minHalfSideSegments;
        private float _maxCircleError;

        public int HalfSideSegments(float radius)
        {
            var cos = 1 - _maxCircleError / radius;
            if (cos <= 0)
                return _minHalfSideSegments;
            if (cos >= 1)
                return MaxHalfSideSegments;
            return Math.Max(_minHalfSideSegments, (int)Math.Round(Math.PI / 2 / Math.Acos(cos)));
        }
        
        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeLineToolDefinition)builder;

            WidthRange = ob.WidthRange?.Immutable() ?? new ImmutableRange<float>(0.05f, 0.05f);
            DefaultWidth = ob.DefaultWidth.HasValue ? WidthRange.Clamp(ob.DefaultWidth.Value) : WidthRange.FromRatio(0.5f);

            SegmentsPerMeter = Math.Max(0, ob.SegmentsPerMeter ?? 0);
            SegmentsPerMeterSqrt = ob.SegmentsPerMeterSqrt;
            _minHalfSideSegments = Math.Max(2, ob.HalfSideSegments ?? 0);
            _maxCircleError = Math.Max(0.01f, ob.MaxCircleError ?? 0);

            var materials = new Dictionary<MyStringHash, LineMaterialDef>();
            if (ob.Material != null)
            {
                var def = new LineMaterialDef(this, ob, new MyObjectBuilder_EquiDecorativeLineToolDefinition.LineMaterialDef
                {
                    Id = "",
                    Material = ob.Material
                });
                materials[def.Id] = def;
            }

            if (ob.Materials != null)
                foreach (var mtl in ob.Materials)
                {
                    var def = new LineMaterialDef(this, ob, mtl);
                    materials[def.Id] = def;
                }

            SortedMaterials = materials.Values.OrderBy(x => x.Name).ToList();
            Materials = materials;
        }

        public class LineMaterialDef : IDecorativeMaterial
        {
            public readonly EquiDecorativeLineToolDefinition Owner;
            public readonly MyStringHash Id;
            public string Name { get; }
            public string[] UiIcons { get; }
            public readonly MaterialDescriptor Material;
            public readonly Vector2 UvOffset;
            public readonly Vector2 UvNormal;
            public readonly Vector2 UvTangentPerMeter;

            public readonly float DurabilityBase;
            public readonly float DurabilityPerMeter;

            internal LineMaterialDef(EquiDecorativeLineToolDefinition owner,
                MyObjectBuilder_EquiDecorativeLineToolDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeLineToolDefinition.LineMaterialDef ob)
            {
                Owner = owner;
                Id = MyStringHash.GetOrCompute(ob.Id);
                Name = ob.Name ?? EquiDecorativeMaterialController.NameFromId(Id);

                if (ob.UiIcons != null && ob.UiIcons.Length > 0)
                    UiIcons = ob.UiIcons;
                else if (ob.Material.Icons != null && ob.Material.Icons.Count > 0)
                    UiIcons = ob.Material.Icons.ToArray();
                else
                {
                    Log.Warning($"Line material {owner.Id}/{Name} has no UI icon.  Add <UiIcon> tag to the decal.");
                    UiIcons = null;
                }

                Material = ob.Material.Build();
                UvOffset = ob.UvOffset ?? ownerOb.UvOffset ?? Vector2.Zero;
                UvNormal = ob.UvNormal ?? ownerOb.UvNormal ?? new Vector2(0, 1);
                UvTangentPerMeter = ob.UvTangentPerMeter ?? ownerOb.UvTangentPerMeter ?? new Vector2(10, 0);

                DurabilityBase = ob.DurabilityBase ?? ownerOb.DurabilityBase ?? 1;
                DurabilityPerMeter = ob.DurabilityPerMeter ?? ownerOb.DurabilityPerMeter ?? 0;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeLineToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        public MaterialSpec Material;

        [XmlElement]
        public float? Width
        {
            set
            {
                if (value.HasValue)
                    WidthRange = new MutableRange<float>(value.Value, value.Value);
            }
        }

        [XmlElement]
        public float? DefaultWidth;

        [XmlElement]
        public MutableRange<float>? WidthRange;

        public SerializableVector2? UvOffset;
        public SerializableVector2? UvNormal;
        public SerializableVector2? UvTangentPerMeter;
        public float? SegmentsPerMeterSqrt;
        public float? SegmentsPerMeter;
        public int? HalfSideSegments;
        public float? MaxCircleError;

        public float? DurabilityBase;
        public float? DurabilityPerMeter;

        [XmlElement("Variant")]
        public LineMaterialDef[] Materials;

        public class LineMaterialDef
        {
            [XmlAttribute("Id")]
            public string Id;

            [XmlAttribute("Name")]
            public string Name;

            [XmlElement]
            public MaterialSpec Material;

            [XmlElement("UiIcon")]
            public string[] UiIcons;

            public SerializableVector2? UvOffset;
            public SerializableVector2? UvNormal;
            public SerializableVector2? UvTangentPerMeter;

            public float? DurabilityBase;
            public float? DurabilityPerMeter;
        }
    }
}