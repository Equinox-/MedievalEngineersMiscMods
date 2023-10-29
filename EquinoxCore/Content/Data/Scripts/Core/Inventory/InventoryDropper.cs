using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Entities;
using VRage.Collections;
using VRage.Components.Physics;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Inventory
{
    public static class InventoryDropper
    {
        /// <summary>
        /// Drops a collection of inventory items.
        /// </summary>
        /// <param name="items">items to drop</param>
        /// <param name="location">location to drop the items at</param>
        /// <param name="lootBagId">loot bag definition to use</param>
        /// <param name="motionInheritedFrom">physics component to inherit movement from</param>
        /// <returns>dropped item entity, or null if not dropped</returns>
        public static MyEntity DropItems(ListReader<MyInventoryItem> items, Vector3D location,
            MyDefinitionId? lootBagId = null, MyPhysicsComponentBase motionInheritedFrom = null)
        {
            switch (items.Count)
            {
                case 0:
                    return null;
                case 1:
                    return DropItem(items[0], location, motionInheritedFrom);
                default:
                    return DropItemsInBag(items, location, lootBagId, motionInheritedFrom);
            }
        }

        /// <summary>
        /// Drops a collection of inventory items in loot bags.
        /// </summary>
        /// <param name="items">items to drop</param>
        /// <param name="location">location to drop the items at</param>
        /// <param name="lootBagId">loot bag definition to use</param>
        /// <param name="motionInheritedFrom">physics component to inherit movement from</param>
        /// <returns>dropped item entity, or null if not dropped</returns>
        public static MyEntity DropItemsInBag(
            ListReader<MyInventoryItem> items, Vector3D location,
            MyDefinitionId? lootBagId = null, MyPhysicsComponentBase motionInheritedFrom = null)
        {
            if (items.Count == 0)
                return null;
            var scene = MySession.Static?.Scene;
            var bagEntity = scene?.CreateEntity(lootBagId ?? new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "LootBag"));
            if (bagEntity == null)
                return null;
            var bagInventory = bagEntity.Get<MyInventoryBase>();
            if (bagInventory == null)
            {
                bagEntity.Close();
                return null;
            }

            var up = -MyGravityProviderSystem.CalculateTotalGravityInPoint(location);
            if (up.Normalize() < 1e-6f)
                up = Vector3.Up;
            var forward = Vector3.CalculatePerpendicularVector(up);
            var orientation = Quaternion.CreateFromForwardUp(forward, up);
            var localAabb = bagEntity.PositionComp.LocalAABB;
            var pos = FindFreePlaceUtil.FindFreePlaceImproved(location, orientation, localAabb.HalfExtents * 1.5f) ?? location;

            bagEntity.PositionComp.SetWorldMatrix(MatrixD.CreateWorld(pos - localAabb.Center, forward, up), forceUpdate: true);
            foreach (var item in items)
                bagInventory.Add(item, MyInventoryBase.NewItemParams.ForcedInsertion);
            scene.EnqueueEntityActivation(bagEntity);
            return bagEntity;
        }

        /// <summary>
        /// Drops an inventory item.
        /// </summary>
        /// <param name="item">item to drop</param>
        /// <param name="location">location to drop the items at</param>
        /// <param name="motionInheritedFrom">physics component to inherit movement from</param>
        /// <returns>dropped item entity, or null if not dropped</returns>
        public static MyEntity DropItem(MyInventoryItem item, Vector3D location, MyPhysicsComponentBase motionInheritedFrom = null)
        {
            var definition = item.GetDefinition();
            if (definition == null)
                return null;
            var model = MyModels.GetModelOnlyData(definition.Model);
            if (model == null)
                return null;
            var up = -MyGravityProviderSystem.CalculateTotalGravityInPoint(location);
            if (up.Normalize() < 1e-6f)
                up = Vector3.Up;
            var forward = Vector3.CalculatePerpendicularVector(up);
            var orientation = Quaternion.CreateFromForwardUp(forward, up);
            var pos = FindFreePlaceUtil.FindFreePlaceImproved(location, orientation, model.BoundingBox.HalfExtents * 1.5f) ?? location;
            return MySession.Static.Components.Get<MyFloatingObjects>()
                ?.Spawn(item, MatrixD.CreateWorld(pos, forward, up), motionInheritedFrom);
        }
    }
}