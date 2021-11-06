using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Inventory;
using VRage.Collections;
using VRage.Definitions;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Logging;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders.Definitions;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Util
{
    public class VoxelPlacementBuffer
    {
        private int _buffered;

        public int AvailableVolume(MyInventoryBase inv, VoxelPlacementDefinition definition) =>
            AvailableVolume(new SingletonEnumerator<MyInventoryBase>(inv), definition);

        private int AvailablePlacements<E>(E enumerator, VoxelPlacementDefinition definition) where E : IEnumerator<MyInventoryBase>
        {
            var placements = int.MaxValue;
            foreach (var item in definition.Items)
            {
                var count = 0;
                enumerator.Reset();
                while (enumerator.MoveNext())
                {
                    var inv = enumerator.Current;
                    count += inv.GetItemAmountFuzzy(item.Key);
                }

                placements = Math.Min(count / item.Value, placements);
                if (placements == 0)
                    break;
            }

            return placements;
        }

        public int AvailableVolume<E>(E enumerator, VoxelPlacementDefinition definition) where E : IEnumerator<MyInventoryBase>
        {
            var placements = AvailablePlacements(enumerator, definition);
            var result = placements * (long) definition.Volume + _buffered;

            enumerator.Reset();
            return result > int.MaxValue ? int.MaxValue : (int) result;
        }

        public bool ConsumeVolume(MyInventoryBase inv, VoxelPlacementDefinition definition, int amount) =>
            ConsumeVolume(new SingletonEnumerator<MyInventoryBase>(inv), definition, amount);

        public bool ConsumeVolume<E>(E enumerator, VoxelPlacementDefinition definition, int volume) where E : IEnumerator<MyInventoryBase>
        {
            if (_buffered > volume)
            {
                _buffered -= volume;
                return true;
            }

            var placements = (int) Math.Ceiling((volume - _buffered) / (double) definition.Volume);
            placements = Math.Min(placements, AvailablePlacements(enumerator, definition));
            var success = true;
            foreach (var item in definition.Items)
            {
                var required = item.Value * placements;
                enumerator.Reset();
                while (enumerator.MoveNext())
                {
                    var inv = enumerator.Current;
                    var amountToRemove = Math.Min(required, inv.GetItemAmountFuzzy(item.Key));
                    if (amountToRemove <= 0) continue;
                    if (inv.RemoveItemsFuzzy(item.Key, amountToRemove)) required -= amountToRemove;
                    if (required <= 0) break;
                }

                success &= required <= 0;
            }

            var totalAvailable = placements * (long) definition.Volume + _buffered;
            totalAvailable -= volume;
            _buffered = totalAvailable > int.MaxValue ? int.MaxValue : (int) totalAvailable;
            return success && _buffered < 0;
        }

        public class VoxelPlacementDefinition
        {
            public readonly MyVoxelMaterialDefinition Material;
            public readonly int Volume;
            public readonly DictionaryReader<MyDefinitionId, int> Items;

            public VoxelPlacementDefinition(MyVoxelMaterialDefinition material, int volume, DictionaryReader<MyDefinitionId, int> items)
            {
                Material = material;
                Volume = volume;
                Items = items;
            }

            public VoxelPlacementDefinition(MyObjectBuilder_VoxelMiningDefinition.MiningDef ob, NamedLogger log)
            {
                Material = MyDefinitionManager.Get<MyVoxelMaterialDefinition>(ob.VoxelMaterial);
                if (Material == null)
                {
                    log.Warning($"Could not find voxel material definition {ob.VoxelMaterial}");
                    Material = MyVoxelMaterialDefinition.Default;
                }

                Volume = ob.VolumeAttribute;
                var replacementItems = new Dictionary<MyDefinitionId, int>();
                foreach (var item in ob.MinedItems)
                {
                    MyObjectBuilderType type;
                    try
                    {
                        type = MyObjectBuilderType.Parse(item.Type);
                    }
                    catch
                    {
                        log.Warning($"Can not parse defined builder type {item.Type}");
                        continue;
                    }

                    var key = new MyDefinitionId(type, MyStringHash.GetOrCompute(item.Subtype));
                    replacementItems[key] = item.Amount;
                }

                Items = replacementItems;
            }
        }
    }
}