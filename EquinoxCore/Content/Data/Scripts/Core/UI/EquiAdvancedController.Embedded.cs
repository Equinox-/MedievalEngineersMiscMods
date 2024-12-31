using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Controllers;
using VRage.Game;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class EmbeddedControllerData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded>
    {
        private readonly MyContextMenuController _controller;

        public EmbeddedControllerData(MyContextMenuController ctl, EquiAdvancedControllerDefinition owner, EmbeddedControllerFactory factory) : base(ctl, owner, factory)
        {
            _controller = MyContextMenuFactory.CreateContextMenuController(factory.Id);
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

    internal sealed class EmbeddedControllerFactory : ControlFactory<MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded>
    {
        private readonly EquiAdvancedControllerDefinition _owner;
        public readonly MyDefinitionId Id;

        public EmbeddedControllerFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded def) : base(def)
        {
            _owner = owner;
            Id = def.Id;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new EmbeddedControllerData(ctl, _owner, this);
    }
}