using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entities.Gravity;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.Scene;
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
                        splitMesh.AddFeatureInternal(triplet, feature.Def, feature.CatenaryFactor);
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
                finalMesh._features[kv.Key] = new RenderData(kv.Value.Def, kv.Value.CatenaryFactor, EquiDynamicMeshComponent.NullId);
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
            public readonly float CatenaryFactor;

            public RenderData(MyDefinitionBase def, float catenaryFactor, ulong renderId)
            {
                Def = def;
                CatenaryFactor = catenaryFactor;
                RenderId = renderId;
            }
        }

        private void AddFeatureInternal(in FeatureKey triplet, MyDefinitionBase def, float catenaryFactor)
        {
            if (_features.TryGetValue(triplet, out var data))
                data = new RenderData(def, catenaryFactor, data.RenderId);
            else
            {
                data = new RenderData(def, catenaryFactor, EquiDynamicMeshComponent.NullId);
                _featureByBlock.Add(triplet.A.Block, triplet);
                _featureByBlock.Add(triplet.B.Block, triplet);
                if (triplet.C.Block != BlockId.Null)
                    _featureByBlock.Add(triplet.C.Block, triplet);
                if (triplet.D.Block != BlockId.Null)
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
            _featureByBlock.Remove(triplet.B.Block, triplet);
            if (triplet.C.Block != BlockId.Null)
                _featureByBlock.Remove(triplet.C.Block, triplet);
            if (triplet.D.Block != BlockId.Null)
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
            return bestAnchor.Block != BlockId.Null;
        }

        public static BlockAndAnchor CreateAnchorFromBlockLocalPosition(MyGridDataComponent grid, MyBlock block, Vector3 blockLocalPosition)
        {
            var size = block.Definition.Size * grid.Size;
            var norm = blockLocalPosition / size;
            return new BlockAndAnchor(block.Id, AnchorPacking.Pack(norm));
        }

        public static EquiMeshHelpers.LineData CreateLineData(EquiDecorativeLineToolDefinition def, Vector3 a, Vector3 b, float catenaryFactor)
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
            };
        }

        public static EquiMeshHelpers.SurfaceData CreateSurfaceData(EquiDecorativeSurfaceToolDefinition def, Vector3 a, Vector3 b, Vector3 c, Vector3? d,
            Vector3 alignNormal)
        {
            Vector3 norm;
            if (d.HasValue)
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

            var projection = norm;
            var dropBelow = projection.AbsMax() / 20;
            if (Math.Abs(projection.X) < dropBelow) projection.X = 0;
            if (Math.Abs(projection.Y) < dropBelow) projection.Y = 0;
            if (Math.Abs(projection.Z) < dropBelow) projection.Z = 0;

            projection.Normalize();

            var uvX = Vector3.CalculatePerpendicularVector(projection);
            uvX.Normalize();
            var uvY = Vector3.Cross(projection, uvX);
            uvY.Normalize();

            EquiMeshHelpers.VertexData CreateVertex(Vector3 pos) => new EquiMeshHelpers.VertexData
            {
                Position = pos,
                Uv = new HalfVector2(new Vector2(uvX.Dot(pos), uvY.Dot(pos)) / def.TextureSize),
            };

            return new EquiMeshHelpers.SurfaceData
            {
                Material = def.Material.MaterialName,
                Pt0 = CreateVertex(a),
                Pt1 = CreateVertex(b),
                Pt2 = CreateVertex(c),
                Pt3 = d.HasValue ? (EquiMeshHelpers.VertexData?)CreateVertex(d.Value) : null,
                Normal = norm,
                FlipRearNormals = def.FlipRearNormals,
            };
        }

        /// <summary>
        /// Commits a feature to the runtime mesh.  Returns true if the feature should be removed.
        /// </summary>
        private bool CommitFeatureInternal(in FeatureKey triplet)
        {
            if (!_features.TryGetValue(triplet, out var data)) return false;
            if (data.RenderId != EquiDynamicMeshComponent.NullId) _dynamicMesh.DestroyObject(data.RenderId);
            if (!triplet.A.TryGetGridLocalAnchor(_gridData, out var blockA)) return true;
            if (!triplet.B.TryGetGridLocalAnchor(_gridData, out var blockB)) return true;

            ulong newRenderable;
            if (triplet.IsLine)
            {
                if (!(data.Def is EquiDecorativeLineToolDefinition lineDef)) return true;
                newRenderable = _dynamicMesh.CreateLine(CreateLineData(lineDef, blockA, blockB, data.CatenaryFactor));
            }
            else
            {
                if (!(data.Def is EquiDecorativeSurfaceToolDefinition surfDef)) return true;
                if (!triplet.C.TryGetGridLocalAnchor(_gridData, out var blockC)) return true;
                Vector3? blockD;
                if (triplet.D.Block != BlockId.Null)
                {
                    if (!triplet.D.TryGetGridLocalAnchor(_gridData, out var blockDVal)) return true;
                    blockD = blockDVal;
                }
                else
                    blockD = null;

                var gravityWorld = MyGravityProviderSystem.CalculateNaturalGravityInPoint(Entity.GetPosition());
                var localGravity = Vector3.TransformNormal(gravityWorld, Entity.PositionComp.WorldMatrixNormalizedInv);
                newRenderable = _dynamicMesh.CreateSurface(CreateSurfaceData(surfDef, blockA, blockB, blockC, blockD, -localGravity));
            }

            _features[triplet] = new RenderData(data.Def, data.CatenaryFactor, newRenderable);
            return false;
        }

        private static List<T> GetOrCreate<T>(Dictionary<MyDefinitionId, List<T>> table, MyDefinitionId id)
        {
            if (!table.TryGetValue(id, out var val))
                table.Add(id, val = new List<T>());
            return val;
        }

        private bool TryAddFeatureInternal(in FeatureKey key, EquiDecorativeToolBaseDefinition def, float catenaryFactor)
        {
            AddFeatureInternal(key, def, catenaryFactor);
            if (!CommitFeatureInternal(key))
                return true;
            DestroyFeatureInternal(key);
            return false;
        }

        private bool TryGetCenter(in FeatureKey key, out Vector3 center)
        {
            var localPosCount = 0;
            center = Vector3.Zero;
            if (key.A.TryGetGridLocalAnchor(_gridData, out var v))
            {
                localPosCount++;
                center += v;
            }

            if (key.B.TryGetGridLocalAnchor(_gridData, out v))
            {
                localPosCount++;
                center += v;
            }

            if (key.C.TryGetGridLocalAnchor(_gridData, out v))
            {
                localPosCount++;
                center += v;
            }

            if (key.D.TryGetGridLocalAnchor(_gridData, out v))
            {
                localPosCount++;
                center += v;
            }

            center /= localPosCount;

            return localPosCount > 0;
        }

        [Event, Reliable, Broadcast]
        private void AddFeature_Sync(FeatureKey key, SerializableDefinitionId id, float catenaryFactor)
        {
            var def = MyDefinitionManager.Get<EquiDecorativeToolBaseDefinition>(id);
            if (def != null)
                TryAddFeatureInternal(key, def, catenaryFactor);
        }

        [Event, Reliable, Broadcast]
        private void RemoveFeature_Sync(FeatureKey key)
        {
            DestroyFeatureInternal(in key);
        }

        public void AddLine(BlockAndAnchor a, BlockAndAnchor b, EquiDecorativeLineToolDefinition def, float catenaryFactor)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var mp = MyAPIGateway.Multiplayer;
            if (TryAddFeatureInternal(in key, def, catenaryFactor))
                mp?.RaiseEvent(this, ctx => ctx.AddFeature_Sync, key, (SerializableDefinitionId)def.Id, catenaryFactor);
        }

        public void AddSurface(BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d,
            EquiDecorativeSurfaceToolDefinition def)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, c, d);
            var mp = MyAPIGateway.Multiplayer;
            if (TryAddFeatureInternal(in key, def, 0))
                mp?.RaiseEvent(this, ctx => ctx.AddFeature_Sync, key, (SerializableDefinitionId)def.Id, 0f);
        }

        public void RemoveLine(BlockAndAnchor a, BlockAndAnchor b)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, BlockAndAnchor.Null, BlockAndAnchor.Null);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, key);
        }

        public void RemoveSurface(BlockAndAnchor a, BlockAndAnchor b, BlockAndAnchor c, BlockAndAnchor d)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            var key = new FeatureKey(a, b, c, d);
            var mp = MyAPIGateway.Multiplayer;
            if (DestroyFeatureInternal(in key))
                mp?.RaiseEvent(this, ctx => ctx.RemoveFeature_Sync, key);
        }

        public override bool IsSerialized => _features.Count > 0;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiDecorativeMeshComponent)base.Serialize(copy);
            using (PoolManager.Get<Dictionary<MyDefinitionId, List<MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder>>>(out var lines))
            using (PoolManager.Get<Dictionary<MyDefinitionId, List<MyObjectBuilder_EquiDecorativeMeshComponent.SurfaceBuilder>>>(out var triangles))
            {
                foreach (var kv in _features)
                {
                    var triplet = kv.Key;
                    var def = kv.Value.Def.Id;
                    if (triplet.IsLine)
                        GetOrCreate(lines, def).Add(new MyObjectBuilder_EquiDecorativeMeshComponent.LineBuilder
                        {
                            A = triplet.A.Block.Value,
                            AOffset = triplet.A.PackedAnchor,
                            B = triplet.B.Block.Value,
                            BOffset = triplet.B.PackedAnchor,
                            CatenaryFactor = kv.Value.CatenaryFactor,
                        });
                    else
                        GetOrCreate(triangles, def).Add(new MyObjectBuilder_EquiDecorativeMeshComponent.SurfaceBuilder
                        {
                            A = triplet.A.Block.Value,
                            AOffset = triplet.A.PackedAnchor,
                            B = triplet.B.Block.Value,
                            BOffset = triplet.B.PackedAnchor,
                            C = triplet.C.Block.Value,
                            COffset = triplet.C.PackedAnchor,
                            D = triplet.D.Block.Value,
                            DOffset = triplet.D.PackedAnchor,
                        });
                }

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
                        Definition = entry.Key,
                        Surfaces = entry.Value,
                    });
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
            var commit = Entity?.InScene ?? false;
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
                                BlockAndAnchor.Null), def, item.CatenaryFactor);
                }

            if (ob.Surfaces != null)
                foreach (var triangles in ob.Surfaces)
                {
                    if (triangles.Surfaces == null) continue;
                    var def = MyDefinitionManager.Get<EquiDecorativeSurfaceToolDefinition>(triangles.Definition);
                    if (def == null) continue;
                    foreach (var item in triangles.Surfaces)
                        if (item.A != 0 && item.B != 0 && item.C != 0)
                            AddFeatureInternal(new FeatureKey(
                                new BlockAndAnchor(item.A, item.AOffset),
                                new BlockAndAnchor(item.B, item.BOffset),
                                new BlockAndAnchor(item.C, item.COffset),
                                item.ShouldSerializeD() ? new BlockAndAnchor(item.D, item.DOffset) : BlockAndAnchor.Null), def, 0);
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
        }

        public class DecorativeSurfaces : IMyRemappable
        {
            [XmlElement("Definition")]
            public SerializableDefinitionId Definition;

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

        public void Remap(IMySceneRemapper remapper)
        {
            if (Lines != null)
                foreach (var line in Lines)
                    line.Remap(remapper);
            if (Surfaces != null)
                foreach (var surf in Surfaces)
                    surf.Remap(remapper);
        }
    }
}