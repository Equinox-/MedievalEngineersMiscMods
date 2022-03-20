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
using VRage.ObjectBuilders;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeSurfaceToolDefinition))]
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
                var gridDecor = gridData.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                if (ActiveAction == MyHandItemActionEnum.Secondary)
                {
                    gridDecor.RemoveSurface(
                        unique[0].Anchor,
                        unique[1].Anchor,
                        unique[2].Anchor,
                        unique.Count > 3 ? unique[3].Anchor : EquiDecorativeMeshComponent.BlockAndAnchor.Null);
                    return;
                }
                var surfData = CreateSurfaceData<DecorAnchor>(gridPos, unique, pt => pt.GridLocalPosition);
                var durabilityRequired = (int)Math.Ceiling(_definition.DurabilityBase + surfData.Area * _definition.DurabilityPerSquareMeter);
                if (!TryRemoveDurability(durabilityRequired))
                    return;
                gridDecor.AddSurface(
                    unique[0].Anchor,
                    unique[1].Anchor,
                    unique[2].Anchor,
                    unique.Count > 3 ? unique[3].Anchor : EquiDecorativeMeshComponent.BlockAndAnchor.Null,
                    _definition);
            }
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
        public MyMaterialDescriptor Material { get; private set; }
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