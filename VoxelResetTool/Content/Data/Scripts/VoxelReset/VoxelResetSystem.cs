using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Grid;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Components.Session;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.ModAPI;
using VRage.Network;
using VRage.Serialization;
using VRage.Session;
using VRage.Voxels;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.VoxelReset
{
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    public class VoxelResetSystem : MySessionComponent
    {
        public const int SizeResolution = 256;

        public const int ViewRangeLeaves = 10;
        public const int ViewRangeMaxChunks = 500;
        public const double ViewLabelsRange = 50;

        #region Server

        [Event]
        [Reliable]
        [Server]
        private void RequestModifiedCells(long entityId, Vector3D position)
        {
            var sender = MyEventContext.Current.Sender;
            var isLocal = MyEventContext.Current.IsLocallyInvoked;
            if (!isLocal && !MyAPIGateway.Session.IsAdminModeEnabled(sender.Value))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var voxel = Scene.TryGetEntity(entityId, out var ent) ? ent as MyVoxelBase : null;
            if (voxel == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            RefreshModifiedCellsForClient(voxel, sender, position);
        }

        private static readonly MyParallelTask Parallel = new MyParallelTask();

        private void RefreshModifiedCellsForClient(MyVoxelBase voxel, EndpointId sender, Vector3D position)
        {
            var storage = ((IMyVoxelBase)voxel)?.Storage;
            if (storage == null || !VoxelResetHooks.IsAvailable(storage))
            {
                if (sender.Value == 0)
                    OnQueryResult(voxel?.EntityId ?? 0, false, 0, default, default);
                else
                    MyAPIGateway.Multiplayer.RaiseEvent(this,
                        x => x.OnQueryResult, voxel?.EntityId ?? 0,
                        false, 0, default(BoundingBoxI), default(int), sender);
                return;
            }

            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref position, out var center);
            center >>= VoxelResetHooks.LeafLodCount;
            var queryBox = new BoundingBoxI(center - ViewRangeLeaves, center + ViewRangeLeaves);
            var result = PoolManager.Get<List<PackedLeaf>>();
            var totalSize = 0;
            Parallel.Start(
                () =>
                {
                    using (PoolManager.Get(out Dictionary<ulong, int> temp))
                    {
                        var query = new ObservationQuery(queryBox, temp);
                        VoxelResetHooks.QueryChunks(storage, MyStorageDataTypeEnum.Content, ref query);
                        VoxelResetHooks.QueryChunks(storage, MyStorageDataTypeEnum.Material, ref query);
                        if (result.Capacity < temp.Count)
                            result.Capacity = temp.Count + 32;
                        foreach (var leaf in temp)
                        {
                            result.Add(new PackedLeaf(leaf.Key, (byte)Math.Min(leaf.Value / SizeResolution, byte.MaxValue)));
                            totalSize += leaf.Value;
                        }
                    }
                },
                () =>
                {
                    // Locally invoked
                    if (sender.Value == 0)
                    {
                        OnQueryResult(voxel.EntityId, true, result.Count, queryBox, totalSize);
                        _modifiedLeaves.AddCollection(result);
                    }
                    else
                    {
                        SendModifiedCellsToClient(voxel, sender, result, queryBox, totalSize);
                    }

                    PoolManager.Return(ref result);
                });
        }

        private sealed class ChunkDistanceSorter : IComparer<PackedLeaf>
        {
            public Vector3I RelativeToLeaf;

            private int DistanceTo(ulong leafId) => MyCellCoord.UnpackCoord(leafId).RectangularDistance(RelativeToLeaf);

            public int Compare(PackedLeaf x, PackedLeaf y) => DistanceTo(x.LeafId).CompareTo(DistanceTo(y.LeafId));
        }

        private void SendModifiedCellsToClient(MyVoxelBase voxel, EndpointId target, List<PackedLeaf> query, BoundingBoxI leafBox, int totalSize)
        {
            var queryLimit = query.Count;
            if (queryLimit > ViewRangeMaxChunks)
            {
                using (PoolManager.Get(out ChunkDistanceSorter sorter))
                {
                    sorter.RelativeToLeaf = leafBox.Center;
                    query.Sort(sorter);
                    queryLimit = ViewRangeMaxChunks;
                }
            }

            using (PoolManager.Get(out List<DeltaEncodedChunk> encoded))
            {
                MyAPIGateway.Multiplayer.RaiseEvent(this,
                    x => x.OnQueryResult,
                    voxel.EntityId, true, query.Count, leafBox, totalSize, target);
                const int queryChunkMaxBytes = 1024;
                var queryOffset = 0;
                var bytes = 0;
                var encodingOffset = new Vector3I();

                void SendChunk()
                {
                    var encodedArray = encoded.ToArray();
                    MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.OnQueryResultPiece, encodingOffset, encodedArray, target);
                    encoded.Clear();
                    bytes = 0;
                }

                while (queryOffset < queryLimit)
                {
                    var leaf = query[queryOffset++];
                    var pos = MyCellCoord.UnpackCoord(leaf.LeafId);
                    if (encoded.Count == 0)
                        encodingOffset = pos;
                    var delta = pos - encodingOffset;
                    encoded.Add(new DeltaEncodedChunk { X = delta.X, Y = delta.Y, Z = delta.Z, PackedSize = leaf.PackedSize, });
                    var deltaBytes = BytesForVariant(delta.X) + BytesForVariant(delta.Y) + BytesForVariant(delta.Z);
                    bytes += deltaBytes;
                    if (bytes > queryChunkMaxBytes)
                        SendChunk();
                }

                if (encoded.Count > 0)
                    SendChunk();
            }
        }

        /// <summary>
        /// Efficiently writes small integers. Closer to zero, less bytes.
        /// From -64 to 63 (inclusive), 8 bits.
        /// From -8 192 to 8 191 (inclusive), 16 bits.
        /// From -1 048 576 to 1 048 575, 24 bits.
        /// From -134 217 728 to 134 217 727, 32 bits.
        /// Otherwise 40 bits.
        /// </summary>
        private static int BytesForVariant(int value)
        {
            const int cutoff1 = 64;
            const int cutoff2 = cutoff1 * 128;
            const int cutoff3 = cutoff2 * 128;
            const int cutoff4 = cutoff3 * 128;
            if (value >= -cutoff1 && value < cutoff1)
                return 1;
            if (value >= -cutoff2 && value < cutoff2)
                return 2;
            if (value >= -cutoff3 && value < cutoff3)
                return 3;
            if (value >= -cutoff4 && value < cutoff4)
                return 4;
            return 5;
        }

        [Event]
        [Reliable]
        [Server]
        private void ResetVoxel(long entityId, Vector3D position, Vector3I leafCenter, int halfExtent)
        {
            var sender = MyEventContext.Current.Sender;
            if (!MyEventContext.Current.IsLocallyInvoked && !MyAPIGateway.Session.IsAdminModeEnabled(sender.Value))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var voxel = Scene.TryGetEntity(entityId, out var ent) ? ent as MyVoxelBase : null;
            if (voxel == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (ResetImpl(voxel, leafCenter, halfExtent, out var resetBox))
            {
                RefreshModifiedCellsForClient(voxel, sender, position);
                VoxelResetHooks.TryResetClients(voxel, resetBox);
            }
        }

        private bool ResetImpl(IMyVoxelBase voxel, Vector3I leafCenter, int halfExtent, out BoundingBoxI resetBox)
        {
            resetBox = new BoundingBoxI(leafCenter - halfExtent, leafCenter + halfExtent);
            var changed = false;
            // note that EnumeratePoints treats the max as exclusive.
            resetBox.Max += 1;
            foreach (var pt in resetBox.EnumeratePoints())
            {
                var leafId = new MyCellCoord(0, pt).PackId64();
                changed |= VoxelResetHooks.ResetLeaf(voxel.Storage, MyStorageDataTypeEnum.Content, leafId);
                changed |= VoxelResetHooks.ResetLeaf(voxel.Storage, MyStorageDataTypeEnum.Material, leafId);
            }

            resetBox.Max -= 1;
            return changed;
        }

        #endregion

        #region Client

        public IMyVoxelBase Voxel { get; private set; }
        private bool _querySuccess;
        private BoundingBoxI _queryLeafBounds;
        private readonly List<PackedLeaf> _modifiedLeaves = new List<PackedLeaf>();

        public enum ResetState
        {
            NotModified,
            Modified,
            ModifiedAndForbidden
        }
        
        /// <summary>
        /// Attempts to determine if the chunk is modified.
        /// This requires a previously issued query using RequestShow.
        /// </summary>
        public bool TryGetLeafStates(IMyVoxelBase voxel, BoundingBoxI leaves, out ResetState state)
        {
            if (Voxel != voxel || !_querySuccess || !_queryLeafBounds.Intersects(leaves))
            {
                state = default;
                return false;
            }

            state = ResetState.NotModified;

            // EnumeratePoints is exclusive
            leaves.Max += 1;
            foreach (var leafCoord in leaves.EnumeratePoints())
            {
                var leafId = new MyCellCoord(0, leafCoord).PackId64();
                if (!_modifiedLeaves.Contains(new PackedLeaf(leafId, 0)))
                    continue;
                
                state = ResetState.Modified;
                break;
            }

            if (state == ResetState.NotModified)
                return true;

            // Maintain exclusive for world box.
            leaves.Min <<= VoxelResetHooks.LeafLodCount;
            leaves.Max = (leaves.Max + 1) << VoxelResetHooks.LeafLodCount;
            var worldBox = default(BoundingBoxD);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref leaves.Min, out worldBox.Min);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref leaves.Max, out worldBox.Max);

            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyGamePruningStructure.GetTopmostEntitiesInBox(worldBox, entities);
                foreach (var entity in entities)
                {
                    if (MyAPIGateway.Players?.GetPlayerControllingEntity(entity) != null
                        || (entity.Components.TryGet(out MyGridRigidBodyComponent rb) && !rb.IsStatic))
                    {
                        state = ResetState.ModifiedAndForbidden;
                        break;
                    }
                }
            }

            return true;
        }

        public void Render(bool showSize, BoundingBoxI highlightLeaves)
        {
            var voxel = Voxel;
            if (voxel == null)
                return;
            var matrix = MatrixD.CreateTranslation(voxel.PositionLeftBottomCorner);
            var cameraPos = MyCameraComponent.ActiveCamera?.GetPosition() ?? Vector3D.Zero;
            using (var batch = MyRenderProxy.DebugDrawBatchAABB(matrix, Color.Red, true, false))
            {
                BoundingBoxI leafBounds = default;
                BoundingBox box = default;
                foreach (var leaf in _modifiedLeaves)
                {
                    var cell = new MyCellCoord();
                    cell.SetUnpack(leaf.LeafId);
                    leafBounds.Min = cell.CoordInLod << VoxelResetHooks.LeafLodCount;
                    leafBounds.Max = (cell.CoordInLod + 1) << VoxelResetHooks.LeafLodCount;
                    MyVoxelCoordSystems.VoxelCoordToLocalPosition(ref leafBounds.Min, out box.Min);
                    MyVoxelCoordSystems.VoxelCoordToLocalPosition(ref leafBounds.Max, out box.Max);
                    // Shrink slightly so the border between chunks is clearer.
                    box.Inflate(-0.25f);
                    BoundingBoxD boxD = box;
                    batch.Add(ref boxD, highlightLeaves.Contains(cell.CoordInLod) ? Color.Cyan : Color.Red);

                    if (!showSize || leaf.PackedSize == 0)
                        continue;
                    var worldPos = Vector3D.Transform(box.Center, ref matrix);
                    if (Vector3D.DistanceSquared(worldPos, cameraPos) < ViewLabelsRange * ViewLabelsRange)
                    {
                        var size = leaf.Size;
                        var text = $"{size / 1024:F1} KiB";
                        MyRenderProxy.DebugDrawText3D(worldPos, text, Color.Lime, 0.5f, true);
                    }
                }
            }
        }

        /// <summary>
        /// Stops showing modified voxel chunks.
        /// </summary>
        public void RequestHide()
        {
            Voxel = null;
            _querySuccess = false;
            _queryLeafBounds = default;
            _modifiedLeaves.Clear();
        }

        /// <summary>
        /// Starts showing modified voxel chunks.
        /// </summary>
        /// <param name="voxel">voxel to show chunks for</param>
        /// <param name="position">position to show chunks around</param>
        public void RequestShow(MyVoxelBase voxel, Vector3D position)
        {
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.RequestModifiedCells, voxel?.EntityId ?? 0, position);
            else
                RefreshModifiedCellsForClient(voxel, new EndpointId(0), position);
        }

        /// <summary>
        /// Resets a specific voxel chunk.
        /// </summary>
        /// <param name="voxel">voxel to reset a chunk for</param>
        /// <param name="worldPos">world position of the chunk to reset</param>
        public void RequestResetVoxel(MyVoxelBase voxel, Vector3D worldPos, int halfExtent)
        {
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref worldPos, out var leafCoord);
            leafCoord >>= VoxelResetHooks.LeafLodCount;
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.ResetVoxel, voxel.EntityId, worldPos, leafCoord, halfExtent);
            else if (ResetImpl(voxel, leafCoord, halfExtent, out _))
                RequestShow(voxel, worldPos);
        }

        [Event]
        [Client]
        [Reliable]
        private void OnQueryResult(long entityId, bool success, int resultCount, BoundingBoxI queryLeafBox, int totalSize)
        {
            Voxel = Scene.TryGetEntity(entityId, out var ent) ? ent as IMyVoxelBase : null;
            _querySuccess = success && Voxel != null;
            _queryLeafBounds = queryLeafBox;
            _modifiedLeaves.Clear();
            if (_querySuccess)
                MyAPIGateway.Utilities?.ShowNotification($"Found {resultCount} modified voxel chunks ({totalSize / 1024f:F1} KiB)");
            else
                MyAPIGateway.Utilities?.ShowNotification(
                    "Failed to load modified voxel chunks. Only usable on a https://github.com/Equinox-/medieval-engineers-ds-manager enabled server.",
                    textColor: Color.Red);
        }

        [Event]
        [Client]
        [Reliable]
        private void OnQueryResultPiece(Vector3I offset, DeltaEncodedChunk[] chunks)
        {
            var leaf = new MyCellCoord { Lod = 0 };
            foreach (var chunk in chunks)
            {
                leaf.CoordInLod.X = offset.X + chunk.X;
                leaf.CoordInLod.Y = offset.Y + chunk.Y;
                leaf.CoordInLod.Z = offset.Z + chunk.Z;
                _modifiedLeaves.Add(new PackedLeaf(leaf.PackId64(), chunk.PackedSize));
            }
        }

        private struct DeltaEncodedChunk
        {
            [Serialize(MyPrimitiveFlags.VariantSigned)]
            public int X;

            [Serialize(MyPrimitiveFlags.VariantSigned)]
            public int Y;

            [Serialize(MyPrimitiveFlags.VariantSigned)]
            public int Z;

            [Serialize]
            public byte PackedSize;
        }

        #endregion

        private struct ObservationQuery : VoxelResetHooks.IChunkQuery
        {
            private BoundingBoxI _leafQuery;
            private readonly Dictionary<ulong, int> _dirtyCells;

            public ObservationQuery(BoundingBoxI leafQuery, Dictionary<ulong, int> dirtyCells)
            {
                _leafQuery = leafQuery;
                _dirtyCells = dirtyCells;
            }

            public void VisitProviderLeaf(ulong id, in BoundingBoxI leafRange)
            {
            }

            public void VisitMicroOctreeLeaf(ulong id, in BoundingBoxI maxLodRange, int leafSize)
            {
                if (!_leafQuery.Intersects(maxLodRange))
                    return;
                _dirtyCells[id] = _dirtyCells.GetValueOrDefault(id) + leafSize;
            }

            public bool VisitNode(ulong id, in BoundingBoxI maxLodRange) => _leafQuery.Intersects(maxLodRange);
        }

        private readonly struct PackedLeaf : IEquatable<PackedLeaf>
        {
            public readonly ulong LeafId;
            public readonly byte PackedSize;

            public PackedLeaf(ulong leafId, byte packedSize)
            {
                LeafId = leafId;
                PackedSize = packedSize;
            }

            public int Size => PackedSize * SizeResolution;

            public bool Equals(PackedLeaf other) => LeafId == other.LeafId;

            public override bool Equals(object obj) => obj is PackedLeaf other && Equals(other);

            public override int GetHashCode() => LeafId.GetHashCode();
        }
    }
}