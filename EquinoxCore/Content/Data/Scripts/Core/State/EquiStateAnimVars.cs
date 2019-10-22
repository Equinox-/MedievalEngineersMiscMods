using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.EntityComponents;
using VRage;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.State
{
    [MyComponent(typeof(MyObjectBuilder_EquiStateAnimVars))]
    [MyDefinitionRequired(typeof(EquiStateAnimVarsDefinition))]
    [MyDependency(typeof(MyAnimationControllerComponent))]
    [MyDependency(typeof(MyEntityStateComponent), Critical = true)]
    public class EquiStateAnimVars : MyEntityComponent
    {
        private MyEntityStateComponent _state;
        private readonly MultiComponentReference<MyAnimationControllerComponent> _animator = new MultiComponentReference<MyAnimationControllerComponent>();

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _animator.AddToContainer(Container, true, true);
            _state = Container.Get<MyEntityStateComponent>();
            _state.StateChanged += StateChanged;
            _animator.ComponentAdded += OnAnimatorAdded;
            StateChanged(MyStringHash.NullOrEmpty, _state.CurrentState);
        }

        private void OnAnimatorAdded(MyAnimationControllerComponent obj)
        {
            StateChanged(MyStringHash.NullOrEmpty, _state.CurrentState);
        }

        private void StateChanged(MyStringHash oldState, MyStringHash newState)
        {
            var instructions = Definition.InstructionsForState(newState);
            if (instructions == null)
                return;
            foreach (var animator in _animator.Components)
            foreach (var kv in instructions)
            {
                if (kv.Value.Value.HasValue)
                    animator.Variables.SetValue(kv.Key, kv.Value.Value.Value);
                else if (kv.Value.Transform.HasValue)
                    animator.Variables.SetTransformValue(kv.Key, kv.Value.Transform.Value);
            }
        }

        public override void OnRemovedFromScene()
        {
            _animator.ComponentAdded -= OnAnimatorAdded;
            _state.StateChanged -= StateChanged;
            _animator.RemoveFromContainer();
            base.OnRemovedFromScene();
        }

        public EquiStateAnimVarsDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiStateAnimVarsDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiStateAnimVars : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiStateAnimVarsDefinition))]
    public class EquiStateAnimVarsDefinition : MyEntityComponentDefinition
    {
        public struct SetInstruction
        {
            public readonly float? Value;
            public readonly MyTransformD? Transform;

            public SetInstruction(float val)
            {
                Value = val;
                Transform = null;
            }

            public SetInstruction(MyTransformD transform)
            {
                Value = null;
                Transform = transform;
            }
        }

        private readonly Dictionary<MyStringHash, Dictionary<MyStringId, SetInstruction>> _states =
            new Dictionary<MyStringHash, Dictionary<MyStringId, SetInstruction>>();

        public IReadOnlyDictionary<MyStringId, SetInstruction> InstructionsForState(MyStringHash state)
        {
            return _states.GetValueOrDefault(state);
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiStateAnimVarsDefinition) def;

            foreach (var state in ob.States)
            foreach (var when in state.When)
            {
                var stateKey = MyStringHash.GetOrCompute(when);
                Dictionary<MyStringId, SetInstruction> instructions;
                if (!_states.TryGetValue(stateKey, out instructions))
                    _states.Add(stateKey, instructions = new Dictionary<MyStringId, SetInstruction>());

                foreach (var instruction in state.Instructions)
                {
                    var varKey = MyStringId.GetOrCompute(instruction.Key);
                    float valOutput;
                    if (!string.IsNullOrWhiteSpace(instruction.Value) && float.TryParse(instruction.Value, out valOutput))
                        instructions[varKey] = new SetInstruction(valOutput);
                    else if (instruction.Transform.HasValue)
                    {
                        var mat = instruction.Transform.Value.GetMatrix();
                        instructions[varKey] = new SetInstruction(new MyTransformD(in mat));
                    }
                    else
                        MyDefinitionErrors.Add(Package, $"State {when} instruction {varKey} has no value", LogSeverity.Warning);
                }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiStateAnimVarsDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public struct VarEntry
        {
            [XmlAttribute]
            public string Key;

            [XmlAttribute]
            public string Value;

            [XmlElement]
            public MyPositionAndOrientation? Transform;
        }

        public struct StateEntry
        {
            [XmlElement("Set")]
            public VarEntry[] Instructions;

            [XmlElement("When")]
            public string[] When;
        }

        [XmlElement("State")]
        public StateEntry[] States;
    }
}