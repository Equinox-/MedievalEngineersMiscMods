using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Game.EntityComponents;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.State
{
    [MyComponent(typeof(MyObjectBuilder_EquiStateModelComponent))]
    [MyDependency(typeof(MyEntityStateComponent), Critical = true)]
    [MyDefinitionRequired(typeof(EquiStateModelComponentDefinition))]
    public class EquiStateModelComponent : MyEntityComponent
    {
        private MyEntityStateComponent _stateComponent;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _stateComponent = Container.Get<MyEntityStateComponent>();
            _stateComponent.StateChanged += StateChanged;
            StateChanged(MyStringHash.NullOrEmpty, _stateComponent.CurrentState);
        }

        private void StateChanged(MyStringHash oldstate, MyStringHash newstate)
        {
            string model;
            if (Definition.StateToModel.TryGetValue(newstate, out model))
            {
                Entity.RefreshModels(model, model);
                Entity.Render.RemoveRenderObjects();
                Entity.Render.AddRenderObjects();
                // TODO grids?
            }
        }

        public override void OnRemovedFromScene()
        {
            _stateComponent.StateChanged -= StateChanged;
            _stateComponent = null;
            base.OnRemovedFromScene();
        }

        private EquiStateModelComponentDefinition Definition { get; set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiStateModelComponentDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiStateModelComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiStateModelComponentDefinition))]
    public class EquiStateModelComponentDefinition : MyEntityComponentDefinition
    {
        private readonly Dictionary<MyStringHash, string> _backing = new Dictionary<MyStringHash, string>();
        public IReadOnlyDictionary<MyStringHash, string> StateToModel => _backing;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiStateModelComponentDefinition) def;
            _backing.Clear();
            if (ob.Entries == null) return;
            foreach (var k in ob.Entries)
                _backing[MyStringHash.GetOrCompute(k.State)] = k.Model;
        }
    }
}

[MyObjectBuilderDefinition]
[XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
public class MyObjectBuilder_EquiStateModelComponentDefinition : MyObjectBuilder_EntityComponentDefinition
{
    public struct Entry
    {
        [XmlAttribute]
        public string State;

        [XmlAttribute]
        public string Model;
    }

    [XmlElement("Entry")]
    public Entry[] Entries;
}