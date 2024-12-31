using System;
using Medieval.GUI.ContextMenu;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using VRage;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal interface IControlHolder
    {
        MyGuiControlBase Root { get; }
        void SyncFromControl();
        void SyncToControl();

        void DetachFromMenu();
    }

    internal abstract class ControlHolder<T> : IControlHolder where T : MyObjectBuilder_EquiAdvancedControllerDefinition.ControlBase
    {
        protected readonly MyContextMenuController Ctl;
        protected readonly EquiAdvancedControllerDefinition Owner;
        protected readonly ControlFactory<T> Factory;
        private readonly TemplatedLabel _label;

        protected T Def => Factory.Def;
        private bool _commitPermitted;

        internal ControlHolder(MyContextMenuController ctl, EquiAdvancedControllerDefinition owner, ControlFactory<T> factory)
        {
            Ctl = ctl;
            Owner = owner;
            Factory = factory;
            _label = factory.Label.Create(ctl);
        }

        public MyGuiControlBase Root { get; protected set; }

        private MyGuiControlLabel CreateLabel()
        {
            var label = new MyGuiControlLabel(text: MyTexts.GetString(Def.TextId));
            label.SetToolTip(Def.Tooltip);
            label.ApplyStyle(ContextMenuStyles.LabelStyle());
            _label.BindTo(label);
            return label;
        }

        protected MyGuiControlLabel MakeLabelRoot()
        {
            var label = CreateLabel();
            Root = label;
            return label;
        }

        protected void MakeHorizontalRoot(MyGuiControlBase ctl)
        {
            var label = CreateLabel();
            label.LayoutStyle = MyGuiControlLayoutStyle.DynamicX;

            var containerSize = new Vector2(ctl.Size.X + Ctl.Margin.X + label.Size.X, Math.Max(ctl.Size.Y, label.Size.Y));
            var horizontal = new MyGuiControlParent(size: containerSize);
            horizontal.Layout = new MyHorizontalLayoutBehavior(spacing: Ctl.Margin.X);
            horizontal.Controls.Add(ctl);
            horizontal.Controls.Add(label);
            Root = horizontal;
        }

        protected void MakeVerticalRoot(MyGuiControlBase ctl, MyGuiControlBase valueLabel = null)
        {
            var label = CreateLabel();
            var vertical = new MyGuiControlParent(size: ctl.Size + new Vector2(0.0f, label.Size.Y));
#pragma warning disable CS0618 // Type or member is obsolete
            var layout = new MyLayoutVertical(vertical, Ctl.MarginPx.X);
#pragma warning restore CS0618 // Type or member is obsolete
            if (valueLabel != null)
                layout.Add(label, valueLabel);
            else
                layout.Add(label, MyAlignH.Left);
            layout.Add(ctl, MyAlignH.Center);
            Root = vertical;
        }

        public void SyncToControl()
        {
            _commitPermitted = false;
            _label.UpdateBound();
            SyncToControlInternal();
            _commitPermitted = true;
        }

        protected abstract void SyncToControlInternal();

        public void SyncFromControl()
        {
            if (!_commitPermitted || !Root.Enabled) return;
            SyncFromControlInternal();
        }

        protected abstract void SyncFromControlInternal();

        public virtual void DetachFromMenu()
        {
        }
    }

    internal abstract class ControlFactory
    {
        public abstract IControlHolder Create(MyContextMenuController ctl);
    }

    internal abstract class ControlFactory<T> : ControlFactory where T : MyObjectBuilder_EquiAdvancedControllerDefinition.ControlBase
    {
        public readonly T Def;
        public readonly TemplatedLabelFactory Label;

        protected ControlFactory(T def, DataSourceReference[] positionalLabelArgs = null)
        {
            Def = def;
            Label = new TemplatedLabelFactory(def.Text, positionalLabelArgs);
        }
    }
}