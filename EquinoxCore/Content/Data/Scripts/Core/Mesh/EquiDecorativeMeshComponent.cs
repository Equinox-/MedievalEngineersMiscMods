using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entities.Gravity;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Import;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.Serialization;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyComponent(typeof(MyObjectBuilder_EquiDecorativeMeshComponent))]
    [MyDependency(typeof(MyGridDataComponent), Critical = true)]
    [MyDependency(typeof(MyGridConnectivityComponent), Critical = false)]
    [MyDependency(typeof(EquiDynamicMeshComponent), Critical = false)]
    [ReplicatedComponent]
    public partial class EquiDecorativeMeshComponent : MyEntityComponent, IMyEventProxy
    {
        private readonly MyFreeList<RenderData> _features = new MyFreeList<RenderData>();
        private readonly Dictionary<FeatureKey, int> _featuresByKey = new Dictionary<FeatureKey, int>();
        private readonly MyHashSetDictionary<BlockId, FeatureKey> _featureByBlock = new MyHashSetDictionary<BlockId, FeatureKey>();

#pragma warning disable CS0649
        [Automatic]
        private readonly MyGridDataComponent _gridData;

        [Automatic]
        private readonly MyGridConnectivityComponent _gridConnectivity;
#pragma warning restore CS0649
        private EquiDynamicMeshComponent _dynamicMesh;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _dynamicMesh = Container.GetOrAdd<EquiDynamicMeshComponent>();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _gridData.BlockRemoved += OnBlockRemoved;
            _gridData.BeforeMerge += BeforeMerge;
            _gridData.AfterMerge += AfterMerge;
            if (_gridConnectivity != null)
            {
                _gridConnectivity.BeforeGridSplit += BeforeSplit;
                _gridConnectivity.AfterGridSplit += AfterSplit;
            }

            using (PoolManager.Get<List<FeatureKey>>(out var toUpdate))
            {
                toUpdate.AddRange(_featuresByKey.Keys);
                foreach (var triplet in toUpdate)
                    if (CommitFeatureInternal(in triplet))
                        DestroyFeatureInternal(in triplet);
            }
        }

        public override void OnRemovedFromScene()
        {
            _gridData.BlockRemoved -= OnBlockRemoved;
            _gridData.BeforeMerge -= BeforeMerge;
            _gridData.AfterMerge -= AfterMerge;
            if (_gridConnectivity != null)
            {
                _gridConnectivity.BeforeGridSplit -= BeforeSplit;
                _gridConnectivity.AfterGridSplit -= AfterSplit;
            }

            base.OnRemovedFromScene();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            _dynamicMesh = null;
            base.OnBeforeRemovedFromContainer();
        }

        private static void BeforeSplit(MyEntity originalEntity, MyEntity splitEntity, List<MyBlock> blocksMoved)
        {
            var originalMesh = originalEntity.Get<EquiDecorativeMeshComponent>();
            if (originalMesh == null) return;
            using (PoolManager.Get<HashSet<BlockId>>(out var moved))
            using (PoolManager.Get<HashSet<FeatureKey>>(out var moveOrDestroy))
            {
                foreach (var block in blocksMoved)
                    moved.Add(block.Id);

                foreach (var block in blocksMoved)
                    if (originalMesh._featureByBlock.TryGet(block.Id, out var features))
                        foreach (var feature in features)
                            moveOrDestroy.Add(feature);

                var splitMesh = splitEntity.Components.Get<EquiDecorativeMeshComponent>();
                foreach (var triplet in moveOrDestroy)
                {
                    if (!originalMesh._featuresByKey.TryGetValue(triplet, out var index)) continue;
                    if (
                        moved.Contains(triplet.A.Block)
                        && (triplet.B.IsNull || moved.Contains(triplet.B.Block))
                        && (triplet.C.IsNull || moved.Contains(triplet.C.Block))
                        && (triplet.D.IsNull || moved.Contains(triplet.D.Block)))
                    {
                        if (splitMesh == null) splitEntity.Components.Add(splitMesh = new EquiDecorativeMeshComponent());
                        ref var feature = ref originalMesh._features[index];
                        splitMesh.AddFeatureInternal(triplet, feature.Def, feature.Args);
                    }

                    originalMesh.DestroyFeatureInternal(triplet);
                }
            }
        }

        private static void AfterSplit(MyEntity originalEntity, MyEntity splitEntity, List<MyBlock> blocksMoved)
        {
            var splitMesh = splitEntity.Get<EquiDecorativeMeshComponent>();
            if (splitMesh == null) return;
            using (PoolManager.Get<HashSet<FeatureKey>>(out var moved))
            {
                foreach (var block in blocksMoved)
                    if (splitMesh._featureByBlock.TryGet(block.Id, out var features))
                        foreach (var feature in features)
                            moved.Add(feature);
                foreach (var triplet in moved)
                    if (splitMesh.CommitFeatureInternal(triplet))
                        splitMesh.DestroyFeatureInternal(triplet);
            }
        }

        private static void BeforeMerge(MyGridDataComponent finalGridData, MyGridDataComponent otherGridData, List<MyBlock> thisBlocks,
            List<MyBlock> otherBlocks)
        {
            var otherMesh = otherGridData.Container.Get<EquiDecorativeMeshComponent>();
            if (otherMesh == null || otherMesh._features.Count == 0) return;
            // Move all features to new grid
            var finalMesh = finalGridData.Container.GetOrAdd<EquiDecorativeMeshComponent>();
            foreach (var kv in otherMesh._featuresByKey)
            {
                // Destroy feature on old grid.
                ref var otherFeature = ref otherMesh._features[kv.Value];
                otherMesh.DestroyRendererInternal(kv.Key, ref otherFeature);
                // Copy feature data to new grid.
                if (!finalMesh._featuresByKey.TryGetValue(kv.Key, out var finalIndex))
                    finalMesh._featuresByKey.Add(kv.Key, finalIndex = finalMesh._features.Allocate());
                ref var finalFeature = ref finalMesh._features[finalIndex];
                finalFeature = otherFeature;
            }

            // Copy all block -> feature bindings.
            foreach (var kv in otherMesh._featureByBlock)
                finalMesh._featureByBlock.Add(kv.Key, kv.Value);

            otherMesh._features.Clear();
            otherMesh._featuresByKey.Clear();
            otherMesh._featureByBlock.Clear();
        }

        private static void AfterMerge(MyGridDataComponent finalGridData, MyGridDataComponent otherGridData, List<MyBlock> thisBlocks,
            List<MyBlock> otherBlocks)
        {
            var finalMesh = finalGridData.Container.Get<EquiDecorativeMeshComponent>();
            if (finalMesh == null) return;
            using (PoolManager.Get<HashSet<FeatureKey>>(out var moved))
            {
                foreach (var block in otherBlocks)
                    if (finalMesh._featureByBlock.TryGet(block.Id, out var features))
                        foreach (var feature in features)
                            moved.Add(feature);
                foreach (var triplet in moved)
                    if (finalMesh.CommitFeatureInternal(triplet))
                        finalMesh.DestroyFeatureInternal(triplet);
            }
        }

        private void OnBlockRemoved(MyBlock block, MyGridDataComponent grid)
        {
            if (!_featureByBlock.TryGet(block.Id, out var features)) return;
            using (PoolManager.Get<List<FeatureKey>>(out var temp))
            {
                temp.AddRange(features);
                foreach (var tmp in temp)
                    DestroyFeatureInternal(in tmp);
            }
        }

        private struct RenderData
        {
            public MyDefinitionBase Def;
            public FeatureArgs Args;
            public ulong? RenderId;
        }

        private void AddFeatureInternal(in FeatureKey triplet, MyDefinitionBase def, in FeatureArgs args)
        {
            if (!_featuresByKey.TryGetValue(triplet, out var index))
            {
                index = _features.Allocate();
                _featuresByKey.Add(triplet, index);
                _featureByBlock.Add(triplet.A.Block, triplet);
                if (!triplet.B.IsNull)
                    _featureByBlock.Add(triplet.B.Block, triplet);
                if (!triplet.C.IsNull)
                    _featureByBlock.Add(triplet.C.Block, triplet);
                if (!triplet.D.IsNull)
                    _featureByBlock.Add(triplet.D.Block, triplet);
            }

            ref var feature = ref _features[index];
            feature.Def = def;
            feature.Args = args;
        }

        private bool DestroyFeatureInternal(in FeatureKey triplet)
        {
            if (!_featuresByKey.TryGetValue(triplet, out var index)) return false;
            DestroyRendererInternal(in triplet, ref _features[index]);

            _features.Free(index);
            _featuresByKey.Remove(triplet);
            _featureByBlock.Remove(triplet.A.Block, triplet);
            if (!triplet.B.IsNull)
                _featureByBlock.Remove(triplet.B.Block, triplet);
            if (!triplet.C.IsNull)
                _featureByBlock.Remove(triplet.C.Block, triplet);
            if (!triplet.D.IsNull)
                _featureByBlock.Remove(triplet.D.Block, triplet);
            return true;
        }

        /// <summary>
        /// Destroys the renderer associated with a feature without removing the feature from the state.
        /// After calling this the feature MUST be removed from the _features and _featuresByBlock states.
        /// </summary>
        private void DestroyRendererInternal(in FeatureKey triplet, ref RenderData data)
        {
            switch (triplet.Type)
            {
                case FeatureType.Decal:
                case FeatureType.Line:
                case FeatureType.Surface:
                    if (data.RenderId != null)
                    {
                        _dynamicMesh.DestroyObject(data.RenderId.Value);
                        data.RenderId = null;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static bool TrySnapPosition(MyGridDataComponent grid,
            Vector3 gridLocalPosition,
            float snapDistance,
            out BlockAndAnchor snappedAnchor)
        {
            snappedAnchor = default;
            if (!grid.Container.TryGet(out EquiDecorativeMeshComponent decor)) return false;
            BlockAndAnchor bestAnchor = default;
            var bestDistSq = snapDistance * snapDistance;
            using (PoolManager.Get<List<MyBlock>>(out var blocks))
            {
                grid.GetBlocksInSphere(new BoundingSphereD(gridLocalPosition, snapDistance), blocks);
                foreach (var block in blocks)
                {
                    var id = block.Id;
                    if (!decor._featureByBlock.TryGet(id, out var featureKeys)) continue;

                    void MaybeUpdate(in BlockAndAnchor anchor)
                    {
                        if (anchor.Block != id || !anchor.TryGetGridLocalAnchor(grid, out var pivot))
                            return;
                        var distSq = Vector3.DistanceSquared(pivot, gridLocalPosition);
                        if (distSq >= bestDistSq) return;
                        bestAnchor = anchor;
                        bestDistSq = distSq;
                    }

                    foreach (var featureKey in featureKeys)
                    {
                        MaybeUpdate(in featureKey.A);
                        MaybeUpdate(in featureKey.B);
                        MaybeUpdate(in featureKey.C);
                        MaybeUpdate(in featureKey.D);
                    }
                }
            }

            snappedAnchor = bestAnchor;
            return !bestAnchor.IsNull;
        }

        public static BlockAndAnchor CreateAnchorFromBlockLocalPosition(MyGridDataComponent grid, MyBlock block, Vector3 blockLocalPosition)
        {
            var size = block.Definition.Size * grid.Size;
            var norm = blockLocalPosition / size;
            return new BlockAndAnchor(block.Id, NewAnchorPacking.Pack(norm) | NewAnchorPackingMask);
        }

        public struct DecalArgs<TPos> where TPos : struct
        {
            public TPos Position;
            public Vector3 Normal;
            public Vector3 Up;
            public float Height;
            public PackedHsvShift Color;
        }

        public static EquiMeshHelpers.DecalData CreateDecalData(
            EquiDecorativeDecalToolDefinition.DecalDef decal,
            in DecalArgs<Vector3> args)
        {
            var left = Vector3.Cross(args.Normal, args.Up);
            left *= args.Height * decal.AspectRatio / left.Length() / 2;
            return new EquiMeshHelpers.DecalData
            {
                Material = decal.Material,
                Position = args.Position + args.Normal * 0.005f,
                TopLeftUv = decal.TopLeftUv,
                BottomRightUv = decal.BottomRightUv,
                Normal = VF_Packer.PackNormal(args.Normal),
                Up = new HalfVector3(args.Up * args.Height / 2),
                Left = new HalfVector3(left),
                ColorMask = args.Color
            };
        }

        public struct LineArgs<TPos> where TPos : struct
        {
            public TPos A, B;
            public float CatenaryFactor;
            public PackedHsvShift Color;

            public float? WidthA;
            public float? WidthB;
        }

        public static EquiMeshHelpers.LineData CreateLineData(EquiDecorativeLineToolDefinition.LineMaterialDef material, in LineArgs<Vector3> args)
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
                ColorMask = args.Color
            };
        }

        public struct SurfaceArgs<TPos> where TPos : struct
        {
            public TPos A, B, C;
            public TPos? D;

            public PackedHsvShift Color;
            public UvProjectionMode? UvProjection;
            public UvBiasMode? UvBias;
            public float? UvScale;
        }

        private const UvProjectionMode DefaultUvProjection = UvProjectionMode.Bevel;
        private const UvBiasMode DefaultUvBias = UvBiasMode.XAxis;
        private const float DefaultUvScale = 1;

        public static EquiMeshHelpers.SurfaceData CreateSurfaceData(
            EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef materialDef,
            in SurfaceArgs<Vector3> args,
            Vector3 alignNormal)
        {
            Vector3 norm;
            const float eps = 1e-6f;
            var a = args.A;
            var b = args.B;
            var c = args.C;
            var d = args.D;
            if (d.HasValue
                && !a.Equals(b, eps) && !a.Equals(c, eps) && !a.Equals(d.Value, eps)
                && !b.Equals(c, eps) && !b.Equals(d.Value, eps)
                && !c.Equals(d.Value, eps))
            {
                var tmpD = d.Value;
                EquiMeshHelpers.SortSurfacePositions(alignNormal, ref a, ref b, ref c, ref tmpD);
                d = tmpD;
                var norm1 = Vector3.Cross(b - a, c - a);
                norm1.Normalize();
                var norm2 = Vector3.Cross(c - a, tmpD - a);
                norm2.Normalize();
                norm = norm1 + norm2;
                norm.Normalize();
            }
            else
            {
                EquiMeshHelpers.SortSurfacePositions(alignNormal, ref a, ref b, ref c);
                norm = Vector3.Cross(b - a, c - a);
                norm.Normalize();
            }

            FindUvProjection(args.UvProjection ?? DefaultUvProjection, args.UvBias ?? DefaultUvBias, norm, out var uvX, out var uvY);

            var trueUvScale = 1 / (args.UvScale ?? DefaultUvScale);

            var tangent = IdealTriangleTangent(a, b, c);
            if (d.HasValue)
                tangent += IdealTriangleTangent(a, c, d.Value);

            if (tangent.Normalize() < 1e-6f)
                tangent = Vector3.CalculatePerpendicularVector(norm);

            var snappedTangent = Vector3.Cross(Vector3.Cross(norm, tangent), norm);
            if (snappedTangent.Normalize() <= 1e-6)
                norm.CalculatePerpendicularVector(out snappedTangent);

            return new EquiMeshHelpers.SurfaceData
            {
                Material = materialDef.Material.MaterialName,
                Pt0 = CreateVertex(a),
                Pt1 = CreateVertex(b),
                Pt2 = CreateVertex(c),
                Pt3 = d.HasValue ? (EquiMeshHelpers.VertexData?)CreateVertex(d.Value) : null,
                FlipRearNormals = materialDef.FlipRearNormals,
                ColorMask = args.Color
            };

            Vector2 ComputeUv(in Vector3 pos) => new Vector2(trueUvScale * uvX.Dot(pos), trueUvScale * uvY.Dot(pos)) / materialDef.TextureSize;

            Vector3 IdealTriangleTangent(Vector3 pt0, Vector3 pt1, Vector3 pt2)
            {
                var uv0 = ComputeUv(pt0);
                var duv1 = ComputeUv(pt1) - uv0;
                var duv2 = ComputeUv(pt2) - uv0;
                return EquiMeshHelpers.ComputeTriangleTangent(pt1 - pt0, duv1, pt2 - pt0, duv2);
            }

            EquiMeshHelpers.VertexData CreateVertex(Vector3 pos) => new EquiMeshHelpers.VertexData
            {
                Position = pos,
                Uv = new HalfVector2(ComputeUv(in pos)),
                Normal = VF_Packer.PackNormal(norm),
                Tangent = VF_Packer.PackNormal(snappedTangent),
            };
        }

        private static void FindUvProjection(UvProjectionMode projection, UvBiasMode bias, Vector3 normal, out Vector3 uvX, out Vector3 uvY)
        {
            Vector3 biasVector;
            switch (bias)
            {
                case UvBiasMode.XAxis:
                    biasVector = Vector3.UnitX;
                    break;
                case UvBiasMode.YAxis:
                    biasVector = Vector3.UnitY;
                    break;
                case UvBiasMode.ZAxis:
                    biasVector = Vector3.UnitZ;
                    break;
                case UvBiasMode.Count:
                default:
                    throw new ArgumentOutOfRangeException(nameof(bias), bias, null);
            }

            var uvPlaneNormal = FindUvNormal(projection, normal, biasVector);
            Vector3.Cross(ref biasVector, ref uvPlaneNormal, out uvY);
            var yLength = uvY.LengthSquared();
            if (yLength <= 1e-6f)
                uvPlaneNormal.CalculatePerpendicularVector(out uvY);
            else
                uvY /= (float)Math.Sqrt(yLength);

            Vector3.Cross(ref uvPlaneNormal, ref uvY, out uvX);
        }

        private static readonly Vector3[] FirstQuadrantUvProjections =
        {
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, 0, 1),
            new Vector3(0.7071f, 0.7071f, 0),
            new Vector3(0, 0.7071f, 0.7071f),
            new Vector3(0.7071f, 0, 0.7071f),
            new Vector3(0.57735f, 0.57735f, 0.57735f),
        };

        private static Vector3 FindUvNormal(UvProjectionMode mode, Vector3 normal, Vector3 except)
        {
            switch (mode)
            {
                case UvProjectionMode.Cube:
                    return FindCubeUvNormal(normal, except);
                case UvProjectionMode.Bevel:
                    return FindBeveledUvNormal(normal, except);
                case UvProjectionMode.Count:
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static Vector3 FindCubeUvNormal(Vector3 normal, Vector3 except)
        {
            var rejected = Vector3.Reject(normal, except);
            if (rejected.LengthSquared() < 1e-6)
                rejected = normal;
            var best = Vector3.DominantAxisProjection(rejected);
            best.Normalize();
            return best;
        }

        private static Vector3 FindBeveledUvNormal(Vector3 normal, Vector3 except)
        {
            var exceptAbs = Vector3.Abs(except);
            exceptAbs.Normalize();

            var abs = Vector3.Abs(normal);
            // First octant, 8 possibilities.
            var bestI = 0;
            var bestDot = float.NegativeInfinity;
            for (var i = 0; i < FirstQuadrantUvProjections.Length; i++)
            {
                ref var candidate = ref FirstQuadrantUvProjections[i];
                if (candidate.Dot(ref exceptAbs) > 0.99999f) continue;
                Vector3.Dot(ref abs, ref candidate, out var dot);
                if (dot <= bestDot) continue;
                bestDot = dot;
                bestI = i;
            }

            // Move back to original octant.
            return MiscMath.SafeSign(normal) * FirstQuadrantUvProjections[bestI];
        }

        /// <summary>
        /// Commits a feature to the runtime mesh.  Returns true if the feature should be removed.
        /// </summary>
        private bool CommitFeatureInternal(in FeatureKey triplet)
        {
            if (!_featuresByKey.TryGetValue(triplet, out var index)) return false;
            ref var feature = ref _features[index];
            DestroyRendererInternal(in triplet, ref feature);
            if (!triplet.A.TryGetGridLocalAnchor(_gridData, out var blockA)) return true;

            ref var args = ref feature.Args;
            switch (triplet.Type)
            {
                case FeatureType.Decal:
                {
                    if (!(feature.Def is EquiDecorativeDecalToolDefinition decalsDef)) return true;
                    if (!decalsDef.Decals.TryGetValue(args.MaterialId, out var decalDef)) return true;
                    feature.RenderId = _dynamicMesh.CreateDecal(CreateDecalData(decalDef, new DecalArgs<Vector3>
                    {
                        Position = blockA,
                        Normal = VF_Packer.UnpackNormal(args.DecalNormal),
                        Up = VF_Packer.UnpackNormal(args.DecalUp),
                        Height = args.DecalHeight,
                        Color = args.Color
                    }));
                    break;
                }
                case FeatureType.Line:
                {
                    if (!(feature.Def is EquiDecorativeLineToolDefinition lineDef)) return true;
                    if (!lineDef.Materials.TryGetValue(args.MaterialId, out var materialDef)) return true;
                    if (!triplet.B.TryGetGridLocalAnchor(_gridData, out var blockB)) return true;
                    feature.RenderId = _dynamicMesh.CreateLine(CreateLineData(materialDef, new LineArgs<Vector3>
                    {
                        A = blockA,
                        B = blockB,
                        WidthA = args.WidthA,
                        WidthB = args.WidthB,
                        CatenaryFactor = args.CatenaryFactor,
                        Color = args.Color
                    }));
                    break;
                }
                case FeatureType.Surface:
                {
                    if (!(feature.Def is EquiDecorativeSurfaceToolDefinition surfDef)) return true;
                    if (!surfDef.Materials.TryGetValue(args.MaterialId, out var materialDef)) return true;
                    if (!triplet.B.TryGetGridLocalAnchor(_gridData, out var blockB)) return true;
                    if (!triplet.C.TryGetGridLocalAnchor(_gridData, out var blockC)) return true;
                    Vector3? blockD;
                    if (triplet.D.IsNull)
                        blockD = null;
                    else
                    {
                        if (!triplet.D.TryGetGridLocalAnchor(_gridData, out var blockDVal)) return true;
                        blockD = blockDVal;
                    }

                    var gravityWorld = MyGravityProviderSystem.CalculateNaturalGravityInPoint(Entity.GetPosition());
                    var localGravity = Vector3.TransformNormal(gravityWorld, Entity.PositionComp.WorldMatrixNormalizedInv);
                    feature.RenderId = _dynamicMesh.CreateSurface(CreateSurfaceData(materialDef, new SurfaceArgs<Vector3>
                    {
                        A = blockA,
                        B = blockB,
                        C = blockC,
                        D = blockD,
                        Color = args.Color,
                        UvProjection = args.UvProjection,
                        UvBias = args.UvBias,
                        UvScale = args.UvScale,
                    }, -localGravity));
                    break;
                }
                default:
                    return false;
            }

            return false;
        }

        [RpcSerializable]
        private struct FeatureArgs
        {
            [Serialize]
            private float _float0;

            [Serialize]
            private uint _uint0;

            [Serialize]
            private uint _uint1;

            [Serialize]
            public MyStringHash MaterialId;

            [Serialize]
            public PackedHsvShift Color;

            #region Surfaces

            [NoSerialize]
            public UvProjectionMode UvProjection
            {
                get => (UvProjectionMode)_uint0;
                set => _uint0 = (uint)value;
            }

            [NoSerialize]
            public UvBiasMode UvBias
            {
                get => (UvBiasMode)_uint1;
                set => _uint1 = (uint)value;
            }

            [NoSerialize]
            public float UvScale
            {
                get => _float0;
                set => _float0 = value;
            }

            #endregion

            #region Lines

            [NoSerialize]
            public float CatenaryFactor
            {
                get => _float0;
                set => _float0 = value;
            }

            [NoSerialize]
            public float WidthA
            {
                get => HalfUtils.Unpack((ushort)_uint0);
                set => _uint0 = HalfUtils.Pack(value);
            }

            [NoSerialize]
            public float WidthB
            {
                get => HalfUtils.Unpack((ushort)_uint1);
                set => _uint1 = HalfUtils.Pack(value);
            }

            #endregion

            #region Decals

            [NoSerialize]
            public float DecalHeight
            {
                get => _float0;
                set => _float0 = value;
            }

            [NoSerialize]
            public uint DecalNormal
            {
                get => _uint0;
                set => _uint0 = value;
            }

            [NoSerialize]
            public uint DecalUp
            {
                get => _uint1;
                set => _uint1 = value;
            }

            #endregion
        }

        private bool TryAddFeatureInternal(in FeatureKey key, EquiDecorativeToolBaseDefinition def, in FeatureArgs args)
        {
            AddFeatureInternal(key, def, in args);
            if (!CommitFeatureInternal(key))
                return true;
            DestroyFeatureInternal(key);
            return false;
        }

        [Event, Reliable, Broadcast]
        private void AddFeature_Sync(RpcFeatureKey key, SerializableDefinitionId id, FeatureArgs args)
        {
            var def = MyDefinitionManager.Get<EquiDecorativeToolBaseDefinition>(id);
            if (def != null)
                TryAddFeatureInternal(key, def, args);
        }

        [Event, Reliable, Broadcast]
        private void RemoveFeature_Sync(RpcFeatureKey key) => DestroyFeatureInternal(key);

        private void RaiseAddFeature_Sync(FeatureKey key, MyDefinitionId id, FeatureArgs args)
        {
            var mp = MyAPIGateway.Multiplayer;
            mp?.RaiseEvent(this, ctx => ctx.AddFeature_Sync,
                (RpcFeatureKey)key, (SerializableDefinitionId)id, args);
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
                Color = args.Color
            };
            if (TryAddFeatureInternal(in key, def.Owner, rpcArgs))
                RaiseAddFeature_Sync(key, def.Owner.Id, rpcArgs);
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
                Color = args.Color,
                WidthA = args.WidthA >= 0 ? args.WidthA.Value : -1,
                WidthB = args.WidthB >= 0 ? args.WidthB.Value : -1,
            };
            if (TryAddFeatureInternal(in key, def.Owner, rpcArgs))
                RaiseAddFeature_Sync(key, def.Owner.Id, rpcArgs);
        }

        public void AddSurface(EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef def, SurfaceArgs<BlockAndAnchor> args)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Surface, args.A, args.B, args.C, args.D ?? BlockAndAnchor.Null);
            var rpcArgs = new FeatureArgs
            {
                MaterialId = def.Id,
                Color = args.Color,
                UvProjection = args.UvProjection ?? DefaultUvProjection,
                UvBias = args.UvBias ?? DefaultUvBias,
                UvScale = args.UvScale ?? DefaultUvScale
            };
            if (TryAddFeatureInternal(in key, def.Owner, rpcArgs))
                RaiseAddFeature_Sync(key, def.Owner.Id, rpcArgs);
        }

        public void RemoveDecal(BlockAndAnchor a)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Decal, a, BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }

        public void RemoveLine(BlockAndAnchor a, BlockAndAnchor b)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Line, a, b, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }

        public void RemoveSurface(BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(FeatureType.Surface, a, b, c, d);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }

        public override bool IsSerialized => _features.Count > 0;

        private readonly struct SurfaceGroupKey : IEquatable<SurfaceGroupKey>
        {
            public readonly MyDefinitionId Definition;
            public readonly MyStringHash MaterialId;
            public readonly UvProjectionMode UvProjection;
            public readonly UvBiasMode UvBias;

            public SurfaceGroupKey(MyDefinitionId definition, MyStringHash materialId, UvProjectionMode uvProjection, UvBiasMode uvBias)
            {
                Definition = definition;
                MaterialId = materialId;
                UvProjection = uvProjection;
                UvBias = uvBias;
            }

            public bool Equals(SurfaceGroupKey other)
            {
                return Definition.Equals(other.Definition) && MaterialId.Equals(other.MaterialId) && UvProjection == other.UvProjection &&
                       UvBias == other.UvBias;
            }

            public override bool Equals(object obj) => obj is SurfaceGroupKey other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = Definition.GetHashCode();
                hashCode = (hashCode * 397) ^ MaterialId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)UvProjection;
                hashCode = (hashCode * 397) ^ (int)UvBias;
                return hashCode;
            }
        }

        private readonly struct MaterialGroupKey : IEquatable<MaterialGroupKey>
        {
            public readonly MyDefinitionId Definition;
            public readonly MyStringHash MaterialId;

            public MaterialGroupKey(MyDefinitionId definition, MyStringHash materialId)
            {
                Definition = definition;
                MaterialId = materialId;
            }

            public bool Equals(MaterialGroupKey other) => Definition.Equals(other.Definition) && MaterialId.Equals(other.MaterialId);

            public override bool Equals(object obj) => obj is MaterialGroupKey other && Equals(other);

            public override int GetHashCode() => (Definition.GetHashCode() * 397) ^ MaterialId.GetHashCode();
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiDecorativeMeshComponent)base.Serialize(copy);
            // Purposefully NOT using ListDictionary since the lists in that are pooled and we have to transfer ownership of these lists out
            // of this method to the returned object builder.
            using (PoolManager.Get<Dictionary<MaterialGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.DecalBuilder>>>(out var decals))
            using (PoolManager.Get<Dictionary<MaterialGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder>>>(out var lines))
            using (PoolManager.Get<Dictionary<SurfaceGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.SurfaceBuilder>>>(out var surfaces))
            {
                foreach (var kv in _featuresByKey)
                {
                    var triplet = kv.Key;
                    ref var feature = ref _features[kv.Value];
                    var def = feature.Def.Id;
                    ref var args = ref feature.Args;
                    switch (triplet.Type)
                    {
                        case FeatureType.Decal:
                            AddToCollection(decals, new MaterialGroupKey(def, args.MaterialId), new MyObjectBuilder_EquiDecorativeMeshComponent.DecalBuilder
                            {
                                A = triplet.A.Block.Value,
                                AOffset = triplet.A.PackedAnchor,
                                Normal = args.DecalNormal,
                                Up = args.DecalUp,
                                Height = args.DecalHeight,
                                ColorRaw = args.Color,
                            });
                            break;
                        case FeatureType.Line:
                            AddToCollection(lines, new MaterialGroupKey(def, args.MaterialId), new MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder
                            {
                                A = triplet.A.Block.Value,
                                AOffset = triplet.A.PackedAnchor,
                                B = triplet.B.Block.Value,
                                BOffset = triplet.B.PackedAnchor,
                                CatenaryFactor = args.CatenaryFactor,
                                ColorRaw = args.Color,
                                WidthA = args.WidthA,
                                WidthB = Math.Abs(args.WidthA - args.WidthB) < 1e-6f ? -1 : args.WidthB,
                            });
                            break;
                        case FeatureType.Surface:
                            AddToCollection(surfaces, new SurfaceGroupKey(def, args.MaterialId, args.UvProjection, args.UvBias),
                                new MyObjectBuilder_EquiDecorativeMeshComponent.SurfaceBuilder
                                {
                                    A = triplet.A.Block.Value,
                                    AOffset = triplet.A.PackedAnchor,
                                    B = triplet.B.Block.Value,
                                    BOffset = triplet.B.PackedAnchor,
                                    C = triplet.C.Block.Value,
                                    COffset = triplet.C.PackedAnchor,
                                    D = triplet.D.Block.Value,
                                    DOffset = triplet.D.PackedAnchor,
                                    ColorRaw = args.Color,
                                    UvScale = args.UvScale,
                                });
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                ob.Decals = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeDecals>(decals.Count);
                foreach (var entry in decals)
                    ob.Decals.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeDecals
                    {
                        Definition = entry.Key.Definition,
                        DecalId = entry.Key.MaterialId.String,
                        Decals = entry.Value,
                    });
                ob.Lines = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeLines>(lines.Count);
                foreach (var entry in lines)
                    ob.Lines.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeLines
                    {
                        Definition = entry.Key.Definition,
                        MaterialId = entry.Key.MaterialId.String,
                        Lines = entry.Value,
                    });
                ob.Surfaces = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeSurfaces>(surfaces.Count);
                foreach (var entry in surfaces)
                    ob.Surfaces.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeSurfaces
                    {
                        Definition = entry.Key.Definition,
                        MaterialId = entry.Key.MaterialId.String,
                        UvProjection = entry.Key.UvProjection,
                        UvBias = entry.Key.UvBias,
                        Surfaces = entry.Value,
                    });
                decals.Clear();
                lines.Clear();
                surfaces.Clear();
            }

            return ob;
        }

        private static void AddToCollection<TK, TV>(Dictionary<TK, List<TV>> dict, in TK key, TV value)
        {
            if (!dict.TryGetValue(key, out var val))
                dict.Add(key, val = new List<TV>());
            val.Add(value);
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = builder as MyObjectBuilder_EquiDecorativeMeshComponent;
            if (ob == null) return;
            if (ob.Decals != null)
                foreach (var decals in ob.Decals)
                {
                    if (decals.Decals == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeDecalToolDefinition>(decals.Definition);
                    if (def == null) continue;
                    var decalId = MyStringHash.GetOrCompute(decals.DecalId);
                    if (!def.Decals.ContainsKey(decalId)) continue;
                    foreach (var item in decals.Decals)
                        if (item.A != 0)
                            AddFeatureInternal(new FeatureKey(
                                FeatureType.Decal,
                                new BlockAndAnchor(item.A, item.AOffset),
                                BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null), def, new FeatureArgs
                            {
                                MaterialId = decalId,
                                DecalNormal = item.Normal,
                                DecalUp = item.Up,
                                DecalHeight = item.Height,
                                Color = item.ColorRaw,
                            });
                }

            if (ob.Lines != null)
                foreach (var line in ob.Lines)
                {
                    if (line.Lines == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeLineToolDefinition>(line.Definition);
                    if (def == null) continue;
                    var materialId = MyStringHash.GetOrCompute(line.MaterialId);
                    if (!def.Materials.ContainsKey(materialId)) continue;
                    foreach (var item in line.Lines)
                        if (item.A != 0 && item.B != 0)
                        {
                            var a = new BlockAndAnchor(item.A, item.AOffset);
                            var b = new BlockAndAnchor(item.B, item.BOffset);
                            var key = new FeatureKey(FeatureType.Line, a, b, BlockAndAnchor.Null, BlockAndAnchor.Null);
                            var args = new FeatureArgs
                            {
                                MaterialId = materialId,
                                CatenaryFactor = item.CatenaryFactor,
                                Color = item.ColorRaw,
                                WidthA = item.WidthA,
                                WidthB = item.WidthB,
                            };
                            // Sorting re-ordered the keys, so reorder the widths too.
                            if (!a.Equals(key.A) && args.WidthB >= 0)
                                (args.WidthA, args.WidthB) = (args.WidthB, args.WidthA);
                            AddFeatureInternal(key, def, args);
                        }
                }

            if (ob.Surfaces != null)
                foreach (var triangles in ob.Surfaces)
                {
                    if (triangles.Surfaces == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeSurfaceToolDefinition>(triangles.Definition);
                    if (def == null) continue;
                    var materialId = MyStringHash.GetOrCompute(triangles.MaterialId);
                    if (!def.Materials.ContainsKey(materialId)) continue;
                    var uvProjection = triangles.UvProjection ?? UvProjectionMode.Bevel;
                    var uvBias = triangles.UvBias ?? UvBiasMode.XAxis;
                    foreach (var item in triangles.Surfaces)
                        if (item.A != 0 && item.B != 0 && item.C != 0)
                            AddFeatureInternal(new FeatureKey(
                                    FeatureType.Surface,
                                    new BlockAndAnchor(item.A, item.AOffset),
                                    new BlockAndAnchor(item.B, item.BOffset),
                                    new BlockAndAnchor(item.C, item.COffset),
                                    item.ShouldSerializeD() ? new BlockAndAnchor(item.D, item.DOffset) : BlockAndAnchor.Null), def,
                                new FeatureArgs
                                {
                                    MaterialId = materialId,
                                    Color = item.ColorRaw,
                                    UvProjection = uvProjection,
                                    UvBias = uvBias,
                                    UvScale = item.UvScale,
                                });
                }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeMeshComponent : MyObjectBuilder_EntityComponent, IMyRemappable
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

            [XmlIgnore]
            public PackedHsvShift ColorRaw;

            [XmlAttribute("Color")]
            [NoSerialize]
            public string Color
            {
                get => ModifierDataColor.Serialize(ColorRaw);
                set => ColorRaw = ModifierDataColor.Deserialize(value).Color;
            }

            public bool ShouldSerializeColor() => !ColorRaw.Equals(default);

            public bool ShouldSerializeWidthA() => WidthA >= 0;
            public bool ShouldSerializeWidthB() => WidthB >= 0;
        }

        public class DecorativeLines : IMyRemappable
        {
            [XmlElement("Definition")]
            public SerializableDefinitionId Definition;

            [XmlElement("MaterialId")]
            [Nullable]
            public string MaterialId;

            [XmlElement("Line")]
            public List<LineBuilder> Lines;

            public void Remap(IMySceneRemapper remapper)
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

        [XmlElement("Lines")]
        public List<DecorativeLines> Lines;

        public struct SurfaceBuilder
        {
            [XmlAttribute("A")]
            public ulong A;

            [XmlAttribute("AO")]
            public uint AOffset;

            [XmlAttribute("B")]
            public ulong B;

            [XmlAttribute("BO")]
            public uint BOffset;

            [XmlAttribute("C")]
            public ulong C;

            [XmlAttribute("CO")]
            public uint COffset;

            [XmlAttribute("D")]
            public ulong D;

            [XmlAttribute("DO")]
            public uint DOffset;

            public bool ShouldSerializeD() => D != 0;
            public bool ShouldSerializeDOffset() => ShouldSerializeD();

            [XmlIgnore]
            public PackedHsvShift ColorRaw;

            [XmlAttribute("Color")]
            [NoSerialize]
            public string Color
            {
                get => ModifierDataColor.Serialize(ColorRaw);
                set => ColorRaw = ModifierDataColor.Deserialize(value).Color;
            }

            public bool ShouldSerializeColor() => !ColorRaw.Equals(default);

            [XmlIgnore]
            [Nullable]
            public float? UvScaleRaw;

            [XmlAttribute("S")]
            [NoSerialize]
            public float UvScale
            {
                get => UvScaleRaw ?? 1;
                set
                {
                    if (Math.Abs(value) < 1e-6 || Math.Abs(value - 1) < 1e-6)
                        UvScaleRaw = null;
                    else
                        UvScaleRaw = value;
                }
            }

            public bool ShouldSerializeUvScale() => UvScaleRaw.HasValue;
        }

        public class DecorativeSurfaces : IMyRemappable
        {
            [XmlElement("Definition")]
            public SerializableDefinitionId Definition;

            [XmlElement("MaterialId")]
            [Nullable]
            public string MaterialId;

            [XmlElement("UvProjection")]
            [Nullable]
            public UvProjectionMode? UvProjection;

            [XmlElement("UvBias")]
            [Nullable]
            public UvBiasMode? UvBias;

            [XmlElement("Surf")]
            public List<SurfaceBuilder> Surfaces;

            public void Remap(IMySceneRemapper remapper)
            {
                if (Surfaces == null) return;
                for (var i = 0; i < Surfaces.Count; i++)
                {
                    var surf = Surfaces[i];
                    remapper.RemapObject(MyBlock.SceneType, ref surf.A);
                    remapper.RemapObject(MyBlock.SceneType, ref surf.B);
                    remapper.RemapObject(MyBlock.SceneType, ref surf.C);
                    if (surf.ShouldSerializeD())
                        remapper.RemapObject(MyBlock.SceneType, ref surf.D);
                    Surfaces[i] = surf;
                }
            }
        }

        [XmlElement("Surfaces")]
        public List<DecorativeSurfaces> Surfaces;

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

            [XmlIgnore]
            public PackedHsvShift ColorRaw;

            [XmlAttribute("Color")]
            [NoSerialize]
            public string Color
            {
                get => ModifierDataColor.Serialize(ColorRaw);
                set => ColorRaw = ModifierDataColor.Deserialize(value).Color;
            }

            public bool ShouldSerializeColor() => !ColorRaw.Equals(default);
        }

        public class DecorativeDecals : IMyRemappable
        {
            [XmlElement("Definition")]
            public SerializableDefinitionId Definition;

            [XmlElement("DecalId")]
            [Nullable]
            public string DecalId;

            [XmlElement("Decal")]
            public List<DecalBuilder> Decals;

            public void Remap(IMySceneRemapper remapper)
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

        [XmlElement("Decals")]
        public List<DecorativeDecals> Decals;

        public void Remap(IMySceneRemapper remapper)
        {
            if (Lines != null)
                foreach (var line in Lines)
                    line.Remap(remapper);
            if (Surfaces != null)
                foreach (var surf in Surfaces)
                    surf.Remap(remapper);
            if (Decals != null)
                foreach (var decal in Decals)
                    decal.Remap(remapper);
        }
    }
}