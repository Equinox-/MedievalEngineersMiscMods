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
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeSurfaceToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeSurfaceTool : EquiDecorativeToolBase<EquiDecorativeSurfaceToolDefinition, EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef>
    {
        private EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef MaterialDef =>
            Def.SortedMaterials[DecorativeToolSettings.SurfaceMaterialIndex % Def.SortedMaterials.Count];

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

        private float UvScale => Def.TextureScale.Clamp(DecorativeToolSettings.UvScale);

        protected override void HitWithEnoughPoints(ListReader<BlockAnchorInteraction> points)
        {
            using (GetUnique(points, pt => pt, out var unique))
            {
                if (unique.Count < 3) return;
                var gridData = points[0].Grid;
                var gridPos = gridData.Container.Get<MyPositionComponentBase>();
                var remove = ActiveAction == MyHandItemActionEnum.Secondary;
                if (!remove)
                {
                    var surfData = CreateSurfaceData<BlockAnchorInteraction>(gridPos, unique, pt => pt.GridLocalPosition);
                    var durabilityRequired = (int)Math.Ceiling(MaterialDef.DurabilityBase + surfData.Area * MaterialDef.DurabilityPerSquareMeter);
                    if (!TryRemovePreReqs(durabilityRequired, MaterialDef))
                        return;
                }

                var anchor3 = unique.Count > 3 ? unique[3].Anchor : BlockAndAnchor.Null;
                if (MyMultiplayerModApi.Static.IsServer)
                {
                    var gridDecor = unique[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                    if (remove)
                        gridDecor.RemoveSurface(unique[0].Anchor, unique[1].Anchor, unique[2].Anchor, anchor3);
                    else
                        gridDecor.AddSurface(MaterialDef, new EquiDecorativeMeshComponent.SurfaceArgs<BlockAndAnchor>
                        {
                            A = unique[0].Anchor,
                            B = unique[1].Anchor,
                            C = unique[2].Anchor,
                            D = anchor3,
                            UvProjection = DecorativeToolSettings.UvProjection,
                            UvBias = DecorativeToolSettings.UvBias,
                            UvScale = UvScale,
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
            public RpcBlockAndAnchor Pt0;
            public RpcBlockAndAnchor Pt1;
            public RpcBlockAndAnchor Pt2;
            public RpcBlockAndAnchor Pt3;
            public PackedHsvShift Color;
            public UvProjectionMode UvProjection;
            public UvBiasMode UvBias;
            public float UvScale;
        }

        [Event, Reliable, Server]
        private static void PerformOp(OpArgs args, bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeSurfaceTool behavior)
                || !behavior.Def.Materials.TryGetValue(args.MaterialId, out var material)
                || !behavior.Scene.TryGetEntity(args.Grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            BlockAndAnchor pt0 = args.Pt0;
            BlockAndAnchor pt1 = args.Pt1;
            BlockAndAnchor pt2 = args.Pt2;
            BlockAndAnchor pt3 = args.Pt3;

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
                    if (!behavior.TryRemovePreReqs(durabilityRequired, material))
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
                    material, new EquiDecorativeMeshComponent.SurfaceArgs<BlockAndAnchor>
                    {
                        A = pt0,
                        B = pt1,
                        C = pt2,
                        D = pt3,
                        UvProjection = args.UvProjection,
                        UvBias = args.UvBias,
                        UvScale = args.UvScale,
                        Shared =
                        {
                            Color = args.Color,
                        },
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
                    UvProjection = DecorativeToolSettings.UvProjection,
                    UvBias = DecorativeToolSettings.UvBias,
                    UvScale = UvScale,
                    Shared =
                    {
                        Color = PackedHsvShift
                    },
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
    public class EquiDecorativeSurfaceToolDefinition
        : EquiDecorativeToolBaseDefinition,
            IEquiDecorativeToolBaseDefinition<EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef>
    {
        private readonly MaterialHolder<SurfaceMaterialDef> _holder = new MaterialHolder<SurfaceMaterialDef>();
        public DictionaryReader<MyStringHash, SurfaceMaterialDef> Materials => _holder.Materials;
        public ListReader<SurfaceMaterialDef> SortedMaterials => _holder.SortedMaterials;
        public ImmutableRange<float> TextureScale { get; private set; }


        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeSurfaceToolDefinition)builder;

            TextureScale = ob.TextureScale?.Immutable() ?? new ImmutableRange<float>(1, 1);
            if (ob.Material != null)
                _holder.Add(new SurfaceMaterialDef(this, ob, new MyObjectBuilder_EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef
                {
                    Id = "",
                    Material = ob.Material,
                    TextureSize = ob.TextureSize,
                    FlipRearNormals = ob.FlipRearNormals,
                    UvGuide = ob.UvGuide,
                }));

            if (ob.Materials != null)
                foreach (var mtl in ob.Materials)
                    _holder.Add(new SurfaceMaterialDef(this, ob, mtl));
        }

        public class SurfaceMaterialDef : MaterialDef<EquiDecorativeSurfaceToolDefinition>
        {
            public MaterialDescriptor Material { get; private set; }
            public Vector2 TextureSize { get; private set; }
            public bool FlipRearNormals { get; }
            public Vector2 UvGuidePerpendicular { get; }
            public readonly float DurabilityPerSquareMeter;

            internal SurfaceMaterialDef(
                EquiDecorativeSurfaceToolDefinition owner,
                MyObjectBuilder_EquiDecorativeSurfaceToolDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef ob) : base(owner, ownerOb, ob, ob.Material?.Icons)
            {
                Material = ob.Material.Build();
                TextureSize = ob.TextureSize ?? ownerOb.TextureSize ?? Vector2.One;
                FlipRearNormals = ob.FlipRearNormals ?? ownerOb.FlipRearNormals ?? true;
                var uvGuideTangent = ob.UvGuide ?? ownerOb.UvGuide ?? Vector2.UnitX;
                UvGuidePerpendicular = new Vector2(-uvGuideTangent.Y, uvGuideTangent.X);
                DurabilityPerSquareMeter = ob.DurabilityPerSquareMeter ?? ownerOb.DurabilityPerSquareMeter ?? 0;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeSurfaceToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        /// <inheritdoc cref="SurfaceMaterialDef.Material"/>
        [XmlElement]
        public MaterialSpec Material;

        /// <inheritdoc cref="SurfaceMaterialDef.TextureSize"/>
        [XmlElement]
        public SerializableVector2? TextureSize;

        /// <inheritdoc cref="SurfaceMaterialDef.TextureScale"/>
        [XmlElement]
        public MutableRange<float>? TextureScale;

        /// <inheritdoc cref="SurfaceMaterialDef.FlipRearNormals"/>
        [XmlElement]
        public bool? FlipRearNormals;

        /// <inheritdoc cref="SurfaceMaterialDef.DurabilityPerSquareMeter"/>
        [XmlElement]
        public float? DurabilityPerSquareMeter;

        /// <inheritdoc cref="SurfaceMaterialDef.UvGuide"/>
        [XmlElement]
        public SerializableVector2? UvGuide;

        [XmlElement("Variant")]
        public SurfaceMaterialDef[] Materials;

        public class SurfaceMaterialDef : MaterialDef
        {
            /// <summary>
            /// PBR material definition.
            /// </summary>
            [XmlElement]
            public MaterialSpec Material;

            /// <summary>
            /// Size of the full texture in meters. Defaults to 1 by 1 meter.
            /// </summary>
            [XmlElement]
            public SerializableVector2? TextureSize;

            /// <summary>
            /// Range of permitted texture scales, defaults to no scaling.
            /// </summary>
            [XmlElement]
            public MutableRange<float>? TextureScale;

            /// <summary>
            /// Should the surface be rendered twice, once with flipped normals. Defaults to true.
            /// </summary>
            [XmlElement]
            public bool? FlipRearNormals;

            /// <summary>
            /// Direction in texture coordinate space that the dominant texture in the material points.
            /// Used to render the red guides when placing surfaces.
            /// </summary>
            [XmlElement]
            public SerializableVector2? UvGuide;

            /// <summary>
            /// Durability cost per square meter of surface.
            /// </summary>
            [XmlElement]
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