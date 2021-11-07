using System;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public class VoxelMiningBuffer
    {
        private readonly int[] _harvestVolumeBuffer = new int[byte.MaxValue];

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
                var pos = MyAPIGateway.Entities.FindFreePlace(pointCap, 1) ?? pointCap;
                MySession.Static.Components.Get<MyFloatingObjects>()
                    ?.Spawn(MyInventoryItem.Create(id, available), MatrixD.CreateTranslation(pos));
                return available;
            });
        }
    }
}