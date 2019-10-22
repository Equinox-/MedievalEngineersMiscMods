using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzInventorySwapComponentDefinition : MyObjectBuilder_MultiComponentDefinition
    {
        public struct ItemSwapPair
        {
            public DefinitionTagId Input;

            public DefinitionTagId Output;
        }

        public string Inventory;

        [XmlElement("Swap")]
        public List<ItemSwapPair> Swaps = new List<ItemSwapPair>();
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzInventorySwapComponentDefinition))]
    public class MrzInventorySwapComponentDefinition: MyMultiComponentDefinition
    {
        public MyStringHash Inventory;

        public readonly Dictionary<MyDefinitionId, MyDefinitionId> Swaps = new Dictionary<MyDefinitionId, MyDefinitionId>(MyDefinitionId.Comparer);

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = (MyObjectBuilder_MrzInventorySwapComponentDefinition) builder;

            Inventory = MyStringHash.GetOrCompute(ob.Inventory);

            if (ob.Swaps == null || ob.Swaps.Count == 0)
                return;

            foreach (var pair in ob.Swaps)
                Swaps[pair.Input] = pair.Output;
        }
    }
}
