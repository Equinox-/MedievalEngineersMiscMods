using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
using Equinox76561198048419394.Core.Misc;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.SleepThrough
{
    [MyComponent(typeof(MyObjectBuilder_EquiSleepComponent))]
    [MyDependency(typeof(EquiPlayerAttachmentComponent))]
    public class EquiSleepComponent : MyEntityComponent
    {
        private EquiPlayerAttachmentComponent _attachment;

        private readonly List<EquiPlayerAttachmentComponent.Slot> _slots = new List<EquiPlayerAttachmentComponent.Slot>();
        private EquiSleepSessionComponent _session;

        private readonly EquiPlayerAttachmentComponent.Slot.AttachedCharacterChangedDelegate _attachedCharacterChanged;

        public EquiSleepComponent()
        {
            _attachedCharacterChanged = (a, b, c) => _session.MarkDirty();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _session = MySession.Static.Components.Get<EquiSleepSessionComponent>();
            _attachment = Container.Get<EquiPlayerAttachmentComponent>();
            if (Definition?.Slots != null)
            {
                foreach (var k in Definition.Slots)
                {
                    var s = _attachment.GetSlotOrDefault(k);
                    if (s != null)
                        _slots.Add(s);
                }
            } else 
                _slots.AddRange(_attachment.GetSlots());

            foreach (var k in _slots)
            {
                k.AttachedCharacterChanged += _attachedCharacterChanged;
                _session.Register(k);
            }
        }

        public override void OnRemovedFromScene()
        {
            foreach (var k in _slots)
            {
                k.AttachedCharacterChanged -= _attachedCharacterChanged;
                _session.Unregister(k);
            }
            base.OnRemovedFromScene();
        }

        public EquiSleepComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiSleepComponentDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiSleepComponent : MyObjectBuilder_EntityComponentDefinition
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiSleepComponentDefinition))]
    public class EquiSleepComponentDefinition : MyEntityComponentDefinition
    {
        public IReadOnlyList<string> Slots { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiSleepComponentDefinition) def;
            Slots = ob.Slots != null && ob.Slots.Length > 0 ? ob.Slots : null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiSleepComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("Slot")]
        public string[] Slots;
    }
}