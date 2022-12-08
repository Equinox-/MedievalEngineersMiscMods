using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.Core.Util.EqMath;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.DataSources;
using Sandbox.Game.Entities.Character.Components;
using VRage;
using VRage.Collections;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Controller
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_PlayerAttachmentSlotContext))]
    public class PlayerAttachmentSlotContext : MyContextMenuContext
    {
        public static readonly MyStringId Position = MyStringId.GetOrCompute("Position");
        public static readonly MyStringId Rotation = MyStringId.GetOrCompute("Rotation");
        public static readonly MyStringId Lean = MyStringId.GetOrCompute("Lean");
        public static readonly MyStringId Animation = MyStringId.GetOrCompute("Animation");

        public static readonly MyStringId PositionEnabled = MyStringId.GetOrCompute("PositionEnabled");
        public static readonly MyStringId RotationEnabled = MyStringId.GetOrCompute("RotationEnabled");
        public static readonly MyStringId AnimationEnabled = MyStringId.GetOrCompute("AnimationEnabled");

        private EquiEntityControllerComponent _controller;
        private EquiPlayerAttachmentComponent.Slot _slot;

        public override void Init(object[] contextParams)
        {
            _controller = (EquiEntityControllerComponent)contextParams[0];
            _slot = (EquiPlayerAttachmentComponent.Slot)contextParams[1];
            m_dataSources.Add(PositionEnabled, new SimpleConditionDataSource(() => _slot.Definition.MinLinearShift != _slot.Definition.MaxLinearShift));
            m_dataSources.Add(Position, new Vector3BoundedArrayDataSource(
                _slot.Definition.MinLinearShift,
                Vector3.Zero,
                _slot.Definition.MaxLinearShift,
                () => _slot.LinearShift,
                val => _controller.RequestUpdateShift(linearShift: val)));

            m_dataSources.Add(RotationEnabled, new SimpleConditionDataSource(() =>
                _slot.Definition.MinAngularShift != _slot.Definition.MaxAngularShift ||  _slot.Definition.MinLean < _slot.Definition.MaxLean));
            m_dataSources.Add(Rotation, new Vector3BoundedArrayDataSource(
                MiscMath.ToDegrees(_slot.Definition.MinAngularShift),
                Vector3.Zero,
                MiscMath.ToDegrees(_slot.Definition.MaxAngularShift),
                () => MiscMath.ToDegrees(_slot.AngularShift),
                val => _controller.RequestUpdateShift(angularShift: MathHelper.ToRadians(val))));

            m_dataSources.Add(Lean, new SimpleBoundedDataSource<float>(
                MathHelper.ToDegrees(_slot.Definition.MinLean),
                0,
                MathHelper.ToDegrees(_slot.Definition.MaxLean),
                () => MathHelper.ToDegrees(_slot.LeanAngle),
                val => _controller.RequestUpdateShift(leanAngle: MathHelper.ToRadians(val))
                ));

            m_dataSources.Add(AnimationEnabled, new SimpleConditionDataSource(() => _slot.Definition.AnimationCount > 1));
            m_dataSources.Add(Animation, new AnimationDataSource(this));
        }

        public void ResetPosition() => _controller.RequestUpdateShift(Vector3.Zero, _slot.AngularShift);

        public void ResetRotation() => _controller.RequestUpdateShift(_slot.LinearShift, Vector3.Zero);

        public void ResetAnimation() => _controller.RequestResetPose();

        public void Pickup()
        {
            var pickup = _controller.Container.Get<MyCharacterPickupComponent>();
            pickup?.QuickPickUp(_slot.Controllable.Entity);
        }

        private sealed class AnimationDataSource : IMyListboxDataSource
        {
            private readonly PlayerAttachmentSlotContext _context;

            public AnimationDataSource(PlayerAttachmentSlotContext context) => _context = context;

            public void Close()
            {
            }

            private ListReader<EquiPlayerAttachmentComponentDefinition.AnimationDesc> Animations => _context._slot.Definition.Animations;

            public void GetItems(List<MyTuple<MyStringId, string>> output)
            {
                output.Add(new MyTuple<MyStringId, string>(MyStringId.NullOrEmpty, "Default"));
                foreach (var item in Animations)
                    output.Add(new MyTuple<MyStringId, string>(MyStringId.NullOrEmpty, item.DisplayName));
            }

            public void GetItemSelection(List<bool> output)
            {
                var forced = _context._slot.ForceAnimationId;
                var index = forced.HasValue ? forced.Value + 1 : 0;
                for (var i = 0; i <= Animations.Count; i++)
                    output.Add(i == index);
            }

            public void SetItemSelection(List<bool> input)
            {
                if (input[0])
                {
                    _context._controller.RequestResetPose();
                    return;
                }
                for (var i = 0; i<input.Count; i++)
                    if (input[i])
                    {
                        _context._controller.RequestUpdatePose(i - 1);
                        return;
                    }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PlayerAttachmentSlotContext : MyObjectBuilder_Base
    {
    }
}