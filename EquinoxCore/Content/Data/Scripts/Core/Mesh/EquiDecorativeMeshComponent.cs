using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Struct;
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
    [MyDependency(typeof(EquiDynamicModelsComponent), Critical = false)]
    [ReplicatedComponent]
    public partial class EquiDecorativeMeshComponent : MyEntityComponent, IMyEventProxy
    {
        private readonly OffloadedDictionary<FeatureKey, RenderData> _features = new OffloadedDictionary<FeatureKey, RenderData>();
        private readonly MyHashSetDictionary<BlockId, FeatureKey> _featureByBlock = new MyHashSetDictionary<BlockId, FeatureKey>();

#pragma warning disable CS0649
        [Automatic]
        private readonly MyGridDataComponent _gridData;

        [Automatic]
        private readonly MyGridConnectivityComponent _gridConnectivity;
#pragma warning restore CS0649
        private EquiDynamicMeshComponent _dynamicMesh;
        private EquiDynamicModelsComponent _dynamicModels;

        public struct SharedArgs
        {
            public PackedHsvShift Color;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _dynamicMesh = Container.GetOrAdd<EquiDynamicMeshComponent>();
            _dynamicModels = Container.GetOrAdd<EquiDynamicModelsComponent>();
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
                    if (!originalMesh._features.TryGetValue(triplet, out var handle)) continue;
                    if (
                        moved.Contains(triplet.A.Block)
                        && (triplet.B.IsNull || moved.Contains(triplet.B.Block))
                        && (triplet.C.IsNull || moved.Contains(triplet.C.Block))
                        && (triplet.D.IsNull || moved.Contains(triplet.D.Block)))
                    {
                        if (splitMesh == null) splitEntity.Components.Add(splitMesh = new EquiDecorativeMeshComponent());
                        splitMesh.AddFeatureInternal(triplet, handle.Value.Def, handle.Value.Args);
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
            foreach (var kv in otherMesh._features)
            {
                // Destroy feature on old grid.
                ref var otherFeature = ref kv.Value;
                otherMesh.DestroyRendererInternal(in kv.Key, ref otherFeature);
                // Copy feature data to new grid.
                finalMesh._features.Add(in kv.Key, in otherFeature);
            }

            // Copy all block -> feature bindings.
            foreach (var kv in otherMesh._featureByBlock)
                finalMesh._featureByBlock.Add(kv.Key, kv.Value);

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

        private struct RenderData
        {
            public EquiDecorativeToolBaseDefinition Def;
            public FeatureArgs Args;
            public ulong? RenderId;
        }

        private void AddFeatureInternal(in FeatureKey triplet, EquiDecorativeToolBaseDefinition def, in FeatureArgs args)
        {
            if (!_features.TryGetValue(in triplet, out var handle))
            {
                handle = _features.AddHandle(in triplet);
                _featureByBlock.Add(triplet.A.Block, triplet);
                if (!triplet.B.IsNull)
                    _featureByBlock.Add(triplet.B.Block, triplet);
                if (!triplet.C.IsNull)
                    _featureByBlock.Add(triplet.C.Block, triplet);
                if (!triplet.D.IsNull)
                    _featureByBlock.Add(triplet.D.Block, triplet);
            }

            ref var feature = ref handle.Value;
            feature.Def = def;
            feature.Args = args;
        }

        private bool DestroyFeatureInternal(in FeatureKey triplet)
        {
            if (!_features.TryGetValue(in triplet, out var handle)) return false;
            DestroyRendererInternal(in triplet, ref handle.Value);

            _featureByBlock.Remove(triplet.A.Block, triplet);
            if (!triplet.B.IsNull)
                _featureByBlock.Remove(triplet.B.Block, triplet);
            if (!triplet.C.IsNull)
                _featureByBlock.Remove(triplet.C.Block, triplet);
            if (!triplet.D.IsNull)
                _featureByBlock.Remove(triplet.D.Block, triplet);
            _features.Remove(in triplet);
            return true;
        }

        /// <summary>
        /// Destroys the renderer associated with a feature without removing the feature from the state.
        /// After calling this the feature MUST be removed from the _features and _featuresByBlock states.
        /// </summary>
        private void DestroyRendererInternal(in FeatureKey triplet, ref RenderData data)
        {
            if (data.RenderId == null) return;

            switch (triplet.Type)
            {
                case FeatureType.Decal:
                case FeatureType.Line:
                case FeatureType.Surface:
                    _dynamicMesh.DestroyObject(data.RenderId.Value);
                    break;
                case FeatureType.Model:
                    _dynamicModels.DestroyObject(data.RenderId.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            data.RenderId = null;
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
            return new BlockAndAnchor(block.Id, BlockAndAnchor.NewAnchorPacking.Pack(norm) | BlockAndAnchor.NewAnchorPackingMask);
        }

        /// <summary>
        /// Commits a feature to the runtime mesh.  Returns true if the feature should be removed.
        /// </summary>
        private bool CommitFeatureInternal(in FeatureKey triplet)
        {
            if (!_features.TryGetValue(triplet, out var handle)) return false;
            ref var feature = ref handle.Value;
            DestroyRendererInternal(in triplet, ref feature);
            if (!triplet.A.TryGetGridLocalAnchor(_gridData, out var blockA)) return true;

            ref var args = ref feature.Args;
            switch (triplet.Type)
            {
                case FeatureType.Decal:
                {
                    if (!(feature.Def is EquiDecorativeDecalToolDefinition decalsDef)) return true;
                    if (!decalsDef.Materials.TryGetValue(args.MaterialId, out var decalDef)) return true;
                    feature.RenderId = _dynamicMesh.CreateDecal(CreateDecalData(decalDef, new DecalArgs<Vector3>
                    {
                        Position = blockA,
                        Normal = VF_Packer.UnpackNormal(args.DecalNormal),
                        Up = VF_Packer.UnpackNormal(args.DecalUp),
                        Height = args.DecalHeight,
                        Shared = args.Shared,
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
                        Shared = args.Shared,
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
                        Shared = args.Shared,
                        UvProjection = args.UvProjection,
                        UvBias = args.UvBias,
                        UvScale = args.UvScale,
                    }, -localGravity));
                    break;
                }
                case FeatureType.Model:
                {
                    if (!(feature.Def is EquiDecorativeModelToolDefinition modelsDef)) return true;
                    if (!modelsDef.Materials.TryGetValue(args.MaterialId, out var modelDef)) return true;
                    var data = CreateModelData(modelDef, new ModelArgs<Vector3>
                    {
                        Position = blockA,
                        Forward = VF_Packer.UnpackNormal(args.ModelForward),
                        Up = VF_Packer.UnpackNormal(args.ModelUp),
                        Scale = args.ModelScale,
                        Shared = args.Shared,
                    });
                    feature.RenderId = _dynamicModels.Create(in data);
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
            public SharedArgs Shared;

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

            #region Models

            [NoSerialize]
            public float ModelScale
            {
                get => _float0;
                set => _float0 = value;
            }

            [NoSerialize]
            public uint ModelForward
            {
                get => _uint0;
                set => _uint0 = value;
            }

            [NoSerialize]
            public uint ModelUp
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

        public override bool IsSerialized => _features.Count > 0;

        private readonly struct SurfaceGroupKey : IEquatable<SurfaceGroupKey>
        {
            public readonly SharedGroupKey Shared;
            public readonly UvProjectionMode UvProjection;
            public readonly UvBiasMode UvBias;

            public SurfaceGroupKey(in RenderData feature)
            {
                Shared = new SharedGroupKey(in feature);
                UvProjection = feature.Args.UvProjection;
                UvBias = feature.Args.UvBias;
            }

            public bool Equals(SurfaceGroupKey other)
            {
                return Shared.Equals(other.Shared) && UvProjection == other.UvProjection && UvBias == other.UvBias;
            }

            public override bool Equals(object obj) => obj is SurfaceGroupKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Shared.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)UvProjection;
                    hashCode = (hashCode * 397) ^ (int)UvBias;
                    return hashCode;
                }
            }
        }

        private readonly struct SharedGroupKey : IEquatable<SharedGroupKey>
        {
            public readonly MyDefinitionId Definition;
            public readonly MyStringHash MaterialId;
            public readonly PackedHsvShift Color;

            public SharedGroupKey(in RenderData feature)
            {
                Definition = feature.Def.Id;
                MaterialId = feature.Args.MaterialId;
                Color = feature.Args.Shared.Color;
            }

            public override bool Equals(object obj) => obj is SharedGroupKey other && Equals(other);

            public bool Equals(SharedGroupKey other)
            {
                return Definition.Equals(other.Definition) && MaterialId.Equals(other.MaterialId) && Color.Equals(other.Color);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Definition.GetHashCode();
                    hashCode = (hashCode * 397) ^ MaterialId.GetHashCode();
                    hashCode = (hashCode * 397) ^ Color.GetHashCode();
                    return hashCode;
                }
            }
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiDecorativeMeshComponent)base.Serialize(copy);
            ob.CopyProtection = Entity.EntityId;
            // Purposefully NOT using ListDictionary since the lists in that are pooled and we have to transfer ownership of these lists out
            // of this method to the returned object builder.
            using (PoolManager.Get<Dictionary<SharedGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.DecalBuilder>>>(out var decals))
            using (PoolManager.Get<Dictionary<SharedGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder>>>(out var lines))
            using (PoolManager.Get<Dictionary<SharedGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.ModelBuilder>>>(out var models))
            using (PoolManager.Get<Dictionary<SurfaceGroupKey, List<MyObjectBuilder_EquiDecorativeMeshComponent.SurfaceBuilder>>>(out var surfaces))
            {
                foreach (var kv in _features)
                {
                    ref readonly var triplet = ref kv.Key;
                    ref var feature = ref kv.Value;
                    ref var args = ref feature.Args;
                    switch (triplet.Type)
                    {
                        case FeatureType.Decal:
                            decals.AddMulti(new SharedGroupKey(in feature), new MyObjectBuilder_EquiDecorativeMeshComponent.DecalBuilder
                            {
                                A = triplet.A.Block.Value,
                                AOffset = triplet.A.PackedAnchor,
                                Normal = args.DecalNormal,
                                Up = args.DecalUp,
                                Height = args.DecalHeight,
                            });
                            break;
                        case FeatureType.Line:
                            lines.AddMulti(new SharedGroupKey(in feature), new MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder
                            {
                                A = triplet.A.Block.Value,
                                AOffset = triplet.A.PackedAnchor,
                                B = triplet.B.Block.Value,
                                BOffset = triplet.B.PackedAnchor,
                                CatenaryFactor = args.CatenaryFactor,
                                WidthA = args.WidthA,
                                WidthB = Math.Abs(args.WidthA - args.WidthB) < 1e-6f ? -1 : args.WidthB,
                            });
                            break;
                        case FeatureType.Surface:
                            surfaces.AddMulti(new SurfaceGroupKey(in feature),
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
                                    UvScale = args.UvScale,
                                });
                            break;
                        case FeatureType.Model:
                            models.AddMulti(
                                new SharedGroupKey(in feature),
                                new MyObjectBuilder_EquiDecorativeMeshComponent.ModelBuilder
                                {
                                    A = triplet.A.Block.Value,
                                    AOffset = triplet.A.PackedAnchor,
                                    Forward = args.ModelForward,
                                    Up = args.ModelUp,
                                    Scale = args.ModelScale,
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
                        ColorRaw = entry.Key.Color,

                        Decals = entry.Value,
                    });
                ob.Lines = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeLines>(lines.Count);
                foreach (var entry in lines)
                    ob.Lines.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeLines
                    {
                        Definition = entry.Key.Definition,
                        MaterialId = entry.Key.MaterialId.String,
                        ColorRaw = entry.Key.Color,

                        Lines = entry.Value,
                    });
                ob.Surfaces = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeSurfaces>(surfaces.Count);
                foreach (var entry in surfaces)
                    ob.Surfaces.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeSurfaces
                    {
                        Definition = entry.Key.Shared.Definition,
                        MaterialId = entry.Key.Shared.MaterialId.String,
                        ColorRaw = entry.Key.Shared.Color,

                        UvProjection = entry.Key.UvProjection,
                        UvBias = entry.Key.UvBias,
                        Surfaces = entry.Value,
                    });
                ob.Models = new List<MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeModels>(surfaces.Count);
                foreach (var entry in models)
                    ob.Models.Add(new MyObjectBuilder_EquiDecorativeMeshComponent.DecorativeModels
                    {
                        Definition = entry.Key.Definition,
                        ModelId = entry.Key.MaterialId.String,
                        ColorRaw = entry.Key.Color,

                        Models = entry.Value,
                    });
                decals.Clear();
                lines.Clear();
                surfaces.Clear();
                models.Clear();
            }

            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = builder as MyObjectBuilder_EquiDecorativeMeshComponent;
            if (ob == null) return;

            // Make decorations into ghosts if this is a copy.  This prevents blueprints from copying the objects.
            var entityGhosted = ob.CopyProtection != null && ob.CopyProtection != Entity.EntityId;

            if (ob.Decals != null)
                foreach (var group in ob.Decals)
                {
                    if (group.Decals == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeDecalToolDefinition>(group.Definition);
                    if (def == null) continue;
                    var materialId = MyStringHash.GetOrCompute(group.DecalId);
                    if (!def.Materials.TryGetValue(materialId, out var materialDef)) continue;
                    foreach (var item in group.Decals)
                        if (item.A != 0)
                            AddFeatureInternal(new FeatureKey(
                                FeatureType.Decal,
                                new BlockAndAnchor(item.A, item.AOffset),
                                BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null), def, new FeatureArgs
                            {
                                MaterialId = materialId,
                                DecalNormal = item.Normal,
                                DecalUp = item.Up,
                                DecalHeight = item.Height,
                                Shared =
                                {
#pragma warning disable CS0612 // Type or member is obsolete, back compat
                                    Color = item.ColorRaw ?? group.ColorRaw,
#pragma warning restore CS0612 // Type or member is obsolete
                                }
                            });
                }

            if (ob.Lines != null)
                foreach (var group in ob.Lines)
                {
                    if (group.Lines == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeLineToolDefinition>(group.Definition);
                    if (def == null) continue;
                    var materialId = MyStringHash.GetOrCompute(group.MaterialId);
                    if (!def.Materials.TryGetValue(materialId, out _)) continue;
                    foreach (var item in group.Lines)
                        if (item.A != 0 && item.B != 0)
                        {
                            var a = new BlockAndAnchor(item.A, item.AOffset);
                            var b = new BlockAndAnchor(item.B, item.BOffset);
                            var key = new FeatureKey(FeatureType.Line, a, b, BlockAndAnchor.Null, BlockAndAnchor.Null);
                            var args = new FeatureArgs
                            {
                                MaterialId = materialId,
                                CatenaryFactor = item.CatenaryFactor,
                                WidthA = item.WidthA,
                                WidthB = item.WidthB,
                                Shared =
                                {
#pragma warning disable CS0612 // Type or member is obsolete, back compat
                                    Color = item.ColorRaw ?? group.ColorRaw,
#pragma warning restore CS0612 // Type or member is obsolete
                                }
                            };
                            // Sorting re-ordered the keys, so reorder the widths too.
                            if (!a.Equals(key.A) && args.WidthB >= 0)
                                (args.WidthA, args.WidthB) = (args.WidthB, args.WidthA);
                            AddFeatureInternal(key, def, args);
                        }
                }

            if (ob.Surfaces != null)
                foreach (var group in ob.Surfaces)
                {
                    if (group.Surfaces == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeSurfaceToolDefinition>(group.Definition);
                    if (def == null) continue;
                    var materialId = MyStringHash.GetOrCompute(group.MaterialId);
                    if (!def.Materials.TryGetValue(materialId, out var materialDef)) continue;
                    var uvProjection = group.UvProjection ?? UvProjectionMode.Bevel;
                    var uvBias = group.UvBias ?? UvBiasMode.XAxis;
                    foreach (var item in group.Surfaces)
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
                                    UvProjection = uvProjection,
                                    UvBias = uvBias,
                                    UvScale = item.UvScale,
                                    Shared =
                                    {
#pragma warning disable CS0612 // Type or member is obsolete, back compat
                                        Color = item.ColorRaw ?? group.ColorRaw,
#pragma warning restore CS0612 // Type or member is obsolete
                                    }
                                });
                }

            if (ob.Models != null)
                foreach (var group in ob.Models)
                {
                    if (group.Models == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeModelToolDefinition>(group.Definition);
                    if (def == null) continue;
                    var materialId = MyStringHash.GetOrCompute(group.ModelId);
                    if (!def.Materials.TryGetValue(materialId, out var materialDef)) continue;
                    foreach (var item in group.Models)
                        if (item.A != 0)
                            AddFeatureInternal(new FeatureKey(
                                FeatureType.Model,
                                new BlockAndAnchor(item.A, item.AOffset),
                                BlockAndAnchor.Null, BlockAndAnchor.Null, BlockAndAnchor.Null), def, new FeatureArgs
                            {
                                MaterialId = materialId,
                                ModelForward = item.Forward,
                                ModelUp = item.Up,
                                ModelScale = item.Scale,
                                Shared =
                                {
#pragma warning disable CS0612 // Type or member is obsolete, back compat
                                    Color = item.ColorRaw ?? group.ColorRaw,
#pragma warning restore CS0612 // Type or member is obsolete
                                }
                            });
                }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public partial class MyObjectBuilder_EquiDecorativeMeshComponent : MyObjectBuilder_EntityComponent, IMyRemappable
    {
        // Entity ID of the owner.  Do NOT remap this value, because it is used to detect when the entity
        // is copied/blueprinted and convert the decorative objects into ghosts.
        public long? CopyProtection;

        public abstract class DecorativeGroup : IMyRemappable
        {
            [XmlElement("Definition")]
            public SerializableDefinitionId Definition;

            [XmlIgnore]
            public PackedHsvShift ColorRaw;

            [XmlElement("Color")]
            [NoSerialize]
            public string Color
            {
                get => ModifierDataColor.Serialize(ColorRaw);
                set => ColorRaw = ModifierDataColor.Deserialize(value).Color;
            }

            public bool ShouldSerializeColor() => !ColorRaw.Equals(default);

            public abstract void Remap(IMySceneRemapper remapper);
        }

        [XmlElement("Lines")]
        public List<DecorativeLines> Lines;

        [XmlElement("Surfaces")]
        public List<DecorativeSurfaces> Surfaces;

        [XmlElement("Decals")]
        public List<DecorativeDecals> Decals;

        [XmlElement("Models")]
        public List<DecorativeModels> Models;

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
            if (Models != null)
                foreach (var model in Models)
                    model.Remap(remapper);
        }
    }
}