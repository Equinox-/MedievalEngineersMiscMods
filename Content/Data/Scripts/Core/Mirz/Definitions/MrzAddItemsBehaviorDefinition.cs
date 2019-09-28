using Sandbox.Definitions.Equipment;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzAddItemsBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        public DefinitionTagId ToAdd;

        public int AmountToAdd;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzAddItemsBehaviorDefinition))]
    public class MrzAddItemsBehaviorDefinition: MyToolBehaviorDefinition
    {
        public MyDefinitionId IdToAdd;

        public int AmountToAdd;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = (MyObjectBuilder_MrzAddItemsBehaviorDefinition)builder;

            IdToAdd = ob.ToAdd;
            AmountToAdd = ob.AmountToAdd;
        }
    }
}
