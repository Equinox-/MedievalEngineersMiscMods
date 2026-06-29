using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Controllers;
using VRage.Game;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class EmbeddedControllerData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded>
    {
        private readonly MyContextMenuController _controller;
        private readonly DataSourceValueAccessor<long> _rebuildDs;
        private long _lastRebuild;

        public EmbeddedControllerData(MyContextMenuController ctl, EquiAdvancedControllerDefinition owner, EmbeddedControllerFactory factory) : base(ctl, owner, factory)
        {
            _controller = MyContextMenuFactory.CreateContextMenuController(factory.Id);
            _controller.BeforeAddedToMenu(ctl.Menu, ctl.Position);
            var dsr = factory.Def.RebuildReference;
            _rebuildDs = dsr != null ? new DataSourceValueAccessor<long>(ctl, dsr) : default;
            Root = _controller.CreateControl();
        }

        protected override void SyncToControlInternal()
        {
            var ver = _rebuildDs.GetValue() ?? 0L;
            if (ver != _lastRebuild)
            {
                Root = _controller.CreateControl();
                _lastRebuild = ver;
            }
            _controller.Update();
        }

        protected override void SyncFromControlInternal()
        {
            if (_controller is IMyCommitableController committable)
                committable.CommitDataSource();
        }

        public override void OnBecameTopController()
        {
            base.OnBecameTopController();
            _controller.OnBecameTopController();
        }

        public override void OnLostTopController()
        {
            _controller.OnLostTopController();
            base.OnLostTopController();
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