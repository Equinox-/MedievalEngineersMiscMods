using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    public enum InventoryInteractionMode
    {
        Input = 0,
        Output = 1,
        Both = 2,
    }

    public enum InventorySearchStart
    {
        Front = 0,
        Back = 1,
    }

    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzInventoryInteractionComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public InventoryInteractionMode? InteractionMode;
        public InventorySearchStart? StartSeachFrom;

        public string Inventory;

        public string InputInteractionMessage;
        public string OutputInteractionMessage;

        public string SuccessNotification;
        public string FailureNotification;

        public string UseObject;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzInventoryInteractionComponentDefinition))]
    public class MrzInventoryInteractionComponentDefinition: MyEntityComponentDefinition
    {
        public InventoryInteractionMode InteractionMode;
        public InventorySearchStart StartSearchFrom;

        public MyStringHash Inventory;
        
        public MyStringId InteractionMessage;

        public MyStringId SuccessNotification;
        public MyStringId FailureNotification;

        public string UseObject;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = (MyObjectBuilder_MrzInventoryInteractionComponentDefinition) builder;

            InteractionMode = ob.InteractionMode ?? InventoryInteractionMode.Both;
            StartSearchFrom = ob.StartSeachFrom ?? InventorySearchStart.Back;
            UseObject = ob.UseObject;

            Inventory = MyStringHash.GetOrCompute(ob.Inventory);

            SuccessNotification = MyStringId.GetOrCompute(ob.SuccessNotification);
            FailureNotification = MyStringId.GetOrCompute(ob.FailureNotification);

            switch (InteractionMode)
            {
                case InventoryInteractionMode.Input:
                    InteractionMessage = MyStringId.GetOrCompute(ob.InputInteractionMessage);
                    break;
                case InventoryInteractionMode.Output:
                    InteractionMessage = MyStringId.GetOrCompute(ob.OutputInteractionMessage);
                    break;
                case InventoryInteractionMode.Both:
                    InteractionMessage = MyStringId.GetOrCompute($"{ob.InputInteractionMessage}\n{ob.OutputInteractionMessage}");
                    break;
                default:
                    InteractionMessage = MyStringId.GetOrCompute("DEFINITION ERROR!");
                    break;
            }
        }
    }
}
