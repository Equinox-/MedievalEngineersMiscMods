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
    public class MyObjectBuilder_MrzItemVolumeConstraint : MyObjectBuilder_InventoryConstraint
    {
        [XmlAttribute]
        public float Volume;

        [XmlAttribute]
        public MrzEqualityResult Equality;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzItemVolumeConstraint))]
    public class MrzItemVolumeConstraint : MyInventoryConstraint
    {
        private MrzEqualityResult _equality;
        private float _volume;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            var ob = (MyObjectBuilder_MrzItemVolumeConstraint)builder;

            _equality = ob.Equality;
            _volume = ob.Volume;
        }

        public override MyObjectBuilder_DefinitionBase GetObjectBuilder()
        {
            return new MyObjectBuilder_MrzItemVolumeConstraint()
            {
                Equality = _equality,
                Volume = _volume,
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
                    return Math.Abs(def.Volume - _volume) < MrzUtils.Epsilon;
                case MrzEqualityResult.NotEqual:
                    return Math.Abs(def.Volume - _volume) > MrzUtils.Epsilon;
                case MrzEqualityResult.Less:
                    return def.Volume < _volume;
                case MrzEqualityResult.Greater:
                    return def.Volume > _volume;
                case MrzEqualityResult.LessOrEqual:
                    return def.Volume <= _volume;
                case MrzEqualityResult.GreateOrEqual:
                    return def.Volume >= _volume;
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
