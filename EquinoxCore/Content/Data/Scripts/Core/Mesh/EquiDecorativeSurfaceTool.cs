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
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeSurfaceToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeSurfaceTool : EquiDecorativeToolBase
    {
        private EquiDecorativeSurfaceToolDefinition _definition;

        private EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef MaterialDef =>
            _definition.SortedMaterials[DecorativeToolSettings.SurfaceMaterialIndex % _definition.SortedMaterials.Count];

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

        protected override int RenderPoints => 3;
        protected override int RequiredPoints => 4;

        private float UvScale => _definition.TextureScale.Clamp(DecorativeToolSettings.UvScale);

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
                    var durabilityRequired = (int)Math.Ceiling(MaterialDef.DurabilityBase + surfData.Area * MaterialDef.DurabilityPerSquareMeter);
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
                        gridDecor.AddSurface(MaterialDef, new EquiDecorativeMeshComponent.SurfaceArgs<EquiDecorativeMeshComponent.BlockAndAnchor>
                        {
                            A = unique[0].Anchor,
                            B = unique[1].Anchor,
                            C = unique[2].Anchor,
                            D = anchor3,
                            Color = PackedHsvShift,
                            UvProjection = DecorativeToolSettings.UvProjection,
                            UvBias = DecorativeToolSettings.UvBias,
                            UvScale = UvScale
                        });
                    return;
                }

                MyMultiplayer.RaiseStaticEvent(
                    x => PerformOp,
                    new OpArgs
                    {
                        Grid = unique[0].Grid.Entity.Id,
                        MaterialId = MaterialDef.Id,
                        Pt0 = unique[0].RpcAnchor,
                        Pt1 = unique[1].RpcAnchor,
                        Pt2 = unique[2].RpcAnchor,
                        Pt3 = anchor3,
                        Color = PackedHsvShift,
                        UvProjection = DecorativeToolSettings.UvProjection,
                        UvBias = DecorativeToolSettings.UvBias,
                        UvScale = UvScale,
                    },
                    ActiveAction == MyHandItemActionEnum.Secondary);
            }
        }

        private struct OpArgs
        {
            public EntityId Grid;
            public MyStringHash MaterialId;
            public EquiDecorativeMeshComponent.RpcBlockAndAnchor Pt0;
            public EquiDecorativeMeshComponent.RpcBlockAndAnchor Pt1;
            public EquiDecorativeMeshComponent.RpcBlockAndAnchor Pt2;
            public EquiDecorativeMeshComponent.RpcBlockAndAnchor Pt3;
            public PackedHsvShift Color;
            public UvProjectionMode UvProjection;
            public UvBiasMode UvBias;
            public float UvScale;
        }

        [Event, Reliable, Server]
        private static void PerformOp(OpArgs args, bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeSurfaceTool behavior)
                || !behavior._definition.Materials.TryGetValue(args.MaterialId, out var material)
                || !behavior.Scene.TryGetEntity(args.Grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            EquiDecorativeMeshComponent.BlockAndAnchor pt0 = args.Pt0;
            EquiDecorativeMeshComponent.BlockAndAnchor pt1 = args.Pt1;
            EquiDecorativeMeshComponent.BlockAndAnchor pt2 = args.Pt2;
            EquiDecorativeMeshComponent.BlockAndAnchor pt3 = args.Pt3;

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
                        (int)Math.Ceiling(material.DurabilityBase + surfData.Area * material.DurabilityPerSquareMeter);
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
                gridDecor.AddSurface(
                    material, new EquiDecorativeMeshComponent.SurfaceArgs<EquiDecorativeMeshComponent.BlockAndAnchor>
                    {
                        A = pt0,
                        B = pt1,
                        C = pt2,
                        D = pt3,
                        Color = args.Color,
                        UvProjection = args.UvProjection,
                        UvBias = args.UvBias,
                        UvScale = args.UvScale
                    });
        }

        private EquiMeshHelpers.SurfaceData CreateSurfaceData<T>(MyPositionComponentBase gridPos, ListReader<T> values, Func<T, Vector3> gridLocalPos)
        {
            var average = Vector3.Zero;
            foreach (var pos in values)
                average += gridLocalPos(pos);
            average /= values.Count;
            var gravityWorld = MyGravityProviderSystem.CalculateNaturalGravityInPoint(Vector3D.Transform(average, gridPos.WorldMatrix));
            var localGravity = Vector3.TransformNormal(gravityWorld, gridPos.WorldMatrixNormalizedInv);
            return EquiDecorativeMeshComponent.CreateSurfaceData(
                MaterialDef,
                new EquiDecorativeMeshComponent.SurfaceArgs<Vector3>
                {
                    A = gridLocalPos(values[0]),
                    B = gridLocalPos(values[1]),
                    C = gridLocalPos(values[2]),
                    D = values.Count > 3 ? (Vector3?)gridLocalPos(values[3]) : null,
                    Color = PackedHsvShift,
                    UvProjection = DecorativeToolSettings.UvProjection,
                    UvBias = DecorativeToolSettings.UvBias,
                    UvScale = UvScale
                }, -localGravity);
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

            var color = PackedHsvShift.ToRgb();
            var isoColor = new Color(0xFF - color.R, 0xFF - color.G, 0xFF - color.B);
            var triangleMsg = MyRenderProxy.PrepareDebugDrawTriangles();
            var lineMsg = MyRenderProxy.DebugDrawLine3DOpenBatch(false);
            using (PoolManager.Get(out MyModelData modelData))
            {
                modelData.Clear();
                EquiMeshHelpers.BuildSurface(in surfData, modelData);
                triangleMsg.Vertices.EnsureCapacity(modelData.Positions.Count);
                foreach (var pos in modelData.Positions)
                    triangleMsg.AddVertex(pos, color);
                triangleMsg.Indices.AddCollection(modelData.Indices);

                // Compute some UV iso lines.
                var minIso = float.PositiveInfinity;
                var maxIso = float.NegativeInfinity;
                foreach (var uv in modelData.TexCoords)
                {
                    var iso = Vector2.Dot(uv, MaterialDef.UvGuidePerpendicular);
                    if (iso < minIso) minIso = iso;
                    if (iso > maxIso) maxIso = iso;
                }

                var isoStep = ComputeUvIsoStep(minIso, maxIso);
                var maxIndex = MaterialDef.FlipRearNormals ? modelData.Indices.Count / 2 : modelData.Indices.Count;
                for (var iso = (int)Math.Ceiling(minIso / isoStep); iso <= Math.Floor(maxIso / isoStep); iso++)
                {
                    var isoLevel = iso * isoStep;
                    for (var i = 0; i < maxIndex; i += 3)
                        if (TryComputeTriangleIso(modelData, i, isoLevel, out var a, out var b))
                            lineMsg.AddLine(a, isoColor, b, isoColor);
                }

                modelData.Clear();
            }

            MyRenderProxy.DebugDrawTriangles(triangleMsg, gridPos.WorldMatrix);
            lineMsg.WorldMatrix = gridPos.WorldMatrix;
            MyRenderProxy.DebugDrawLine3DSubmitBatch(lineMsg);
            MyRenderProxy.DebugDrawText2D(DebugTextAnchor, $"Area: {surfData.Area:F2} m²", Color.White, 1f);
        }

        private float ComputeUvIsoStep(float min, float max)
        {
            var delta = max - min;
            if (delta <= 1e-9f) return (max - min) / 2;
            var optimalStep = delta / 4;
            return (float)Math.Pow(2, Math.Floor(Math.Log(optimalStep) * MathHelper.Log2E));
        }

        private bool TryComputeTriangleIso(MyModelData model, int offset, float iso, out Vector3 a, out Vector3 b)
        {
            a = default;
            b = default;
            var first = true;
            for (var j = 0; j < 3; j++)
            {
                var i0 = model.Indices[offset + j];
                var i1 = model.Indices[offset + (j + 1) % 3];
                var uv0 = Vector2.Dot(MaterialDef.UvGuidePerpendicular, model.TexCoords[i0]);
                var uv1 = Vector2.Dot(MaterialDef.UvGuidePerpendicular, model.TexCoords[i1]);
                if (Math.Min(uv0, uv1) > iso || Math.Max(uv0, uv1) < iso)
                    continue;
                var t = (iso - uv0) / (uv1 - uv0);
                var v0 = model.Positions[i0];
                var v1 = model.Positions[i1];
                var pt = Vector3.Lerp(v0, v1, t);
                if (first)
                {
                    a = pt;
                    first = false;
                    continue;
                }

                b = pt;
                return true;
            }

            return false;
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeSurfaceToolDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiDecorativeSurfaceToolDefinition : EquiDecorativeToolBaseDefinition
    {
        public DictionaryReader<MyStringHash, SurfaceMaterialDef> Materials { get; private set; }
        public ListReader<SurfaceMaterialDef> SortedMaterials { get; private set; }
        public ImmutableRange<float> TextureScale { get; private set; }


        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeSurfaceToolDefinition)builder;

            TextureScale = ob.TextureScale?.Immutable() ?? new ImmutableRange<float>(1, 1);
            var materials = new Dictionary<MyStringHash, SurfaceMaterialDef>();
            if (ob.Material != null)
            {
                var def = new SurfaceMaterialDef(this, ob, new MyObjectBuilder_EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef
                {
                    Id = "",
                    Material = ob.Material,
                    TextureSize = ob.TextureSize,
                    FlipRearNormals = ob.FlipRearNormals,
                    UvGuide = ob.UvGuide,
                });
                materials[def.Id] = def;
            }

            if (ob.Materials != null)
                foreach (var mtl in ob.Materials)
                {
                    var def = new SurfaceMaterialDef(this, ob, mtl);
                    materials[def.Id] = def;
                }

            SortedMaterials = materials.Values.OrderBy(x => x.Name).ToList();
            Materials = materials;
        }

        public class SurfaceMaterialDef : IDecorativeMaterial
        {
            public readonly EquiDecorativeSurfaceToolDefinition Owner;
            public readonly MyStringHash Id;
            public string Name { get; }
            public string[] UiIcons { get; }

            public MaterialDescriptor Material { get; private set; }
            public Vector2 TextureSize { get; private set; }
            public bool FlipRearNormals { get; private set; }
            public Vector2 UvGuidePerpendicular { get; private set; }
            public readonly float DurabilityBase;
            public readonly float DurabilityPerSquareMeter;

            internal SurfaceMaterialDef(
                EquiDecorativeSurfaceToolDefinition owner,
                MyObjectBuilder_EquiDecorativeSurfaceToolDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef ob)
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
                    Log.Warning($"Surface material {owner.Id}/{Name} has no UI icon.  Add <UiIcon> tag to the decal.");
                    UiIcons = null;
                }

                Material = ob.Material.Build();
                Material = ob.Material.Build();
                TextureSize = ob.TextureSize ?? ownerOb.TextureSize ?? Vector2.One;
                FlipRearNormals = ob.FlipRearNormals ?? ownerOb.FlipRearNormals ?? true;
                var uvGuideTangent = ob.UvGuide ?? ownerOb.UvGuide ?? Vector2.UnitX;
                UvGuidePerpendicular = new Vector2(-uvGuideTangent.Y, uvGuideTangent.X);
                DurabilityBase = ob.DurabilityBase ?? ownerOb.DurabilityBase ?? 1;
                DurabilityPerSquareMeter = ob.DurabilityPerSquareMeter ?? ownerOb.DurabilityPerSquareMeter ?? 0;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeSurfaceToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        public MaterialSpec Material;
        public SerializableVector2? TextureSize;

        public MutableRange<float>? TextureScale;

        public bool? FlipRearNormals;

        public float? DurabilityBase;
        public float? DurabilityPerSquareMeter;

        public SerializableVector2? UvGuide;

        [XmlElement("Variant")]
        public SurfaceMaterialDef[] Materials;

        public class SurfaceMaterialDef
        {
            [XmlAttribute("Id")]
            public string Id;

            [XmlAttribute("Name")]
            public string Name;

            [XmlElement]
            public MaterialSpec Material;

            [XmlElement("UiIcon")]
            public string[] UiIcons;

            public SerializableVector2? TextureSize;
            public MutableRange<float>? TextureScale;
            public bool? FlipRearNormals;
            public SerializableVector2? UvGuide;
            public float? DurabilityBase;
            public float? DurabilityPerSquareMeter;
        }
    }

    public enum UvProjectionMode
    {
        Cube,
        Bevel,
        Count,
    }

    public enum UvBiasMode
    {
        XAxis,
        YAxis,
        ZAxis,
        Count,
    }
}