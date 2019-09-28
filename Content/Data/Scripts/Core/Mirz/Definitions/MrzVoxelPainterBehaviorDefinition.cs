using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Definitions.Equipment;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    public enum VoxelPainterTargetState
    {
        Ok,
        Invalid,
        Denied
    }

    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzVoxelPainterBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        public struct VoxelPainterTargetStateDef
        {
            [XmlAttribute]
            public string Icon;

            [XmlAttribute]
            public string Message;
        }
        
        public VoxelPainterTargetStateDef? TargetOk;
        public VoxelPainterTargetStateDef? TargetDenied;
        public VoxelPainterTargetStateDef? TargetInvalid;
        public string WrongToolMessage;

        public SerializableDefinitionId? Mining;
        public string PaintMaterial;

        public float? PaintRadius;
        public float? PaintDepth;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzVoxelPainterBehaviorDefinition))]
    public class MrzVoxelPainterBehaviorDefinition : MyToolBehaviorDefinition
    {
        public struct VoxelPainterTargetStateInfo
        {
            public MyStringHash Icon;
            public MyStringId Message;

            public static implicit operator VoxelPainterTargetStateInfo(
                MyObjectBuilder_MrzVoxelPainterBehaviorDefinition.VoxelPainterTargetStateDef def)
            {
                return new VoxelPainterTargetStateInfo()
                {
                    Icon = MyStringHash.GetOrCompute(def.Icon),
                    Message = MyStringId.GetOrCompute(def.Message),
                };
            }
        }

        public readonly Dictionary<VoxelPainterTargetState, VoxelPainterTargetStateInfo> TargetMessages = new Dictionary<VoxelPainterTargetState, VoxelPainterTargetStateInfo>();

        public MyDefinitionId Mining;
        public MyStringHash PaintMaterial;

        public string WrongToolMessage;

        public float PaintRadius;
        public float PaintDepth;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = (MyObjectBuilder_MrzVoxelPainterBehaviorDefinition)builder;

            TargetMessages[VoxelPainterTargetState.Ok] = ob.TargetOk ?? new MyObjectBuilder_MrzVoxelPainterBehaviorDefinition.VoxelPainterTargetStateDef()
            {
                Icon = "VoxelPaint",
                Message = "Press [KEY:PrimaryClick] to paint voxel here.",
            };

            TargetMessages[VoxelPainterTargetState.Denied] = ob.TargetOk ?? new MyObjectBuilder_MrzVoxelPainterBehaviorDefinition.VoxelPainterTargetStateDef()
            {
                Icon = "VoxelPaint_denied",
                Message = "You do not have permission to paint voxel here.",
            };

            TargetMessages[VoxelPainterTargetState.Invalid] = ob.TargetOk ?? new MyObjectBuilder_MrzVoxelPainterBehaviorDefinition.VoxelPainterTargetStateDef()
            {
                Icon = "VoxelPaint_no_permission",
                Message = "You cannot paint voxel here.",
            };
            
            Mining = ob.Mining ?? new MyDefinitionId();
            PaintMaterial = MyStringHash.GetOrCompute(ob.PaintMaterial);
            WrongToolMessage = !string.IsNullOrEmpty(WrongToolMessage) ? WrongToolMessage : "{0} is not suitable to change these voxels.";
            PaintRadius = ob.PaintRadius ?? 5f;
            PaintDepth = ob.PaintDepth ?? 0.5f;
        }
    }
}
