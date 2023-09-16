using System;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public class VoxelMiningBuffer
    {
        private readonly int[] _harvestVolumeBuffer = new int[byte.MaxValue];

        public bool HasAnyItems(MyVoxelMiningDefinition def)
        {
            foreach (var entry in def.MiningEntries)
                if (_harvestVolumeBuffer[entry.Key] >= entry.Value.Volume)
                    return true;
            return false;
        }

        public void Add(byte materialId, int volume) => _harvestVolumeBuffer[materialId] += volume;

        public delegate int DelForEachItem<in T>(T arg, MyDefinitionId item, int amount);

        public bool ForEachItem<T>(MyVoxelMiningDefinition def, T arg, DelForEachItem<T> lambda)
        {
            var modified = false;
            foreach (var entry in def.MiningEntries)
            {
                ref var bufferedVoxel = ref _harvestVolumeBuffer[entry.Key];
                var count = bufferedVoxel / entry.Value.Volume;
                if (count <= 0) continue;
                foreach (var item in entry.Value.MinedItems)
                {
                    var amount = item.Value * count;
                    if (amount <= 0) continue;
                    var consumed = lambda(arg, item.Key, amount);
                    if (consumed <= 0) continue;
                    modified = true;
                    bufferedVoxel -= consumed * entry.Value.Volume;
                }
            }

            return modified;
        }

        public bool PutMaterialsInto(MyInventoryBase dest, MyVoxelMiningDefinition def)
        {
            return ForEachItem(def, dest, (inv, id, available) =>
            {
                var insertAmount = Math.Min(available, inv.ComputeAmountThatFits(id));
                if (insertAmount <= 0) return 0;
                return inv.AddItems(id, insertAmount) ? insertAmount : 0;
            });
        }

        public bool DropMaterials(Vector3D point, MyVoxelMiningDefinition def)
        {
            return ForEachItem(def, point, (pointCap, id, available) =>
            {
                var item = MyInventoryItem.Create(id, available);
                var definition = item.GetDefinition();
                if (definition == null)
                    return 0;
                var model = MyModels.GetModelOnlyData(definition.Model);
                if (model == null)
                    return 0;
                var up = -MyGravityProviderSystem.CalculateTotalGravityInPoint(pointCap);
                if (up.Normalize() < 1e-6f)
                    up = Vector3.Up;
                var forward = Vector3.CalculatePerpendicularVector(up);
                var orientation = Quaternion.CreateFromForwardUp(forward, up);
                var pos = FindFreePlaceUtil.FindFreePlaceImproved(pointCap, orientation, model.BoundingBox.HalfExtents * 1.5f) ?? pointCap;
                MySession.Static.Components.Get<MyFloatingObjects>()
                    ?.Spawn(item, MatrixD.CreateWorld(pos, forward, up));
                return available;
            });
        }
    }
}