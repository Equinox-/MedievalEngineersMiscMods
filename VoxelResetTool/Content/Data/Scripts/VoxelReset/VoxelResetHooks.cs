// #define VOXEL_RESET_HOOKS_ENABLED
// A reflection based implementation is provided here as a reference for how you might implement voxel resetting.
// However it is not whitelist compatible and never will be.
// For local testing of mods that you COMPLETELY TRUST the author of with the safety of your computer and files,
// the following changes to scripting.config can be made to enable this without a server.
// In general you should NEVER enable this implementation unless you're fixing bugs with this script.
// On https://github.com/Equinox-/medieval-engineers-ds-manager enabled dedicated servers the server will have an implementation of all these methods
// injected at runtime.
//
// <AllowType Path="System.Reflection.BindingFlags" />
// <AllowType Path="System.Reflection.FieldAccess" />
// <AllowType Path="System.Reflection.ConstructorInfo" />
// <AllowType Path="System.Reflection.FieldInfo" />
// <AllowType Path="System.Reflection.MethodInfo" />
// <AllowType Path="System.Reflection.MethodBase" />
// <AllowType Path="System.Delegate" />
// <AllowType Path="Sandbox.Engine.Voxels.MyOctreeStorage" />
// <AllowType Path="Sandbox.Engine.Voxels.MyStorageBase" />
// <AllowType Path="Sandbox.Engine.Voxels.IMyOctreeLeafNode" />
// <AllowType Path="Sandbox.Engine.Voxels.MyVoxelHands" />
// <AllowType Path="Sandbox.Engine.Voxels.Shape.MySignedDistanceShape" />
// <AllowType Path="Sandbox.Engine.Voxels.Shape.MyShapeBox" />
// <AllowType Path="System.Type" />
// <AllowType Path="VRage.Game.Voxels.IMyStorage" />
// <AllowType Path="VRage.Game.Voxels.IMyStorageDataProvider" />

using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRage.Voxels;
using VRageMath;
using IMyStorage = VRage.ModAPI.IMyStorage;

#if VOXEL_RESET_HOOKS_ENABLED
using System.Collections;
using System.Linq;
using System.Reflection;
using Sandbox.Engine.Voxels;
using Sandbox.Engine.Voxels.Shape;
#endif

namespace Equinox76561198048419394.VoxelReset
{
    /// <summary>
    /// Helper methods that can be used to access and reset voxel storage.
    /// These methods ONLY work serverside when https://github.com/Equinox-/medieval-engineers-ds-manager replaces it.
    /// </summary>
    public static class VoxelResetHooks
    {
        private const int LeafTypeMissing = 0;
        private const int LeafTypeProvider = 1;
        private const int LeafTypeMicroOctree = 2;

        public const int LeafLodCount = 4;
        private const int ChildCount = 8;
        public const int LeafSizeInVoxels = 1 << LeafLodCount;

#if VOXEL_RESET_HOOKS_ENABLED
        public static bool IsAvailable(IMyStorage storage) => storage is MyOctreeStorage;

        private const BindingFlags Bindings = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Func<MyOctreeStorage, int> TreeHeightField = typeof(MyOctreeStorage)
            .GetField("m_treeHeight", Bindings)
            .CreateGetter<MyOctreeStorage, int>();

        private static readonly Func<MyOctreeStorage, Dictionary<ulong, IMyOctreeLeafNode>> ContentLeaves = typeof(MyOctreeStorage)
            .GetField("m_contentLeaves", Bindings)
            .CreateGetter<MyOctreeStorage, Dictionary<ulong, IMyOctreeLeafNode>>();

        private static readonly Func<MyOctreeStorage, Dictionary<ulong, IMyOctreeLeafNode>> MaterialLeaves = typeof(MyOctreeStorage)
            .GetField("m_materialLeaves", Bindings)
            .CreateGetter<MyOctreeStorage, Dictionary<ulong, IMyOctreeLeafNode>>();

        private static readonly Func<MyOctreeStorage, IDictionary> ContentNodes = typeof(MyOctreeStorage)
            .GetField("m_contentNodes", Bindings)
            .CreateGetter<MyOctreeStorage, IDictionary>();

        private static readonly Func<MyOctreeStorage, IDictionary> MaterialNodes = typeof(MyOctreeStorage)
            .GetField("m_materialNodes", Bindings)
            .CreateGetter<MyOctreeStorage, IDictionary>();

        private static readonly Func<MyStorageBase, FastResourceLock> StorageLock = typeof(MyStorageBase)
            .GetField("m_storageLock", Bindings)
            .CreateGetter<MyStorageBase, FastResourceLock>();

        private static readonly MethodInfo RangeChanged = typeof(MyStorageBase)
            .GetMethod("OnRangeChanged", Bindings);

        private static readonly MethodInfo OnVoxelOperationResponse = typeof(MyVoxelHands)
            .GetMethod("OnVoxelOperationResponse", Bindings);

        private static readonly Type OctreeNodeType = Type.GetType("Sandbox.Engine.Voxels.MyOctreeNode, Sandbox.Game");
        private static readonly FieldInfo OctreeNodeChildMask = OctreeNodeType.GetField("ChildMask", Bindings);
        private static readonly Type MicroOctreeLeafType = Type.GetType("Sandbox.Engine.Voxels.MyMicroOctreeLeaf, Sandbox.Game");
        private static readonly Type ProviderLeafType = Type.GetType("Sandbox.Engine.Voxels.MyProviderLeaf, Sandbox.Game");
        private static readonly ConstructorInfo ProviderLeafConstructor = ProviderLeafType.GetConstructors().First();

        private static int TreeHeightInternal(IMyStorage storage) => TreeHeightField((MyOctreeStorage)storage);

        private static IDictionary NodeDictionary(IMyStorage storage, MyStorageDataTypeEnum type)
        {
            switch (type)
            {
                case MyStorageDataTypeEnum.Content:
                    return ContentNodes((MyOctreeStorage)storage);
                case MyStorageDataTypeEnum.Material:
                    return MaterialNodes((MyOctreeStorage)storage);
                case MyStorageDataTypeEnum.NUM_STORAGE_DATA_TYPES:
                default:
                    return null;
            }
        }

        private static Dictionary<ulong, IMyOctreeLeafNode> LeafDictionary(IMyStorage storage, MyStorageDataTypeEnum type)
        {
            switch (type)
            {
                case MyStorageDataTypeEnum.Content:
                    return ContentLeaves((MyOctreeStorage)storage);
                case MyStorageDataTypeEnum.Material:
                    return MaterialLeaves((MyOctreeStorage)storage);
                case MyStorageDataTypeEnum.NUM_STORAGE_DATA_TYPES:
                default:
                    return null;
            }
        }

        private static byte NodeChildMaskInternal(IMyStorage storage, ulong nodeId, MyStorageDataTypeEnum type)
        {
            var dictionary = NodeDictionary(storage, type);
            object key = nodeId;
            return dictionary != null && dictionary.Contains(key) ? (byte)OctreeNodeChildMask.GetValue(dictionary[key]) : (byte)0;
        }

        private static int TryGetLeafTypeInternal(IMyStorage storage, ulong leafId, MyStorageDataTypeEnum type)
        {
            var dictionary = LeafDictionary(storage, type);
            if (dictionary == null || !dictionary.TryGetValue(leafId, out var leaf))
                return LeafTypeMissing;
            var leafType = leaf.GetType();
            if (leafType == MicroOctreeLeafType)
                return LeafTypeMicroOctree;
            if (leafType == ProviderLeafType)
                return LeafTypeProvider;
            return LeafTypeMissing;
        }

        private static void SetLeafToProvider(IMyStorage storage, ulong leafId, MyStorageDataTypeEnum type)
        {
            var dictionary = LeafDictionary(storage, type);
            var coord = new MyCellCoord();
            coord.SetUnpack(leafId);
            coord.Lod += LeafLodCount;
            dictionary[leafId] = (IMyOctreeLeafNode)ProviderLeafConstructor.Invoke(new object[]
            {
                ((VRage.Game.Voxels.IMyStorage)storage).DataProvider,
                type,
                coord
            });
        }

        private static void DeleteLeafInternal(IMyStorage storage, ulong leafId, MyStorageDataTypeEnum type) => LeafDictionary(storage, type).Remove(leafId);

        private static void DeleteNodeInternal(IMyStorage storage, ulong nodeId, MyStorageDataTypeEnum type) => NodeDictionary(storage, type).Remove(nodeId);

        private static void RaiseResetRangeInternal(IMyStorage storage, Vector3I min, Vector3I max, MyStorageDataTypeEnum type)
        {
            RangeChanged.Invoke(storage, new object[] { min, max, (MyStorageDataTypeFlags)(1 << (int)type) });
        }

        private static FastResourceLock StorageLockInternal(IMyStorage storage) => storage is MyStorageBase impl ? StorageLock(impl) : null;

        private static bool TryResetClientsInternal(MyVoxelBase voxel, BoundingBoxD worldBox)
        {
            var shape = new MyShapeBox
            {
                HalfExtents = (Vector3)worldBox.HalfExtents,
                Rotation = Quaternion.Identity,
                Position = worldBox.Center,
            };
            Func<MyVoxelBase, Action<MySignedDistanceShape, MyVoxelOperationType, byte, bool>> endpoint = x => (Action<MySignedDistanceShape, MyVoxelOperationType, byte, bool>)
                Delegate.CreateDelegate(typeof(Action<MySignedDistanceShape, MyVoxelOperationType, byte, bool>), x, OnVoxelOperationResponse);

            MyAPIGateway.Multiplayer?.RaiseEvent(voxel, endpoint, shape, MyVoxelOperationType.Cut, (byte) 0, true);
            MyAPIGateway.Multiplayer?.RaiseEvent(voxel, endpoint, shape, MyVoxelOperationType.Fill, (byte) 0, true);
            MyAPIGateway.Multiplayer?.RaiseEvent(voxel, endpoint, shape, MyVoxelOperationType.Paint, (byte) 0, true);
            return true;
        }
#else
        // ReSharper disable UnusedParameter.Local
        // ReSharper disable UnusedParameter.Global
        public static bool IsAvailable(IMyStorage storage) => false;

        private static int TreeHeightInternal(IMyStorage storage) => throw new NotSupportedException();
        private static byte NodeChildMaskInternal(IMyStorage storage, ulong nodeId, MyStorageDataTypeEnum type) => throw new NotSupportedException();

        private static int TryGetLeafTypeInternal(IMyStorage storage, ulong leafId, MyStorageDataTypeEnum type) => throw new NotSupportedException();

        private static void SetLeafToProvider(IMyStorage storage, ulong leafId, MyStorageDataTypeEnum type) => throw new NotSupportedException();

        private static void DeleteLeafInternal(IMyStorage storage, ulong leafId, MyStorageDataTypeEnum type) => throw new NotSupportedException();

        private static void DeleteNodeInternal(IMyStorage storage, ulong nodeId, MyStorageDataTypeEnum type) => throw new NotSupportedException();

        private static void RaiseResetRangeInternal(IMyStorage storage, Vector3I min, Vector3I max, MyStorageDataTypeEnum type) => throw new NotSupportedException();

        private static FastResourceLock StorageLockInternal(IMyStorage storage) => throw new NotSupportedException();

        public static bool TryResetClientsInternal(MyVoxelBase voxel, BoundingBoxD worldBox) => false;
        // ReSharper restore UnusedParameter.Global
        // ReSharper restore UnusedParameter.Local
#endif

        public interface IChunkQuery
        {
            /// <summary>
            /// Visits an octree leaf that is using the data provider (no changes)
            /// </summary>
            /// <param name="leafId">leaf chunk ID</param>
            /// <param name="maxLodRange">voxel range of the chunk at the max LOD</param>
            void VisitProviderLeaf(ulong leafId, in BoundingBoxI maxLodRange);

            /// <summary>
            /// Visits an octree leaf that has stored data (changes)
            /// </summary>
            /// <param name="leafId">leaf chunk ID</param>
            /// <param name="maxLodRange">voxel range of the chunk at the max LOD</param>
            void VisitMicroOctreeLeaf(ulong leafId, in BoundingBoxI maxLodRange);

            /// <summary>
            /// Visits an octree leaf that has children.
            /// </summary>
            /// <param name="nodeId">node chunk ID.  This is NOT a leaf chunk ID</param>
            /// <param name="maxLodRange">voxel range of the chunk at the max LOD</param>
            bool VisitNode(ulong nodeId, in BoundingBoxI maxLodRange);
        }

        public static void LeafMaxLodBounds(ulong leafId, ref BoundingBoxI maxLodRange)
        {
            var cell = new MyCellCoord();
            cell.SetUnpack(leafId);
            cell.Lod += LeafLodCount;
            CellMaxLodBounds(in cell, ref maxLodRange);
        }

        public static void CellMaxLodBounds(in MyCellCoord data, ref BoundingBoxI maxLodRange)
        {
            maxLodRange.Min = data.CoordInLod << data.Lod;
            maxLodRange.Max = ((data.CoordInLod + 1) << data.Lod) - 1;
        }

        /// <summary>
        /// Converts an identifier used to load a leaf into one used to load a node AT THE SAME LOCATION.
        /// </summary>
        private static ulong LeafIdAsNodeId(ulong leafId)
        {
            var coord = new MyCellCoord();
            coord.SetUnpack(leafId);
            coord.Lod--;
            return coord.PackId64();
        }

        /// <summary>
        /// Queries the chunks in a voxel storage.
        /// </summary>
        /// <param name="storage">storage to query</param>
        /// <param name="treeType">data type to query</param>
        /// <param name="query">query</param>
        /// <typeparam name="T">query type</typeparam>
        public static void QueryChunks<T>(IMyStorage storage, MyStorageDataTypeEnum treeType, ref T query) where T : struct, IChunkQuery
        {
            if (!IsAvailable(storage))
                return;
            using (PoolManager.Get(out Stack<MyCellCoord> stack))
            using (StorageLockInternal(storage)?.AcquireSharedUsing())
            {
                var treeHeight = TreeHeightInternal(storage);
                stack.Push(new MyCellCoord(treeHeight + LeafLodCount, ref Vector3I.Zero));
                var maxLodRange = new BoundingBoxI();
                while (stack.Count > 0)
                {
                    var data = stack.Pop();
                    var cell = new MyCellCoord(Math.Max(data.Lod - LeafLodCount, 0), ref data.CoordInLod);
                    CellMaxLodBounds(in data, ref maxLodRange);
                    var leafId = cell.PackId64();
                    var leafType = TryGetLeafTypeInternal(storage, leafId, treeType);
                    switch (leafType)
                    {
                        case LeafTypeProvider:
                            query.VisitProviderLeaf(leafId, in maxLodRange);
                            continue;
                        case LeafTypeMicroOctree:
                            query.VisitMicroOctreeLeaf(leafId, in maxLodRange);
                            continue;
                        case LeafTypeMissing:
                        default:
                            break;
                    }

                    var nodeId = LeafIdAsNodeId(leafId);
                    var childMask = NodeChildMaskInternal(storage, nodeId, treeType);
                    if (childMask == 0 || !query.VisitNode(leafId, in maxLodRange))
                        continue;
                    var childTreePos = data.CoordInLod << 1;
                    var childPos = Vector3I.Zero;
                    for (var i = 0; i < ChildCount; i++)
                    {
                        if ((childMask & (1 << i)) == 0)
                            continue;
                        ChildPosition(in childTreePos, i, ref childPos);
                        stack.Push(new MyCellCoord(data.Lod - 1, ref childPos));
                    }
                }
            }
        }

        private static void ChildPosition(in Vector3I childTreePos, int child, ref Vector3I childPos)
        {
            childPos.X = childTreePos.X + ((child >> 0) & 1);
            childPos.Y = childTreePos.Y + ((child >> 1) & 1);
            childPos.Z = childTreePos.Z + ((child >> 2) & 1);
        }

        /// <summary>
        /// Attempts to reset a single leaf of the voxel storage.
        /// </summary>
        /// <param name="storage">storage to reset</param>
        /// <param name="type">data type to reset</param>
        /// <param name="leafId">the leaf chunk ID to reset</param>
        /// <returns>true if reset was successful</returns>
        public static bool ResetLeaf(IMyStorage storage, MyStorageDataTypeEnum type, ulong leafId)
        {
            bool result;
            using (StorageLockInternal(storage)?.AcquireExclusiveUsing())
                result = ResetLeafInternal(storage, type, leafId);
            if (result)
            {
                var data = new MyCellCoord();
                data.SetUnpack(leafId);
                data.Lod += LeafLodCount;
                var min = data.CoordInLod << data.Lod;
                var max = ((data.CoordInLod + 1) << data.Lod) - 1;
                RaiseResetRangeInternal(storage, min, max, type);
            }

            return result;
        }

        private static bool ResetLeafInternal(IMyStorage storage, MyStorageDataTypeEnum type, ulong chunkId)
        {
            if (TryGetLeafTypeInternal(storage, chunkId, type) != LeafTypeMicroOctree)
                return false;
            var leafToReplaceId = chunkId;
            var treeHeight = TreeHeightInternal(storage);
            var parent = new MyCellCoord();
            var leaf = new MyCellCoord();
            while (true)
            {
                leaf.SetUnpack(leafToReplaceId);
                if (leaf.Lod == treeHeight)
                    break;
                parent.Lod = leaf.Lod + 1;
                parent.CoordInLod = leaf.CoordInLod >> 1;
                var parentLeafId = parent.PackId64();
                var parentNodeId = LeafIdAsNodeId(parentLeafId);
                var childTreePos = parent.CoordInLod << 1;
                var childMask = NodeChildMaskInternal(storage, parentNodeId, type);
                var hasAnyDataNodes = false;
                for (var i = 0; i < ChildCount; i++)
                {
                    if ((childMask & (1 << i)) == 0)
                        continue;
                    ChildPosition(in childTreePos, i, ref leaf.CoordInLod);
                    var siblingId = leaf.PackId64();
                    if (siblingId == leafToReplaceId)
                        continue;
                    if (TryGetLeafTypeInternal(storage, siblingId, type) == LeafTypeMicroOctree
                        || NodeChildMaskInternal(storage, LeafIdAsNodeId(siblingId), type) != 0)
                    {
                        hasAnyDataNodes = true;
                        break;
                    }
                }

                // Can't remove the leaf's parent, so this is the final thing that can be removed.
                if (hasAnyDataNodes)
                    break;

                // Remove the parent node and all its children.
                DeleteNodeInternal(storage, parentNodeId, type);
                for (var i = 0; i < ChildCount; i++)
                {
                    ChildPosition(in childTreePos, i, ref leaf.CoordInLod);
                    DeleteLeafInternal(storage, leaf.PackId64(), type);
                }

                leafToReplaceId = parentLeafId;
            }

            SetLeafToProvider(storage, leafToReplaceId, type);
            return true;
        }

        /// <summary>
        /// Resets a voxel range on all clients.
        /// Requires 
        /// </summary>
        public static bool TryResetClients(MyVoxelBase voxel, ulong leafId)
        {
            var box = new BoundingBoxI();
            LeafMaxLodBounds(leafId, ref box);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref box.Min, out var worldMin);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref box.Max, out var worldMax);
            // Extend the reset region a bit to make up for the voxel brush not being exact.
            // Generally better to reset too many voxels than too few. 
            worldMin -= 0.75;
            worldMax += 0.75;

            return TryResetClientsInternal(voxel, new BoundingBoxD(worldMin, worldMax));
        }
    }
}