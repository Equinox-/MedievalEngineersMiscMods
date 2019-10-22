using System.Xml.Serialization;
using Sandbox.Game.EntityComponents.Grid;
using VRage.Components;
using VRage.Components.Block;
using VRage.Components.Physics;
using VRage.Definitions.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Core.Misc
{
    [MyComponent(typeof(MyObjectBuilder_EquiPhysicsTweakerComponent))]
    [MyDefinitionRequired(typeof(EquiPhysicsTweakerComponentDefinition))]
    [MyDependency(typeof(MyHierarchyComponent))]
    public class EquiPhysicsTweakerComponent : MyEntityComponent
    {
        [Automatic]
        private readonly MyHierarchyComponent _parentComponent = null;

        private MyHierarchyComponent _parent = null;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _parentComponent.ParentChanged += ParentChanged;
            ParentChanged(_parentComponent, _parent, _parentComponent.Parent);
        }

        public override void OnRemovedFromScene()
        {
            _parentComponent.ParentChanged -= ParentChanged;
            ParentChanged(_parentComponent, _parent, null);
            base.OnRemovedFromScene();
        }

        private void ParentChanged(MyHierarchyComponent target, MyHierarchyComponent oldParent, MyHierarchyComponent newParent)
        {
            if (_parent == newParent)
                return;
            if (_parent != null)
            {
                var phys = _parent.Container.Get<MyPhysicsComponentBase>();
                if (phys != null)
                {
                    phys.OnPhysicsChanged -= OnPhysicsChanged;
                    phys.LinearDamping = DefaultLinearDamping;
                    phys.AngularDamping = DefaultAngularDamping;
                }
            }

            _parent = newParent;
            if (_parent != null)
            {
                var phys = _parent.Container.Get<MyPhysicsComponentBase>();
                if (phys != null)
                {
                    phys.OnPhysicsChanged += OnPhysicsChanged;
                    OnPhysicsChanged(phys);
                }
            }
        }

        private const float DefaultLinearDamping = 0.1f;
        private const float DefaultAngularDamping = 0.05f;

        private void OnPhysicsChanged(MyPhysicsComponentBase obj)
        {
            if (!obj.Enabled)
                return;
            var objectMass = obj.Mass;
            var myMass = objectMass;
            if (objectMass <= 0)
                objectMass = 1f;
            var block = Container.Get<MyBlockComponent>();
            if (block != null)
            {
                var blockDef = MyDefinitionManager.Get<MyBlockDefinition>(block.DefinitionId);
                if (blockDef != null)
                    myMass = blockDef.Mass;
            }

            var ratio = MathHelper.Clamp((Definition.MassInfluence ?? myMass) / objectMass, 0, 1);

            if (Definition.LinearDamping.HasValue)
                obj.LinearDamping = MathHelper.Lerp(DefaultLinearDamping, Definition.LinearDamping.Value, ratio);

            if (Definition.AngularDamping.HasValue)
                obj.AngularDamping = MathHelper.Lerp(DefaultAngularDamping, Definition.AngularDamping.Value, ratio);
        }

        public EquiPhysicsTweakerComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiPhysicsTweakerComponentDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPhysicsTweakerComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiPhysicsTweakerComponentDefinition))]
    public class EquiPhysicsTweakerComponentDefinition : MyEntityComponentDefinition
    {
        public float? LinearDamping { get; private set; }
        public float? AngularDamping { get; private set; }

        public float? MassInfluence { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiPhysicsTweakerComponentDefinition) def;
            LinearDamping = ob.LinearDamping;
            AngularDamping = ob.AngularDamping;
            MassInfluence = ob.MassInfluence;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPhysicsTweakerComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float? LinearDamping;
        public float? AngularDamping;
        public float? MassInfluence;
    }
}