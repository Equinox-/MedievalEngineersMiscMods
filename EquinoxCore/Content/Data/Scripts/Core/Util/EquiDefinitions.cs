using VRage.Definitions.Inventory;
using VRage.Factory;
using VRage.Game;

namespace Equinox76561198048419394.Core.Util
{
    public static class EquiDefinitions
    {
        public static bool TryGetItemDefinition(string subtype, out MyInventoryItemDefinition def)
        {
            foreach (var attr in MyObjectFactory<VRage.Game.Entity.MyInventoryItemType, VRage.Game.Entity.MyInventoryItem>.Get().Attributes)
                if (MyDefinitionManager.TryGet(new MyDefinitionId(attr.ObjectBuilderType, subtype), out def))
                    return true;
            def = null;
            return false;
        }
    }
}