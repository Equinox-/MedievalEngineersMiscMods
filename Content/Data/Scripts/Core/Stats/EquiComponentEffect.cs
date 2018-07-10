using System.Runtime.InteropServices.WindowsRuntime;
using System.Xml.Serialization;
using Medieval.Entities.Components;
using Sandbox.Definitions.Components.Entity.Stats.Effects;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.Entities.Entity.Stats.Effects;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components.Entity.Stats;

namespace Equinox76561198048419394.Core.Stats
{
    [MyEntityEffect(typeof(MyObjectBuilder_EquiComponentEffect))]
    public class EquiComponentEffect : MyEntityEffect
    {
        private MyEntityComponent _activatedComponent;

        private EquiComponentEffectDefinition _definition;
        private MyEntityComponentDefinition _activatedComponentDefinition;

        public override void Init(MyEntityEffectDefinition definition, long applicantEntityId)
        {
            base.Init(definition, applicantEntityId);
            _definition = (EquiComponentEffectDefinition) definition;
            _activatedComponentDefinition = MyDefinitionManager.Get<MyEntityComponentDefinition>(_definition.AddedComponent);
        }

        public override void Activate(MyEntityStatComponent owner)
        {
            base.Activate(owner);
            if (owner.Entity == null || _activatedComponentDefinition == null)
                return; // can't do anything about this

            // Component exists?
            {
                MyEntityComponent existingComponent;
                MyMultiComponent existingMulti;
                if (owner.Entity.Components.TryGet(_definition.AddedComponent.TypeId, _definition.AddedComponent.SubtypeId, out existingMulti))
                    return;
                if (owner.Entity.Components.TryGet(_definition.AddedComponent.TypeId, out existingComponent))
                {
                    existingMulti = existingComponent as MyMultiComponent;
                    if (existingMulti == null || existingMulti.SubtypeId == _definition.AddedComponent.SubtypeId)
                        return;
                }
            }

            if (_activatedComponent == null)
            {
//    TODO            _activatedComponent = MyComponentFactory.Get().CreateInstance(_definition.AddedComponent.TypeId);
                _activatedComponent = new MyPhantomEffectComponent();
                _activatedComponent.Init(_activatedComponentDefinition);
            }

            if (_activatedComponent.Entity == null)
                owner.Entity.Components.Add(_activatedComponent);
        }

        public override void Deactivate()
        {
            if (_activatedComponent != null)
                Owner?.Entity?.Components.Remove(_activatedComponent);
        }
    }

    [XmlSerializerAssembly("VRage.Game.XmlSerializers")]
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_EquiComponentEffect : MyObjectBuilder_EntityEffect
    {
    }
}