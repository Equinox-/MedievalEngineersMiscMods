using Sandbox.Game.Entities.Inventory;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Library.Utils;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Extensions
{
    public static class MrzItemExtensions
    {
        /// <summary>
        /// Match an item against an id which can be either item definition or a tag.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static bool MatchIdOrTag(this MyInventoryItem item, MyDefinitionId id)
        {
            return item.DefinitionId == id || item.HasTag(id.SubtypeId);
        }

        /// <summary>
        /// Returns a random item id associated with this tag. If no items belong to this tag, or the tag is null, null is returned.
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        public static MyDefinitionId? GetRandomItemFromTag(MyStringHash tag)
        {
            var tagDef = MyDefinitionManager.Get<MyItemTagDefinition>(tag);
            if (tagDef?.Items == null || tagDef.Items.Count == 0)
                return null;

            var index = MyRandom.Instance.Next(tagDef.Items.Count);
            return tagDef.Items[index].Id;
        }
    }
}
