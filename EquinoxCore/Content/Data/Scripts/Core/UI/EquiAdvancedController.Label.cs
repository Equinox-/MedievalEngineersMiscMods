using Medieval.GUI.ContextMenu;
using Sandbox.Graphics.GUI;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class LabelData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Label>
    {
        internal LabelData(MyContextMenuController ctl,
            EquiAdvancedControllerDefinition owner,
            LabelFactory factory) : base(ctl, owner, factory)
        {
            var label = MakeLabelRoot();
            label.EnableAutoWrap = factory.Def.WordWrapping;
        }

        protected override void SyncToControlInternal()
        {
        }

        protected override void SyncFromControlInternal()
        {
        }
    }

    internal sealed class LabelFactory : ControlFactory<MyObjectBuilder_EquiAdvancedControllerDefinition.Label>
    {
        private readonly EquiAdvancedControllerDefinition _owner;

        public LabelFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Label def) : base(def, def.Parameters)
        {
            _owner = owner;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new LabelData(ctl, _owner, this);
    }
}