using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Controllers;
using Sandbox.Graphics.GUI;
using VRage.Game;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class EmbeddedControllerData : IControlHolder
    {
        public MyContextMenuController EmbeddedController;
        public MyGuiControlBase Root { get; set; }

        public void SyncToControl()
        {
            EmbeddedController.Update();
        }

        public void SyncFromControl()
        {
            if (EmbeddedController is IMyCommitableController committable)
                committable.CommitDataSource();
        }
    }

    internal sealed class EmbeddedControllerFactory : ControlFactory
    {
        private readonly EquiAdvancedControllerDefinition _owner;
        private readonly MyDefinitionId _id;

        public EmbeddedControllerFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded def)
        {
            _owner = owner;
            _id = def.Id;
        }

        public override IControlHolder Create(MyContextMenuController ctl)
        {
            var controller = MyContextMenuFactory.CreateContextMenuController(_id);
            controller.BeforeAddedToMenu(ctl.Menu, 0);
            return new EmbeddedControllerData
            {
                Root = controller.CreateControl(),
                EmbeddedController = controller
            };
        }
    }
}