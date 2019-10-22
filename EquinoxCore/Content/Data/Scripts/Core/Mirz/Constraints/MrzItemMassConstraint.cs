using System;
using System.Xml.Serialization;
using Sandbox.Game.Entities.Inventory.Constraints;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;

namespace Equinox76561198048419394.Core.Mirz.Constraints
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzItemMassConstraint : MyObjectBuilder_InventoryConstraint
    {
        /// <summary>
        /// Mass in kilograms
        /// </summary>
        [XmlAttribute]
        public float Mass;

        [XmlAttribute]
        public MrzEqualityResult Equality;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzItemMassConstraint))]
    public class MrzItemMassConstraint : MyInventoryConstraint
    {
        private MrzEqualityResult _equality;
        private float _mass;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            var ob = (MyObjectBuilder_MrzItemMassConstraint)builder;

            _equality = ob.Equality;
            _mass = ob.Mass;
        }

        public override MyObjectBuilder_DefinitionBase GetObjectBuilder()
        {
            return new MyObjectBuilder_MrzItemMassConstraint()
            {
                Equality = _equality,
                Mass = _mass,
            };
        }

        public override bool Check(MyDefinitionId itemId)
        {
            var def = MyDefinitionManager.Get<MyInventoryItemDefinition>(itemId);
            if (def == null)
                return false;

            switch (_equality)
            {
                case MrzEqualityResult.Equal:
                    return Math.Abs(def.Mass - _mass) < MrzUtils.Epsilon;
                case MrzEqualityResult.NotEqual:
                    return Math.Abs(def.Mass - _mass) > MrzUtils.Epsilon;
                case MrzEqualityResult.Less:
                    return def.Mass < _mass;
                case MrzEqualityResult.Greater:
                    return def.Mass > _mass;
                case MrzEqualityResult.LessOrEqual:
                    return def.Mass <= _mass;
                case MrzEqualityResult.GreateOrEqual:
                    return def.Mass >= _mass;
                default:
                    return false;
            }
        }

        public override MyInventoryConstraint Clone()
        {
            throw new NotImplementedException();
        }
    }
}
