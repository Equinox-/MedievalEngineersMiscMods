using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
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
    public class EquiDecorativeLineTool : EquiDecorativeToolBase<EquiDecorativeLineToolDefinition, EquiDecorativeLineToolDefinition.LineMaterialDef>
    {
        private EquiDecorativeLineToolDefinition.LineMaterialDef MaterialDef =>
            Def.SortedMaterials[DecorativeToolSettings.LineMaterialIndex % Def.SortedMaterials.Count];

        protected override int RequiredPoints => 2;

        private float CorrectWidth(float width) => width < 0 ? -1 : Def.WidthRange.Clamp(width);

        private float WidthA => CorrectWidth(DecorativeToolSettings.LineWidthA);
        private float WidthB => CorrectWidth(DecorativeToolSettings.LineWidthB);

        protected override void HitWithEnoughPoints(ListReader<BlockAnchorInteraction> points)
        {
            if (points.Count < 2 || points[0].Equals(points[1])) return;
            var remove = ActiveAction == MyHandItemActionEnum.Secondary;
            if (!remove)
            {
                var length = Vector3.Distance(points[0].GridLocalPosition, points[1].GridLocalPosition);
                var durabilityCost = (int)Math.Ceiling(MaterialDef.DurabilityBase + MaterialDef.DurabilityPerMeter * length);
                if (!TryRemovePreReqs(durabilityCost, MaterialDef))
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
                        new EquiDecorativeMeshComponent.LineArgs<BlockAndAnchor>
                        {
                            A = points[0].Anchor,
                            B = points[1].Anchor,
                            CatenaryFactor = DecorativeToolSettings.LineCatenaryFactor,
                            WidthA = WidthA,
                            WidthB = WidthB,
                            Shared =
                            {
                                Color = PackedHsvShift
                            },
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
            public RpcBlockAndAnchor Pt0, Pt1;
            public float CatenaryFactor;
            public PackedHsvShift Color;
            public float WidthA, WidthB;
        }

        [Event, Reliable, Server]
        private static void PerformOp(OpArgs args, bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeLineTool behavior)
                || !behavior.Def.Materials.TryGetValue(args.MaterialId, out var materialDef)
                || !behavior.Scene.TryGetEntity(args.Grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            BlockAndAnchor pt0 = args.Pt0;
            BlockAndAnchor pt1 = args.Pt1;

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
                if (!behavior.TryRemovePreReqs(durabilityCost, materialDef))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            var gridDecor = gridEntity.Components.GetOrAdd<EquiDecorativeMeshComponent>();
            if (remove)
                gridDecor.RemoveLine(pt0, pt1);
            else
                gridDecor.AddLine(materialDef, new EquiDecorativeMeshComponent.LineArgs<BlockAndAnchor>
                {
                    A = pt0,
                    B = pt1,
                    CatenaryFactor = args.CatenaryFactor,
                    WidthA = behavior.CorrectWidth(args.WidthA),
                    WidthB = behavior.CorrectWidth(args.WidthB),
                    Shared =
                    {
                        Color = args.Color,
                    },
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
                WidthA = WidthA,
                WidthB = WidthB,
                Shared =
                {
                    Color = PackedHsvShift
                },
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
    public class EquiDecorativeLineToolDefinition : EquiDecorativeToolBaseDefinition,
        IEquiDecorativeToolBaseDefinition<EquiDecorativeLineToolDefinition.LineMaterialDef>
    {
        private readonly MaterialHolder<LineMaterialDef> _holder = new MaterialHolder<LineMaterialDef>();
        public DictionaryReader<MyStringHash, LineMaterialDef> Materials => _holder.Materials;
        public ListReader<LineMaterialDef> SortedMaterials => _holder.SortedMaterials;

        private const int MaxHalfSideSegments = 8;

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

            if (ob.WidthRange.HasValue)
                WidthRange = ob.WidthRange.Value.Immutable();
            else if (ob.Width.HasValue)
                WidthRange = new ImmutableRange<float>(ob.Width.Value, ob.Width.Value);
            else
                WidthRange = new ImmutableRange<float>(0.05f, 0.05f);
            DefaultWidth = ob.DefaultWidth.HasValue ? WidthRange.Clamp(ob.DefaultWidth.Value) : WidthRange.FromRatio(0.5f);

            SegmentsPerMeter = Math.Max(0, ob.SegmentsPerMeter ?? 0);
            SegmentsPerMeterSqrt = ob.SegmentsPerMeterSqrt;
            _minHalfSideSegments = Math.Max(2, ob.HalfSideSegments ?? 0);
            _maxCircleError = Math.Max(0.01f, ob.MaxCircleError ?? 0);

            if (ob.Material != null)
                _holder.Add(new LineMaterialDef(this, ob, new MyObjectBuilder_EquiDecorativeLineToolDefinition.LineMaterialDef
                {
                    Id = "",
                    Material = ob.Material
                }));

            if (ob.Materials != null)
                foreach (var mtl in ob.Materials)
                    _holder.Add(new LineMaterialDef(this, ob, mtl));
        }

        public class LineMaterialDef : MaterialDef<EquiDecorativeLineToolDefinition>
        {
            public readonly MaterialDescriptor Material;
            public readonly Vector2 UvOffset;
            public readonly Vector2 UvNormal;
            public readonly Vector2 UvTangentPerMeter;
            public readonly float DurabilityPerMeter;

            internal LineMaterialDef(EquiDecorativeLineToolDefinition owner,
                MyObjectBuilder_EquiDecorativeLineToolDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeLineToolDefinition.LineMaterialDef ob) : base(owner, ownerOb, ob, ob.Material?.Icons)
            {
                Material = ob.Material.Build();
                UvOffset = ob.UvOffset ?? ownerOb.UvOffset ?? Vector2.Zero;
                UvNormal = ob.UvNormal ?? ownerOb.UvNormal ?? new Vector2(0, 1);
                UvTangentPerMeter = ob.UvTangentPerMeter ?? ownerOb.UvTangentPerMeter ?? new Vector2(10, 0);
                DurabilityPerMeter = ob.DurabilityPerMeter ?? ownerOb.DurabilityPerMeter ?? 0;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeLineToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        /// <summary>
        /// PBR material definition.
        /// </summary>
        [XmlElement]
        public MaterialSpec Material;

        /// <summary>
        /// Constant width in meters. Defaults to 0.05m.
        /// </summary>
        [XmlElement]
        public float? Width;

        /// <summary>
        /// Default width value.
        /// </summary>
        [XmlElement]
        public float? DefaultWidth;

        /// <summary>
        /// Range of permitted width values. Defaults to constant width.
        /// <see cref="Width"/>
        /// </summary>
        [XmlElement]
        public MutableRange<float>? WidthRange;

        /// <inheritdoc cref="LineMaterialDef.UvOffset"/>
        [XmlElement]
        public SerializableVector2? UvOffset;

        /// <inheritdoc cref="LineMaterialDef.UvNormal"/>
        [XmlElement]
        public SerializableVector2? UvNormal;

        /// <inheritdoc cref="LineMaterialDef.UvTangentPerMeter"/>
        [XmlElement]
        public SerializableVector2? UvTangentPerMeter;

        /// <summary>
        /// Number of straight line segments per square root of meter of length when the line is sagging.
        /// </summary>
        [XmlElement]
        public float? SegmentsPerMeterSqrt;

        /// <summary>
        /// Number of straight line segments per meter of length when the line is sagging.
        /// </summary>
        [XmlElement]
        public float? SegmentsPerMeter;

        /// <summary>
        /// Half the minimum number of vertices used to represent a cross section. Defaults to 2. 
        /// </summary>
        [XmlElement]
        public int? HalfSideSegments;

        /// <summary>
        /// Maximum error in meters between true circle and n-gon representing the circle in meters. Defaults to 0.01m.
        /// </summary>
        [XmlElement]
        public float? MaxCircleError;

        /// <inheritdoc cref="LineMaterialDef.DurabilityPerMeter"/>
        [XmlElement]
        public float? DurabilityPerMeter;

        [XmlElement("Variant")]
        public LineMaterialDef[] Materials;

        public class LineMaterialDef : MaterialDef
        {
            /// <summary>
            /// PBR material definition.
            /// </summary>
            [XmlElement]
            public MaterialSpec Material;

            /// <summary>
            /// Texture coordinate offset for the beginning of the unwrapped line. Defaults to zero.
            /// </summary>
            [XmlElement]
            public SerializableVector2? UvOffset;

            /// <summary>
            /// Texture coordinate delta for wrapping around the line. Defaults to (0, 1).
            /// </summary>
            [XmlElement]
            public SerializableVector2? UvNormal;

            /// <summary>
            /// Texture coordinate delta along the line, for each meter of length. Defaults to (10, 0).
            /// </summary>
            [XmlElement]
            public SerializableVector2? UvTangentPerMeter;

            /// <summary>
            /// Durability cost per meter of placed line.
            /// </summary>
            [XmlElement]
            public float? DurabilityPerMeter;
        }
    }
}