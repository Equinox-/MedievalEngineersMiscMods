using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
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
using VRage.Input.Devices.Keyboard;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
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
        private float _catenaryFactor;

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiDecorativeLineToolDefinition)definition;
        }

        protected override bool ValidateTarget() => HasPermission(MyPermissionsConstants.Build);

        protected override bool Start(MyHandItemActionEnum action) => HasPermission(MyPermissionsConstants.Build);

        protected override int RequiredPoints => 2;

        protected override void HitWithEnoughPoints(ListReader<DecorAnchor> points)
        {
            if (points.Count < 2 || points[0].Equals(points[1])) return;
            var remove = ActiveAction == MyHandItemActionEnum.Secondary;
            if (!remove)
            {
                var length = Vector3.Distance(points[0].GridLocalPosition, points[1].GridLocalPosition);
                var durabilityCost = (int)Math.Ceiling(_definition.DurabilityBase + _definition.DurabilityPerMeter * length);
                if (!TryRemoveDurability(durabilityCost))
                    return;
            }

            if (MyMultiplayerModApi.Static.IsServer)
            {
                var gridDecor = points[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                if (remove)
                    gridDecor.RemoveLine(points[0].Anchor, points[1].Anchor);
                else
                    gridDecor.AddLine(points[0].Anchor, points[1].Anchor, _definition, _catenaryFactor, _color);
                return;
            }

            MyMultiplayer.RaiseStaticEvent(
                x => PerformOp,
                points[0].Grid.Entity.Id,
                points[0].RpcAnchor,
                points[1].RpcAnchor,
                _catenaryFactor,
                _color,
                remove);
        }

        [Event, Reliable, Server]
        private static void PerformOp(EntityId grid,
            EquiDecorativeMeshComponent.RpcBlockAndAnchor rpcPt0,
            EquiDecorativeMeshComponent.RpcBlockAndAnchor rpcPt1,
            float catenaryFactor,
            PackedHsvShift color,
            bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeLineTool behavior)
                || !behavior.Scene.TryGetEntity(grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            EquiDecorativeMeshComponent.BlockAndAnchor pt0 = rpcPt0;
            EquiDecorativeMeshComponent.BlockAndAnchor pt1 = rpcPt1;

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
                var durabilityCost = (int)Math.Ceiling(behavior._definition.DurabilityBase + behavior._definition.DurabilityPerMeter * length);
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
                gridDecor.AddLine(pt0, pt1, behavior._definition, catenaryFactor, color);
        }

        protected override void RenderShape(MyGridDataComponent grid, ListReader<Vector3> positions)
        {
            var catenaryDelta = Math.Sign(MyAPIGateway.Input.MouseScrollWheelValue())
                                * (MyAPIGateway.Input.IsKeyDown(MyKeys.Shift) ? 4 : 1)
                                * MathHelper.Lerp(1 / 1000f, 1 / 50f, _catenaryFactor);
            _catenaryFactor = MathHelper.Clamp(_catenaryFactor + catenaryDelta, 0, 1);

            var gridPos = grid.Container.Get<MyPositionComponentBase>();
            var line = EquiDecorativeMeshComponent.CreateLineData(_definition, positions[0], positions[1], _catenaryFactor);
            if (line.CatenaryLength > 0 && line.UseNaturalGravity)
                line.Gravity = Vector3.TransformNormal(
                    MyGravityProviderSystem.CalculateNaturalGravityInPoint(Holder.GetPosition()),
                    gridPos.WorldMatrixNormalizedInv);
            var length = Vector3.Distance(positions[0], positions[1]) * (1 + _catenaryFactor);
            MyRenderProxy.DebugDrawText2D(new Vector2(-.45f, -.45f),
                $"Length: {length:F2} m\nExtra Length: {_catenaryFactor * 100:F2} %", Color.White, 1f);
            using (PoolManager.Get<List<Vector3>>(out var points))
            {
                points.EnsureCapacity(line.Segments + 1);
                if (!EquiMeshHelpers.TrySolveCatenary(in line, points))
                    for (var i = 0; i <= line.Segments; i++)
                        points.Add(Vector3.Lerp(line.Pt0, line.Pt1, i / (float)line.Segments));
                var msg = MyRenderProxy.DebugDrawLine3DOpenBatch(true);
                msg.WorldMatrix = gridPos.WorldMatrix;
                msg.Lines.EnsureCapacity((points.Count - 1) * 2);
                var color = _color.ToRgb();
                for (var i = 0; i < points.Count; i++)
                {
                    var pt = new MyFormatPositionColor { Position = points[i], Color = color };
                    msg.Lines.Add(pt);
                    if (i > 0 && i < points.Count - 1)
                        msg.Lines.Add(pt);
                }

                MyRenderProxy.DebugDrawLine3DSubmitBatch(msg);
            }
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeLineToolDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiDecorativeLineToolDefinition : EquiDecorativeToolBaseDefinition
    {
        public MaterialDescriptor Material { get; private set; }

        public float Width { get; private set; }

        public Vector2 UvOffset { get; private set; }
        public Vector2 UvNormal { get; private set; }
        public Vector2 UvTangentPerMeter { get; private set; }

        public float SegmentsPerMeter { get; private set; }

        public float? SegmentsPerMeterSqrt { get; private set; }
        public int HalfSideSegments { get; private set; }

        public float DurabilityBase { get; private set; }
        public float DurabilityPerMeter { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeLineToolDefinition)builder;
            Material = ob.Material.Build();
            Width = ob.Width ?? 0.05f;
            UvOffset = ob.UvOffset ?? Vector2.Zero;
            UvNormal = ob.UvNormal ?? new Vector2(0, 1);
            UvTangentPerMeter = ob.UvTangentPerMeter ?? new Vector2(10, 0);
            SegmentsPerMeter = Math.Max(0, ob.SegmentsPerMeter ?? 0);
            SegmentsPerMeterSqrt = ob.SegmentsPerMeterSqrt;
            HalfSideSegments = Math.Max(2, ob.HalfSideSegments ?? 2);
            DurabilityBase = ob.DurabilityBase ?? 1;
            DurabilityPerMeter = ob.DurabilityPerMeter ?? 0;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeLineToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        public MaterialSpec Material;
        public float? Width;
        public SerializableVector2? UvOffset;
        public SerializableVector2? UvNormal;
        public SerializableVector2? UvTangentPerMeter;
        public float? SegmentsPerMeterSqrt;
        public float? SegmentsPerMeter;
        public int? HalfSideSegments;

        public float? DurabilityBase;
        public float? DurabilityPerMeter;
    }
}