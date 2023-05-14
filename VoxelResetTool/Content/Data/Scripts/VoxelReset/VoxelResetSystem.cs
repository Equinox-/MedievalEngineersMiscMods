using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Components;
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
        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            if (!Session.IsDedicated)
                AddFixedUpdate(Render);
        }

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

        private void RefreshModifiedCellsForClient(MyVoxelBase voxel, EndpointId sender, Vector3D position)
        {
            var storage = ((IMyVoxelBase)voxel)?.Storage;
            if (storage == null || !VoxelResetHooks.IsAvailable(storage))
            {
                if (sender.Value == 0)
                    OnQueryResult(voxel?.EntityId ?? 0, false, 0, default);
                else
                    MyAPIGateway.Multiplayer.RaiseEvent(this,
                        x => x.OnQueryResult, voxel?.EntityId ?? 0,
                        false, 0, default(BoundingBoxI), sender);
                return;
            }

            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref position, out var center);
            const int halfExtentInChunks = 5; // ~1000 chunks of space.
            const int halfExtentInMeters = halfExtentInChunks * VoxelResetHooks.LeafSizeInVoxels;
            var queryBox = new BoundingBoxI(center - halfExtentInMeters, center + halfExtentInMeters);
            var result = PoolManager.Get<List<ulong>>();
            MyAPIGateway.Parallel.Start(
                () =>
                {
                    var query = new ObservationQuery(queryBox, result);
                    VoxelResetHooks.QueryChunks(storage, MyStorageDataTypeEnum.Content, ref query);
                    VoxelResetHooks.QueryChunks(storage, MyStorageDataTypeEnum.Material, ref query);
                },
                () =>
                {
                    // Locally invoked
                    if (sender.Value == 0)
                    {
                        OnQueryResult(voxel.EntityId, true, result.Count, queryBox);
                        _modifiedLeaves.AddCollection(result);
                    }
                    else
                    {
                        SendModifiedCellsToClient(voxel, sender, result, queryBox);
                    }

                    PoolManager.Return(ref result);
                });
        }

        private void SendModifiedCellsToClient(MyVoxelBase voxel, EndpointId target, List<ulong> query, BoundingBoxI queryBox)
        {
            using (PoolManager.Get(out List<DeltaEncodedChunk> encoded))
            {
                MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.OnQueryResult, voxel.EntityId, true, query.Count, queryBox, target);
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

                while (queryOffset < query.Count)
                {
                    var pos = MyCellCoord.UnpackCoord(query[queryOffset++]);
                    if (encoded.Count == 0)
                        encodingOffset = pos;
                    var delta = pos - encodingOffset;
                    encoded.Add(new DeltaEncodedChunk { X = delta.X, Y = delta.Y, Z = delta.Z });
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
        private void ResetVoxel(long entityId, Vector3D position, ulong leafId)
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

            var changed = VoxelResetHooks.ResetLeaf(voxel.Storage, MyStorageDataTypeEnum.Content, leafId);
            changed |= VoxelResetHooks.ResetLeaf(voxel.Storage, MyStorageDataTypeEnum.Material, leafId);
            if (changed)
            {
                RefreshModifiedCellsForClient(voxel, sender, position);
                VoxelResetHooks.TryResetClients(voxel, leafId);
            }
        }

        #endregion

        #region Client

        private IMyVoxelBase _voxel;
        private bool _querySuccess;
        private BoundingBoxI _queryVoxelBounds;
        private readonly List<ulong> _modifiedLeaves = new List<ulong>();

        /// <summary>
        /// Attempts to determine if the chunk is modified.
        /// This requires a previously issued query using RequestShow.
        /// </summary>
        public bool TryGetChunkState(IMyVoxelBase voxel, Vector3D worldPos, out bool isModified)
        {
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref worldPos, out var voxelCoord);
            if (_voxel != voxel || !_querySuccess || !_queryVoxelBounds.Contains(voxelCoord))
            {
                isModified = default;
                return false;
            }

            var leafId = new MyCellCoord(0, voxelCoord >> VoxelResetHooks.LeafLodCount).PackId64();
            isModified = _modifiedLeaves.Contains(leafId);
            return true;
        }

        [FixedUpdate]
        private void Render()
        {
            var voxel = _voxel;
            if (voxel == null)
                return;
            using (var batch = MyRenderProxy.DebugDrawBatchAABB(MatrixD.CreateTranslation(voxel.PositionLeftBottomCorner), Color.Red, true, false))
            {
                BoundingBoxI leafBounds = default;
                BoundingBox box = default;
                foreach (var leafId in _modifiedLeaves)
                {
                    VoxelResetHooks.LeafMaxLodBounds(leafId, ref leafBounds);
                    // Make the box exclusive
                    leafBounds.Max += 1;
                    MyVoxelCoordSystems.VoxelCoordToLocalPosition(ref leafBounds.Min, out box.Min);
                    MyVoxelCoordSystems.VoxelCoordToLocalPosition(ref leafBounds.Max, out box.Max);
                    // Shrink slightly so the border between chunks is clearer.
                    box.Inflate(-0.25f);
                    BoundingBoxD boxD = box;
                    batch.Add(ref boxD);
                }
            }
        }

        /// <summary>
        /// Stops showing modified voxel chunks.
        /// </summary>
        public void RequestHide()
        {
            _voxel = null;
            _querySuccess = false;
            _queryVoxelBounds = default;
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

        private static ulong GetLeafId(MyVoxelBase voxel, Vector3D worldPos)
        {
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref worldPos, out var voxelCoord);
            return new MyCellCoord(0, voxelCoord >> VoxelResetHooks.LeafLodCount).PackId64();
        }

        /// <summary>
        /// Resets a specific voxel chunk.
        /// </summary>
        /// <param name="voxel">voxel to reset a chunk for</param>
        /// <param name="worldPos">world position of the chunk to reset</param>
        public void RequestResetVoxel(MyVoxelBase voxel, Vector3D worldPos)
        {
            var leafId = GetLeafId(voxel, worldPos);
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.ResetVoxel, voxel.EntityId, worldPos, leafId);
            else
            {
                var changed = VoxelResetHooks.ResetLeaf(voxel.Storage, MyStorageDataTypeEnum.Content, leafId);
                changed |= VoxelResetHooks.ResetLeaf(voxel.Storage, MyStorageDataTypeEnum.Material, leafId);
                if (changed)
                    RequestShow(voxel, worldPos);
            }
        }

        [Event]
        [Client]
        [Reliable]
        private void OnQueryResult(long entityId, bool success, int resultCount, BoundingBoxI queryBox)
        {
            _voxel = Scene.TryGetEntity(entityId, out var ent) ? ent as IMyVoxelBase : null;
            _querySuccess = success && _voxel != null;
            _queryVoxelBounds = queryBox;
            _modifiedLeaves.Clear();
            if (_querySuccess)
                MyAPIGateway.Utilities?.ShowNotification($"Found {resultCount} modified voxel chunks");
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
            var cell = new MyCellCoord { Lod = 0 };
            foreach (var chunk in chunks)
            {
                cell.CoordInLod.X = offset.X + chunk.X;
                cell.CoordInLod.Y = offset.Y + chunk.Y;
                cell.CoordInLod.Z = offset.Z + chunk.Z;
                _modifiedLeaves.Add(cell.PackId64());
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
        }

        #endregion

        private struct ObservationQuery : VoxelResetHooks.IChunkQuery
        {
            private BoundingBoxI _query;
            private readonly List<ulong> _dirtyCells;

            public ObservationQuery(BoundingBoxI query, List<ulong> dirtyCells)
            {
                _query = query;
                _dirtyCells = dirtyCells;
            }

            public void VisitProviderLeaf(ulong id, in BoundingBoxI maxLodRange)
            {
            }

            public void VisitMicroOctreeLeaf(ulong id, in BoundingBoxI maxLodRange)
            {
                if (!_query.Intersects(maxLodRange))
                    return;
                _dirtyCells.Add(id);
            }

            public bool VisitNode(ulong id, in BoundingBoxI maxLodRange) => _query.Intersects(maxLodRange);
        }
    }
}