using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.Entities.UseObject;
using Medieval.GUI.ContextMenu;
using ObjectBuilders.Definitions.GUI;
using Sandbox.Graphics;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.BetterTax
{
    [MyComponent(typeof(MyObjectBuilder_EquiBetterTaxComponent))]
    public class EquiBetterTaxComponent : MyEntityComponent, IMyGenericUseObjectInterfaceFiltered
    {
        public EquiBetterTaxComponentDefinition Definition { get; private set; } = EquiBetterTaxComponentDefinition.Default;

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiBetterTaxComponentDefinition) def ?? EquiBetterTaxComponentDefinition.Default;
        }

        public void Use(string dummy, UseActionEnum _, MyEntity user)
        {
            if (dummy != Definition.PaymentDummy) return;
            if (MySession.Static.PlayerEntity != user) return;
            MyContextMenuScreen.OpenMenu(user, Definition.PaymentMenu.SubtypeName, Entity, user);
        }

        public MyActionDescription GetActionInfo(string dummy, UseActionEnum _)
        {
            if (dummy != Definition.PaymentDummy) return default;
            return new MyActionDescription
            {
                Text = MyStringId.GetOrCompute("Press {0} to pay taxes."),
                FormatParams = new object[] { MyAPIGateway.Input.GetLocalizedShortcut(MyStringHash.GetOrCompute("CharacterUse")) },
                Icon = "ContextMenuBag",
            };
        }

        public UseActionEnum SupportedActions => UseActionEnum.OpenTerminal;
        public UseActionEnum PrimaryAction => UseActionEnum.OpenTerminal;
        public UseActionEnum SecondaryAction => UseActionEnum.OpenTerminal;
        public bool ContinuousUsage => false;
        public bool AppliesTo(string dummyName) => dummyName == Definition.PaymentDummy;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterTaxComponent : MyObjectBuilder_EntityComponent
    {
    }
    
    [MyDefinitionType(typeof(MyObjectBuilder_EquiBetterTaxComponentDefinition))]
    public class EquiBetterTaxComponentDefinition : MyEntityComponentDefinition
    {
        private static EquiBetterTaxComponentDefinition _default;

        private readonly HashSet<EquiBetterTaxAreaSelection.SelectionMode> _modes = new HashSet<EquiBetterTaxAreaSelection.SelectionMode>();

        internal static EquiBetterTaxComponentDefinition Default
        {
            get
            {
                if (_default != null) return _default;
                var def = new EquiBetterTaxComponentDefinition();
                def.Init(new MyObjectBuilder_EquiBetterTaxComponentDefinition());
                return _default = def;
            }
        }

        public HashSetReader<EquiBetterTaxAreaSelection.SelectionMode> SupportedModes => _modes;

        public float ValueMultiplier { get; private set; }

        public string PaymentDummy { get; private set; }
        public MyDefinitionId PaymentMenu { get; private set; }
        
        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiBetterTaxComponentDefinition) def;

            _modes.Clear();
            if (ob.SupportPayLocal ?? true) _modes.Add(EquiBetterTaxAreaSelection.SelectionMode.Local);
            if (ob.SupportPayConnected ?? true) _modes.Add(EquiBetterTaxAreaSelection.SelectionMode.Connected);
            if (ob.SupportPayAll ?? false) _modes.Add(EquiBetterTaxAreaSelection.SelectionMode.All);

            ValueMultiplier = ob.ValueMultiplier ?? 1;
            PaymentDummy = ob.PaymentDummy;
            PaymentMenu = ob.PaymentMenu.HasValue
                ? (MyDefinitionId) ob.PaymentMenu.Value
                :  new MyDefinitionId(typeof(MyObjectBuilder_ContextMenu), "EquiPayTaxesMenu");
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterTaxComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        /// <summary>
        /// Use object dummy name for paying taxes directly.
        /// By default, no use object.
        /// </summary>
        [XmlElement]
        public string PaymentDummy;

        /// <summary>
        /// Menu to use for payment.
        /// By default, EquiPayTaxesMenu.
        /// </summary>
        [XmlElement]
        public SerializableDefinitionId? PaymentMenu;

        /// <summary>
        /// Allow paying for the area of the opened claim block.
        /// </summary>
        [XmlElement]
        public bool? SupportPayLocal;

        /// <summary>
        /// Allow paying for areas connected to the opened claim block.
        /// </summary>
        [XmlElement]
        public bool? SupportPayConnected;

        /// <summary>
        /// Allow paying for any area the player owns.
        /// </summary>
        [XmlElement]
        public bool? SupportPayAll;

        /// <summary>
        /// Value multiplier for paying at this claim block. A value of two will make an item that is usually worth one hour instead be worth two hours.
        /// </summary>
        [XmlElement]
        public float? ValueMultiplier;
    }
}