using System.Xml.Serialization;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Inventory.Constraints;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;
using VRageMath;

namespace Equinox76561198048419394.Core.Mirz.Constraints
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzItemSizeConstraint : MyObjectBuilder_InventoryConstraint
    {
        /// <summary>
        /// Size in meters
        /// </summary>
        public Vector3 Size;

        [XmlAttribute]
        public MrzEqualityResult Equality;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzItemSizeConstraint))]
    public class MrzItemSizeConstraint : MyInventoryConstraint
    {
        private MrzEqualityResult _equality;
        private Vector3 _size;
        private float _blockSize;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            var ob = (MyObjectBuilder_MrzItemSizeConstraint)builder;

            _equality = ob.Equality;
            _size = ob.Size;

            // we assume all blocks that go into inventory will be small blocks.
            _blockSize = MyCubeGridDefinitions.GetCubeSize(MyCubeSize.Small);
        }

        public override MyObjectBuilder_DefinitionBase GetObjectBuilder()
        {
            return new MyObjectBuilder_MrzItemSizeConstraint()
            {
                Equality = _equality,
                Size = _size,
            };
        }

        public override bool Check(MyDefinitionId itemId)
        {
            var def = MyDefinitionManager.Get<MyInventoryItemDefinition>(itemId);
            if (def == null)
                return false;

            var itemSize = def.Size;
            if (def is MyBlockItemDefinition)
            {
                MrzUtils.ShowNotificationDebug($"Original mass: {itemSize}");
                itemSize *= _blockSize;
                MrzUtils.ShowNotificationDebug($"Corrected mass: {itemSize}");
            }

            switch (_equality)
            {
                case MrzEqualityResult.Equal:
                    return itemSize == _size;
                case MrzEqualityResult.NotEqual:
                    return itemSize != _size;
                case MrzEqualityResult.Less:
                    return itemSize.IsLessThan(_size);
                case MrzEqualityResult.Greater:
                    return itemSize.IsGreaterThan(_size);
                case MrzEqualityResult.LessOrEqual:
                    return itemSize.IsLessOrEqual(_size);
                case MrzEqualityResult.GreateOrEqual:
                    return itemSize.IsGreaterOrEqual(_size);
                default:
                    return false;
            }
        }

        public override MyInventoryConstraint Clone()
        {
            throw new System.NotImplementedException();
        }
    }
}
