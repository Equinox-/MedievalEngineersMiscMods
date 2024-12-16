using System;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.DataSources;
using Sandbox.Game.GUI.Dialogs;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class LabelData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Label>
    {
        private delegate bool DelParameterBinding(ref object val);

        private readonly object[] _parameters;
        private readonly DelParameterBinding[] _bindings;
        private readonly MyGuiControlLabel _label;

        internal LabelData(MyContextMenuController ctl,
            EquiAdvancedControllerDefinition owner,
            MyObjectBuilder_EquiAdvancedControllerDefinition.Label def) : base(ctl, owner, def)
        {
            var count = def.Parameters?.Length ?? 0;
            _parameters = count == 0 ? Array.Empty<object>() : new object[count];
            _bindings = count == 0 ? Array.Empty<DelParameterBinding>() : new DelParameterBinding[count];
            for (var i = 0; def.Parameters != null && i < count; i++)
                _bindings[i] = Bind(ctl, def.Parameters[i]);
            _label = MakeLabelRoot();
            _label.EnableAutoWrap = def.WordWrapping;
        }

        private static DelParameterBinding Bind(MyContextMenuController ctl, MyObjectBuilder_EquiAdvancedControllerDefinition.DataSourceReference dsr)
        {
            var accessor = new DataSourceAccessor<IMyContextMenuDataSource>(ctl, dsr.Id);
            var binding = InnerBind<string>();
            binding = binding ?? InnerBind<int>();
            binding = binding ?? InnerBind<uint>();
            binding = binding ?? InnerBind<long>();
            binding = binding ?? InnerBind<ulong>();
            binding = binding ?? InnerBind<float>();
            binding = binding ?? InnerBind<double>();
            return binding;

            DelParameterBinding InnerBind<T>() where T : IEquatable<T>
            {
                switch (accessor.DataSource)
                {
                    case IMySingleValueDataSource<T> svd:
                        return (ref object val) =>
                        {
                            var curr = svd.GetData();
                            if (val is T oldT && oldT.Equals(curr)) return false;
                            val = curr;
                            return true;
                        };
                    case IMyArrayDataSource<T> avd:
                        var index = dsr.Index;
                        return (ref object val) =>
                        {
                            var curr = avd.GetData(index);
                            if (val is T oldT && oldT.Equals(curr)) return false;
                            val = curr;
                            return true;
                        };
                    default:
                        return null;
                }
            }
        }

        protected override void SyncToControlInternal()
        {
            var changed = false;
            for (var i = 0; i<_bindings.Length; i++)
                changed |= _bindings[i](ref _parameters[i]);
            if (changed)
                _label.UpdateFormatParams(_parameters);
        }

        protected override void SyncFromControlInternal()
        {
        }
    }

    internal sealed class LabelFactory : ControlFactory
    {
        private readonly EquiAdvancedControllerDefinition _owner;
        private readonly MyObjectBuilder_EquiAdvancedControllerDefinition.Label _def;

        public LabelFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Label def)
        {
            _owner = owner;
            _def = def;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new LabelData(ctl, _owner, _def);
    }
}