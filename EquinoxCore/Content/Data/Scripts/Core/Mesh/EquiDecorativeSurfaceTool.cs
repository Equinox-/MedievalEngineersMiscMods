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
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeSurfaceToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeSurfaceTool : EquiDecorativeToolBase
    {
        private EquiDecorativeSurfaceToolDefinition _definition;

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiDecorativeSurfaceToolDefinition)definition;
        }

        protected override bool ValidateTarget() => HasPermission(MyPermissionsConstants.Build);

        protected override bool Start(MyHandItemActionEnum action) => HasPermission(MyPermissionsConstants.Build);

        private PoolManager.ReturnHandle<List<TR>> GetUnique<TV, TR>(ListReader<TV> values, Func<TV, TR> transform, out List<TR> unique)
            where TR : IEquatable<TR>
        {
            var handle = PoolManager.Get(out unique);
            using (PoolManager.Get(out HashSet<TR> set))
                foreach (var value in values)
                {
                    var transformed = transform(value);
                    if (set.Add(transformed))
                        unique.Add(transformed);
                }

            return handle;
        }

        protected override int RequiredPoints => 4;

        protected override void HitWithEnoughPoints(ListReader<DecorAnchor> points)
        {
            using (GetUnique(points, pt => pt, out var unique))
            {
                if (unique.Count < 3) return;
                var gridData = points[0].Grid;
                var gridPos = gridData.Container.Get<MyPositionComponentBase>();
                var remove = ActiveAction == MyHandItemActionEnum.Secondary;
                if (!remove)
                {
                    var surfData = CreateSurfaceData<DecorAnchor>(gridPos, unique, pt => pt.GridLocalPosition);
                    var durabilityRequired = (int)Math.Ceiling(_definition.DurabilityBase + surfData.Area * _definition.DurabilityPerSquareMeter);
                    if (!TryRemoveDurability(durabilityRequired))
                        return;
                }

                var anchor3 = unique.Count > 3 ? unique[3].Anchor : EquiDecorativeMeshComponent.BlockAndAnchor.Null;
                if (MyMultiplayerModApi.Static.IsServer)
                {
                    var gridDecor = unique[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                    if (remove)
                        gridDecor.RemoveSurface(unique[0].Anchor, unique[1].Anchor, unique[2].Anchor, anchor3);
                    else
                        gridDecor.AddSurface(unique[0].Anchor, unique[1].Anchor, unique[2].Anchor, anchor3, _definition);
                    return;
                }

                MyMultiplayer.RaiseStaticEvent(x => PerformOp,
                    unique[0].Grid.Entity.Id, unique[0].Anchor, unique[1].Anchor, unique[2].Anchor,
                    anchor3, ActiveAction == MyHandItemActionEnum.Secondary);
            }
        }

        [Event, Reliable, Server]
        private static void PerformOp(EntityId grid,
            EquiDecorativeMeshComponent.BlockAndAnchor pt0,
            EquiDecorativeMeshComponent.BlockAndAnchor pt1,
            EquiDecorativeMeshComponent.BlockAndAnchor pt2,
            EquiDecorativeMeshComponent.BlockAndAnchor pt3,
            bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeSurfaceTool behavior)
                || !behavior.Scene.TryGetEntity(grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!gridEntity.Components.TryGet(out MyGridDataComponent gridData))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            using (PoolManager.Get(out List<Vector3> points))
            {
                if (!pt0.TryGetGridLocalAnchor(gridData, out var local0)
                    || !pt1.TryGetGridLocalAnchor(gridData, out var local1)
                    || !pt2.TryGetGridLocalAnchor(gridData, out var local2))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                points.EnsureCapacity(4);
                points.Add(local0);
                points.Add(local1);
                points.Add(local2);
                if (!pt3.IsNull)
                {
                    if (pt3.TryGetGridLocalAnchor(gridData, out var local3))
                        points.Add(local3);
                    else
                    {
                        MyEventContext.ValidationFailed();
                        return;
                    }
                }

                var com = Vector3.Zero;
                foreach (var pos in points)
                    com += pos;
                com /= points.Count;

                if (!NetworkTrust.IsTrusted(gridData, Vector3D.Transform(com, gridEntity.PositionComp.WorldMatrix)))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                if (!remove)
                {
                    var surfData = behavior.CreateSurfaceData<Vector3>(gridEntity.PositionComp, points, pt => pt);
                    var durabilityRequired =
                        (int)Math.Ceiling(behavior._definition.DurabilityBase + surfData.Area * behavior._definition.DurabilityPerSquareMeter);
                    if (!behavior.TryRemoveDurability(durabilityRequired))
                    {
                        MyEventContext.ValidationFailed();
                        return;
                    }
                }
            }

            var gridDecor = gridEntity.Components.GetOrAdd<EquiDecorativeMeshComponent>();
            if (remove)
                gridDecor.RemoveSurface(pt0, pt1, pt2, pt3);
            else
                gridDecor.AddSurface(pt0, pt1, pt2, pt3, behavior._definition);
        }

        private EquiMeshHelpers.SurfaceData CreateSurfaceData<T>(MyPositionComponentBase gridPos, ListReader<T> values, Func<T, Vector3> gridLocalPos)
        {
            var average = Vector3.Zero;
            foreach (var pos in values)
                average += gridLocalPos(pos);
            average /= values.Count;
            var gravityWorld = MyGravityProviderSystem.CalculateNaturalGravityInPoint(Vector3D.Transform(average, gridPos.WorldMatrix));
            var localGravity = Vector3.TransformNormal(gravityWorld, gridPos.WorldMatrixNormalizedInv);
            return EquiDecorativeMeshComponent.CreateSurfaceData(_definition, gridLocalPos(values[0]), gridLocalPos(values[1]), gridLocalPos(values[2]),
                values.Count > 3 ? (Vector3?)gridLocalPos(values[3]) : null, -localGravity);
        }

        protected override void RenderShape(MyGridDataComponent grid, ListReader<Vector3> positions)
        {
            var gridPos = grid.Container.Get<MyPositionComponentBase>();
            EquiMeshHelpers.SurfaceData surfData;
            using (GetUnique(positions, pt => pt, out var unique))
            {
                if (unique.Count < 3) return;
                surfData = CreateSurfaceData<Vector3>(gridPos, unique, pt => pt);
            }

            var msg = MyRenderProxy.PrepareDebugDrawTriangles();
            using (PoolManager.Get(out MyModelData modelData))
            {
                modelData.Clear();
                EquiMeshHelpers.BuildSurface(in surfData, modelData);
                msg.Vertices.EnsureCapacity(modelData.Positions.Count);
                foreach (var pos in modelData.Positions)
                    msg.AddVertex(pos, Color.White);
                msg.Indices.AddCollection(modelData.Indices);
                modelData.Clear();
            }

            MyRenderProxy.DebugDrawTriangles(msg, gridPos.WorldMatrix);
            MyRenderProxy.DebugDrawText2D(new Vector2(-.45f, -.45f), $"Area: {surfData.Area:F2} m²", Color.White, 1f);
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeSurfaceToolDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiDecorativeSurfaceToolDefinition : EquiDecorativeToolBaseDefinition
    {
        public MaterialDescriptor Material { get; private set; }
        public Vector2 TextureSize { get; private set; }
        public bool FlipRearNormals { get; private set; }

        public float DurabilityBase { get; private set; }
        public float DurabilityPerSquareMeter { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeSurfaceToolDefinition)builder;
            Material = ob.Material.Build();
            TextureSize = ob.TextureSize ?? Vector2.One;
            FlipRearNormals = ob.FlipRearNormals ?? true;

            DurabilityBase = ob.DurabilityBase ?? 1;
            DurabilityPerSquareMeter = ob.DurabilityPerSquareMeter ?? 0;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeSurfaceToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        public MaterialSpec Material;
        public SerializableVector2? TextureSize;
        public bool? FlipRearNormals;

        public float? DurabilityBase;
        public float? DurabilityPerSquareMeter;
    }
}