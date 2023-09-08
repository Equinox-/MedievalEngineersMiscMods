using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
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
        private readonly Dictionary<FeatureKey, RenderData> _features = new Dictionary<FeatureKey, RenderData>();
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
                toUpdate.AddRange(_features.Keys);
                foreach (var triplet in toUpdate)
                    if (CommitFeatureInternal(triplet))
                        DestroyFeatureInternal(triplet);
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
                    if (!originalMesh._features.TryGetValue(triplet, out var feature)) continue;
                    if (moved.Contains(triplet.A.Block) && moved.Contains(triplet.B.Block) && (triplet.IsLine || moved.Contains(triplet.C.Block)))
                    {
                        if (splitMesh == null) splitEntity.Components.Add(splitMesh = new EquiDecorativeMeshComponent());
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
            // Move all connections to new grid
            var finalMesh = finalGridData.Container.GetOrAdd<EquiDecorativeMeshComponent>();
            foreach (var kv in otherMesh._features)
                finalMesh._features[kv.Key] = new RenderData(kv.Value.Def, kv.Value.Args, EquiDynamicMeshComponent.NullId);
            foreach (var kv in otherMesh._featureByBlock)
                finalMesh._featureByBlock.Add(kv.Key, kv.Value);

            // Destroy all connections on the old grid
            foreach (var kv in otherMesh._features)
                if (kv.Value.RenderId != EquiDynamicMeshComponent.NullId)
                    otherMesh._dynamicMesh.DestroyObject(kv.Value.RenderId);
            otherMesh._features.Clear();
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

        private readonly struct RenderData
        {
            public readonly MyDefinitionBase Def;
            public readonly ulong RenderId;
            public readonly FeatureArgs Args;

            public RenderData(MyDefinitionBase def, FeatureArgs args, ulong renderId)
            {
                Def = def;
                Args = args;
                RenderId = renderId;
            }
        }

        private void AddFeatureInternal(in FeatureKey triplet, MyDefinitionBase def, in FeatureArgs args)
        {
            if (_features.TryGetValue(triplet, out var data))
                data = new RenderData(def, args, data.RenderId);
            else
            {
                data = new RenderData(def, args, EquiDynamicMeshComponent.NullId);
                _featureByBlock.Add(triplet.A.Block, triplet);
                if (!triplet.B.IsNull)
                    _featureByBlock.Add(triplet.B.Block, triplet);
                if (!triplet.C.IsNull)
                    _featureByBlock.Add(triplet.C.Block, triplet);
                if (!triplet.D.IsNull)
                    _featureByBlock.Add(triplet.D.Block, triplet);
            }

            _features[triplet] = data;
        }

        private bool DestroyFeatureInternal(in FeatureKey triplet)
        {
            if (!_features.TryGetValue(triplet, out var data)) return false;
            if (data.RenderId != EquiDynamicMeshComponent.NullId) _dynamicMesh.DestroyObject(data.RenderId);
            _features.Remove(triplet);
            _featureByBlock.Remove(triplet.A.Block, triplet);
            if (!triplet.B.IsNull)
                _featureByBlock.Remove(triplet.B.Block, triplet);
            if (!triplet.C.IsNull)
                _featureByBlock.Remove(triplet.C.Block, triplet);
            if (!triplet.D.IsNull)
                _featureByBlock.Remove(triplet.D.Block, triplet);
            return true;
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
            return new BlockAndAnchor(block.Id, AnchorPacking.Pack(norm));
        }

        public static EquiMeshHelpers.DecalData CreateDecalData(
            EquiDecorativeDecalToolDefinition.DecalDef def,
            Vector3 pos,
            Vector3 decalNormal,
            Vector3 decalUp,
            float height,
            PackedHsvShift color = default)
        {
            var left = Vector3.Cross(decalNormal, decalUp);
            left *= height * def.AspectRatio / left.Length() / 2;
            return new EquiMeshHelpers.DecalData
            {
                Material = def.Material,
                Position = pos + decalNormal * 0.005f,
                TopLeftUv = def.TopLeftUv,
                BottomRightUv = def.BottomRightUv,
                Normal = VF_Packer.PackNormal(decalNormal),
                Up = new HalfVector3(decalUp * height / 2),
                Left = new HalfVector3(left),
                ColorMask = color
            };
        }

        public static EquiMeshHelpers.LineData CreateLineData(EquiDecorativeLineToolDefinition def, Vector3 a, Vector3 b, float catenaryFactor,
            PackedHsvShift color = default)
        {
            var length = Vector3.Distance(a, b);
            var defaultSegmentsPerMeterSqrt = 0f;
            if (catenaryFactor > 0)
                defaultSegmentsPerMeterSqrt = MathHelper.Lerp(4, 8, MathHelper.Clamp(catenaryFactor, 0, 1));
            var segmentsPerMeterSqrt = def.SegmentsPerMeterSqrt ?? defaultSegmentsPerMeterSqrt;
            var segmentCount = Math.Max(1, (int)Math.Ceiling(length * def.SegmentsPerMeter + Math.Sqrt(length) * segmentsPerMeterSqrt));
            var catenaryLength = catenaryFactor > 0 ? length * (1 + catenaryFactor) : 0;
            return new EquiMeshHelpers.LineData
            {
                Material = def.Material.MaterialName,
                Pt0 = a,
                Pt1 = b,
                Width = def.Width,
                UvOffset = def.UvOffset,
                UvTangent = def.UvTangentPerMeter * Math.Max(catenaryLength, length),
                UvNormal = def.UvNormal,
                Segments = segmentCount,
                HalfSideSegments = def.HalfSideSegments,
                CatenaryLength = catenaryLength,
                UseNaturalGravity = true,
                ColorMask = color
            };
        }

        public static EquiMeshHelpers.SurfaceData CreateSurfaceData(EquiDecorativeSurfaceToolDefinition def, Vector3 a, Vector3 b, Vector3 c, Vector3? d,
            Vector3 alignNormal, PackedHsvShift color = default, UvProjectionMode uvProjection = UvProjectionMode.Bevel, UvBiasMode uvBias = UvBiasMode.XAxis)
        {
            Vector3 norm;
            const float eps = 1e-6f;
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

            FindUvProjection(uvProjection, uvBias, norm, out var uvX, out var uvY);

            Vector2 ComputeUv(in Vector3 pos) => new Vector2(uvX.Dot(pos), uvY.Dot(pos)) / def.TextureSize;

            Vector3 IdealTriangleTangent(Vector3 pt0, Vector3 pt1, Vector3 pt2)
            {
                var uv0 = ComputeUv(pt0);
                var duv1 = ComputeUv(pt1) - uv0;
                var duv2 = ComputeUv(pt2) - uv0;
                return EquiMeshHelpers.ComputeTriangleTangent(pt1 - pt0, duv1, pt2 - pt0, duv2);
            }

            var tangent = IdealTriangleTangent(a, b, c);
            if (d.HasValue)
                tangent += IdealTriangleTangent(a, c, d.Value);

            if (tangent.Normalize() < 1e-6f)
                tangent = Vector3.CalculatePerpendicularVector(norm);

            var snappedTangent = Vector3.Cross(Vector3.Cross(norm, tangent), norm);
            if (snappedTangent.Normalize() <= 1e-6)
                norm.CalculatePerpendicularVector(out snappedTangent);

            EquiMeshHelpers.VertexData CreateVertex(Vector3 pos) => new EquiMeshHelpers.VertexData
            {
                Position = pos,
                Uv = new HalfVector2(ComputeUv(in pos)),
                Normal = VF_Packer.PackNormal(norm),
                Tangent = VF_Packer.PackNormal(snappedTangent),
            };

            return new EquiMeshHelpers.SurfaceData
            {
                Material = def.Material.MaterialName,
                Pt0 = CreateVertex(a),
                Pt1 = CreateVertex(b),
                Pt2 = CreateVertex(c),
                Pt3 = d.HasValue ? (EquiMeshHelpers.VertexData?)CreateVertex(d.Value) : null,
                FlipRearNormals = def.FlipRearNormals,
                ColorMask = color
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
                uvY /= (float) Math.Sqrt(yLength);

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
            return Vector3.Sign(normal) * FirstQuadrantUvProjections[bestI];
        }

        /// <summary>
        /// Commits a feature to the runtime mesh.  Returns true if the feature should be removed.
        /// </summary>
        private bool CommitFeatureInternal(in FeatureKey triplet)
        {
            if (!_features.TryGetValue(triplet, out var data)) return false;
            if (data.RenderId != EquiDynamicMeshComponent.NullId) _dynamicMesh.DestroyObject(data.RenderId);
            if (!triplet.A.TryGetGridLocalAnchor(_gridData, out var blockA)) return true;

            var args = data.Args;
            ulong newRenderable;
            if (triplet.IsDecal)
            {
                if (!(data.Def is EquiDecorativeDecalToolDefinition decalsDef) || !decalsDef.Decals.TryGetValue(args.DecalId, out var decalDef)) return true;
                newRenderable = _dynamicMesh.CreateDecal(CreateDecalData(decalDef, blockA,
                    VF_Packer.UnpackNormal(args.DecalNormal),
                    VF_Packer.UnpackNormal(args.DecalUp),
                    args.DecalHeight,
                    args.Color));
            }
            else if (triplet.IsLine)
            {
                if (!(data.Def is EquiDecorativeLineToolDefinition lineDef)) return true;
                if (!triplet.B.TryGetGridLocalAnchor(_gridData, out var blockB)) return true;
                newRenderable = _dynamicMesh.CreateLine(CreateLineData(lineDef, blockA, blockB, args.CatenaryFactor, args.Color));
            }
            else
            {
                if (!(data.Def is EquiDecorativeSurfaceToolDefinition surfDef)) return true;
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
                newRenderable = _dynamicMesh.CreateSurface(CreateSurfaceData(surfDef, blockA, blockB, blockC, blockD, -localGravity,
                    args.Color, args.UvProjection, args.UvBias));
            }

            _features[triplet] = new RenderData(data.Def, data.Args, newRenderable);
            return false;
        }

        private static List<TV> GetOrCreate<TK, TV>(Dictionary<TK, List<TV>> table, TK id)
        {
            if (!table.TryGetValue(id, out var val))
                table.Add(id, val = new List<TV>());
            return val;
        }

        [RpcSerializable]
        private struct FeatureArgs
        {
            public float CatenaryFactor;

            public MyStringHash DecalId;
            public uint DecalNormal;
            public uint DecalUp;

            public PackedHsvShift Color;

            [NoSerialize]
            public UvProjectionMode UvProjection
            {
                get => (UvProjectionMode)DecalNormal;
                set => DecalNormal = (uint)value;
            }

            [NoSerialize]
            public UvBiasMode UvBias
            {
                get => (UvBiasMode)DecalUp;
                set => DecalUp = (uint)value;
            }

            [NoSerialize]
            public float DecalHeight
            {
                get => CatenaryFactor;
                set => CatenaryFactor = value;
            }
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

        public void AddDecal(BlockAndAnchor pos, EquiDecorativeDecalToolDefinition.DecalDef def, Vector3 normal, Vector3 up, float height, PackedHsvShift color)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(pos, BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var args = new FeatureArgs
            {
                DecalId = def.Id,
                DecalNormal = VF_Packer.PackNormal(normal),
                DecalUp = VF_Packer.PackNormal(up),
                DecalHeight = height,
                Color = color
            };
            if (TryAddFeatureInternal(in key, def.Owner, args))
                RaiseAddFeature_Sync(key, def.Owner.Id, args);
        }

        public void AddLine(BlockAndAnchor a, BlockAndAnchor b, EquiDecorativeLineToolDefinition def, float catenaryFactor, PackedHsvShift color)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var args = new FeatureArgs { CatenaryFactor = catenaryFactor, Color = color };
            if (TryAddFeatureInternal(in key, def, args))
                RaiseAddFeature_Sync(key, def.Id, args);
        }

        public void AddSurface(BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d,
            EquiDecorativeSurfaceToolDefinition def, PackedHsvShift color,
            UvProjectionMode uvProjection = UvProjectionMode.Bevel, UvBiasMode uvBias = UvBiasMode.XAxis)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, c, d);
            var args = new FeatureArgs { Color = color, UvProjection = uvProjection, UvBias = uvBias };
            if (TryAddFeatureInternal(in key, def, args))
                RaiseAddFeature_Sync(key, def.Id, args);
        }

        public void RemoveDecal(BlockAndAnchor a)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }

        public void RemoveLine(BlockAndAnchor a, BlockAndAnchor b)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }

        public void RemoveSurface(BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, c, d);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, (RpcFeatureKey)key);
        }

        public override bool IsSerialized => _features.Count > 0;

        private readonly struct SurfaceGroupKey : IEquatable<SurfaceGroupKey>
        {
            public readonly MyDefinitionId Definition;
            public readonly UvProjectionMode UvProjection;
            public readonly UvBiasMode UvBias;

            public SurfaceGroupKey(MyDefinitionId definition, UvProjectionMode uvProjection, UvBiasMode uvBias)
            {
                Definition = definition;
                UvProjection = uvProjection;
                UvBias = uvBias;
            }

            public bool Equals(SurfaceGroupKey other) => Definition.Equals(other.Definition) && UvProjection == other.UvProjection && UvBias == other.UvBias;

            public override bool Equals(object obj) => obj is SurfaceGroupKey other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = Definition.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)UvProjection;
                hashCode = (hashCode * 397) ^ (int)UvBias;
                return hashCode;
            }
        }

        private readonly struct DecalGroupKey : IEquatable<DecalGroupKey>
        {
            public readonly MyDefinitionId Definition;
            public readonly MyStringHash DecalId;

            public DecalGroupKey(MyDefinitionId definition, MyStringHash decalId)
            {
                Definition = definition;
                DecalId = decalId;
            }

            public bool Equals(DecalGroupKey other) => Definition.Equals(other.Definition) && DecalId.Equals(other.DecalId);

            public override bool Equals(object obj) => obj is DecalGroupKey other && Equals(other);

            public override int GetHashCode() => (Definition.GetHashCode() * 397) ^ DecalId.GetHashCode();
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiDecorativeMeshComponent)base.Serialize(copy);
            using (PoolManager.Get<Dictionary<DecalGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.DecalBuilder>>>(out var decals))
            using (PoolManager.Get<Dictionary<MyDefinitionId, List<MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder>>>(out var lines))
            using (PoolManager.Get<Dictionary<SurfaceGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.SurfaceBuilder>>>(out var triangles))
            {
                foreach (var kv in _features)
                {
                    var triplet = kv.Key;
                    var def = kv.Value.Def.Id;
                    var args = kv.Value.Args;
                    if (triplet.IsDecal)
                        GetOrCreate(decals, new DecalGroupKey(def, args.DecalId)).Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecalBuilder
                        {
                            A = triplet.A.Block.Value,
                            AOffset = triplet.A.PackedAnchor,
                            Normal = args.DecalNormal,
                            Up = args.DecalUp,
                            Height = args.DecalHeight,
                            ColorRaw = args.Color,
                        });
                    else if (triplet.IsLine)
                        GetOrCreate(lines, def).Add(new MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder
                        {
                            A = triplet.A.Block.Value,
                            AOffset = triplet.A.PackedAnchor,
                            B = triplet.B.Block.Value,
                            BOffset = triplet.B.PackedAnchor,
                            CatenaryFactor = args.CatenaryFactor,
                            ColorRaw = args.Color,
                        });
                    else
                        GetOrCreate(triangles, new SurfaceGroupKey(def, args.UvProjection, args.UvBias)).Add(new MyObjectBuilder_EquiDecorativeMeshComponent.SurfaceBuilder
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
                        });
                }

                ob.Decals = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeDecals>(decals.Count);
                foreach (var entry in decals)
                    ob.Decals.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeDecals
                    {
                        Definition = entry.Key.Definition,
                        DecalId = entry.Key.DecalId.String,
                        Decals = entry.Value,
                    });
                ob.Lines = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeLines>(lines.Count);
                foreach (var entry in lines)
                    ob.Lines.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeLines
                    {
                        Definition = entry.Key,
                        Lines = entry.Value,
                    });
                ob.Surfaces = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeSurfaces>(triangles.Count);
                foreach (var entry in triangles)
                    ob.Surfaces.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeSurfaces
                    {
                        Definition = entry.Key.Definition,
                        UvProjection = entry.Key.UvProjection,
                        UvBias = entry.Key.UvBias,
                        Surfaces = entry.Value,
                    });
                decals.Clear();
                lines.Clear();
                triangles.Clear();
            }

            return ob;
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
                            AddFeatureInternal(new FeatureKey(new BlockAndAnchor(item.A, item.AOffset),
                                BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null), def, new FeatureArgs
                            {
                                DecalId = decalId,
                                DecalNormal = item.Normal,
                                DecalUp = item.Up,
                                DecalHeight = item.Height,
                                Color = item.ColorRaw
                            });
                }

            if (ob.Lines != null)
                foreach (var line in ob.Lines)
                {
                    if (line.Lines == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeLineToolDefinition>(line.Definition);
                    if (def == null) continue;
                    foreach (var item in line.Lines)
                        if (item.A != 0 && item.B != 0)
                            AddFeatureInternal(new FeatureKey(
                                    new BlockAndAnchor(item.A, item.AOffset),
                                    new BlockAndAnchor(item.B, item.BOffset),
                                    BlockAndAnchor.Null,
                                    BlockAndAnchor.Null),
                                def,
                                new FeatureArgs
                                {
                                    CatenaryFactor = item.CatenaryFactor,
                                    Color = item.ColorRaw
                                });
                }

            if (ob.Surfaces != null)
                foreach (var triangles in ob.Surfaces)
                {
                    if (triangles.Surfaces == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeSurfaceToolDefinition>(triangles.Definition);
                    if (def == null) continue;
                    var uvProjection = triangles.UvProjection ?? UvProjectionMode.Bevel;
                    var uvBias = triangles.UvBias ?? UvBiasMode.XAxis;
                    foreach (var item in triangles.Surfaces)
                        if (item.A != 0 && item.B != 0 && item.C != 0)
                            AddFeatureInternal(new FeatureKey(
                                    new BlockAndAnchor(item.A, item.AOffset),
                                    new BlockAndAnchor(item.B, item.BOffset),
                                    new BlockAndAnchor(item.C, item.COffset),
                                    item.ShouldSerializeD() ? new BlockAndAnchor(item.D, item.DOffset) : BlockAndAnchor.Null), def,
                                new FeatureArgs
                                {
                                    Color = item.ColorRaw,
                                    UvProjection = uvProjection,
                                    UvBias = uvBias,
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

        public class DecorativeLines : IMyRemappable
        {
            [XmlElement("Definition")]
            public SerializableDefinitionId Definition;

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
        }

        public class DecorativeSurfaces : IMyRemappable
        {
            [XmlElement("Definition")]
            public SerializableDefinitionId Definition;

            [XmlElement("UvProjection")]
            public UvProjectionMode? UvProjection;
            
            [XmlElement("UvBias")]
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