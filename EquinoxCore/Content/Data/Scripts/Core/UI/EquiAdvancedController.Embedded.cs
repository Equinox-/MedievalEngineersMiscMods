using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Controllers;
using Sandbox.Graphics.GUI;
using VRage.Game;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class EmbeddedControllerData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded>
    {
        private readonly MyContextMenuController _controller;

        public EmbeddedControllerData(MyContextMenuController ctl, EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded def,
            MyDefinitionId id) : base(ctl, owner, def)
        {
            _controller = MyContextMenuFactory.CreateContextMenuController(id);
            _controller.BeforeAddedToMenu(ctl.Menu, 0);
            Root = _controller.CreateControl();
        }

        protected override void SyncToControlInternal()
        {
            _controller.Update();
        }

        protected override void SyncFromControlInternal()
        {
            if (_controller is IMyCommitableController committable)
                committable.CommitDataSource();
        }

        public override void DetachFromMenu()
        {
            _controller.AfterRemovedFromMenu(Ctl.Menu);
            base.DetachFromMenu();
        }
    }

    internal sealed class EmbeddedControllerFactory : ControlFactory
    {
        private readonly EquiAdvancedControllerDefinition _owner;
        private readonly MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded _def;
        private readonly MyDefinitionId _id;

        public EmbeddedControllerFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded def)
        {
            _owner = owner;
            _def = def;
            _id = def.Id;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new EmbeddedControllerData(ctl, _owner, _def, _id);
    }
}