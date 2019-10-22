using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzEquipmentSpawnerDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? LootId;

        public string Inventory;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzEquipmentSpawnerDefinition))]
    public class MrzEquipmentSpawnerDefinition : MyEntityComponentDefinition
    {
        public MyDefinitionId LootTable;

        public MyStringHash Inventory;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = builder as MyObjectBuilder_MrzEquipmentSpawnerDefinition;
            if (ob == null)
                throw new MyDefinitionException("Wrong ob!");

            Inventory = MyStringHash.GetOrCompute(ob.Inventory);
            LootTable = ob.LootId ?? new MyDefinitionId();
        }
    }
}
