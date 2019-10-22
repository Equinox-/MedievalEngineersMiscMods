using System;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.ModAPI;
using VRage.Definitions;
using VRage.Game;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.Core.Mirz.Behaviors
{
    public partial class MrzVoxelPainterBehavior
    {
        [Serializable]
        private struct PaintOperation
        {
            public long PainterId;
            public long VoxelId;
            public SerializableDefinitionId MiningId;

            public Vector3 Position;

            public float Radius;
            public Plane Plane;
            public byte Material;

        }

        /// <summary>
        /// Collect items spawned by material based on it's amount.
        /// </summary>
        /// <param name="voxelMaterial"></param>
        /// <param name="amount"></param>
        private void CollectAmount(int voxelMaterial, ref int amount)
        {
            MyVoxelMiningDefinition.MiningEntry entry;
            if (_mining.MiningEntries.TryGetValue(voxelMaterial, out entry))
            {
                var times = amount / entry.Volume;
                amount = amount % entry.Volume;

                foreach (var item in entry.MinedItems)
                {
                    var quantity = item.Value * times;
                    _inventory.AddOrSpawnItem(item.Key, quantity);
                }
            }
        }

        private readonly int[] _cutAmmounts = new int[256];
        private readonly bool[] _filter = new bool[256];

        private void DoOperationServer(MyVoxelBase target, Vector3 localPos, float radius, Plane plane)
        {
            var operation = new PaintOperation()
            {
                PainterId = Holder.EntityId,
                VoxelId = target.EntityId,
                MiningId = _mining.Id,
                Position = localPos,
                Plane = plane,
                Radius = radius,
                Material = _fillMaterial,
            };

            if (PaintVoxels(target, localPos, radius, plane, _fillMaterial, _filter, _cutAmmounts))
            {
                int maxMaterial = MyVoxelMaterialDefinition.MaterialCount;

                for (int i = 0; i < maxMaterial; ++i)
                {
                    if (_cutAmmounts[i] > 0)
                        CollectAmount(i, ref _cutAmmounts[i]);
                }

                MyAPIGateway.Multiplayer.RaiseStaticEvent(x => DoOperationClient, operation);
            }
        }

        [Event, Reliable, Broadcast]
        private static void DoOperationClient(PaintOperation data)
        {
            MyVoxelBase target;
            if (!MyEntities.TryGetEntityById(data.VoxelId, out target))
                return;

            var miningDef = MyDefinitionManager.Get<MyVoxelMiningDefinition>(data.MiningId);
            if (miningDef == null)
                return;

            var filter = new bool[256];
            for (var i = 0; i < filter.Length; i++)
                filter[i] = miningDef.MiningEntries.ContainsKey(i);

            PaintVoxels(target, data.Position, data.Radius, data.Plane, data.Material, filter, null);
        }

        private static bool PaintVoxels(MyVoxelBase target, Vector3 localPos, float radius, Plane plane, byte material, bool[] filter, int[] amounts)
        {
            var minCorner = new Vector3I(localPos - radius - 1);
            var maxCorner = new Vector3I(localPos + radius + 1);

            Vector3I storageSize = target.Storage.Size - 1;
            Vector3I.Clamp(ref minCorner, ref Vector3I.Zero, ref storageSize, out minCorner);
            Vector3I.Clamp(ref maxCorner, ref Vector3I.Zero, ref storageSize, out maxCorner);

            StorageData.Resize(minCorner, maxCorner);

            target.Storage.ReadRange(StorageData, MyStorageDataTypeFlags.ContentAndMaterial, 0, minCorner, maxCorner);

            var pos = plane.Normal * -plane.D + target.SizeInMetresHalf;
            var localPlane = new Plane(pos - minCorner - .5f, plane.Normal);
            var changed = PaintVoxelsChange(StorageData, localPos - minCorner, localPlane, radius, material, filter, amounts);

            if (changed)
                target.Storage.WriteRange(StorageData, MyStorageDataTypeFlags.Material, minCorner, maxCorner);

            return changed;
        }

        private static bool PaintVoxelsChange(MyStorageData data, Vector3 localCenter, Plane plane, float radius, byte material, bool[] filter, int[] amounts)
        {
            Vector3 relPos = -localCenter;
            Vector3 origin = relPos;

            float maxDist = (radius + 1) * (radius + 1);
            float minDist = (radius - 1) * (radius - 1);

            int linear = 0;
            bool painted = false;

            Vector3I pos, max = data.Size3D;

            for (pos.Z = 0; pos.Z < max.Z; ++pos.Z)
            {
                for (pos.Y = 0; pos.Y < max.Y; ++pos.Y)
                {
                    for (pos.X = 0; pos.X < max.X; ++pos.X)
                    {
                        var dist = relPos.LengthSquared();
                        var pDist = plane.D + plane.Normal.Dot(pos);

                        if (dist < maxDist && pDist > -1)
                        {
                            var existingMaterial = data.Material(linear);

                            if (filter[existingMaterial])
                            {
                                var existingContent = data.Content(linear);
                                byte toSet;
                                if (dist < minDist && pDist > 1)
                                {
                                    toSet = MyVoxelDataConstants.ContentEmpty;
                                }
                                else
                                {
                                    var distance = (float) Math.Sqrt(dist) - radius;

                                    distance = Math.Max(distance, -pDist);
                                    toSet = (byte) ((distance + 1) * MyVoxelDataConstants.HalfContent);

                                    if (toSet >= existingContent && pDist > 1)
                                        toSet = (byte) (existingContent * distance);
                                }

                                if (toSet < existingContent)
                                {
                                    painted = true;
                                    if (amounts != null)
                                        amounts[data.Material(linear)] += existingContent - toSet;

                                    // we only change the material
                                    data.Material(linear, material);
                                }
                            }
                        }

                        linear++;
                        relPos.X++;
                    }
                    relPos.X = origin.X;
                    relPos.Y++;
                }
                relPos.Y = origin.Y;
                relPos.Z++;
            }

            return painted;
        }
    }
}
