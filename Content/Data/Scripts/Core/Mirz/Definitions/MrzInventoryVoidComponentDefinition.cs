using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzInventoryVoidComponentDefinition : MyObjectBuilder_MultiComponentDefinition
    {
        public string Inventory;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzInventoryVoidComponentDefinition))]
    public class MrzInventoryVoidComponentDefinition : MyMultiComponentDefinition
    {
        public MyStringHash Inventory;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = (MyObjectBuilder_MrzInventoryVoidComponentDefinition)builder;

            Inventory = MyStringHash.GetOrCompute(ob.Inventory);
        }
    }
}
