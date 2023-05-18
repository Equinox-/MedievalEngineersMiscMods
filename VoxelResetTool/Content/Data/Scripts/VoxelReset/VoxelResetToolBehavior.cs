using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.VoxelReset
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiVoxelResetToolBehaviorDefinition))]
    public class EquiVoxelResetToolBehavior : MyToolBehaviorBase
    {
        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        private bool IsAdmin
        {
            get
            {
                var player = MyAPIGateway.Players?.GetPlayerControllingEntity(Holder);
                return player != null && MyAPIGateway.Session.IsAdminModeEnabled(player.IdentityId);
            }
        }

        protected override bool ValidateTarget() => IsAdmin;

        protected override bool Start(MyHandItemActionEnum action) => true;

        private Vector3D Position
        {
            get
            {
                var candidate = MyCameraComponent.ActiveCamera?.GetPosition() ?? Holder.GetPosition();
                // If the target is close to the camera just reset the target.
                if (Target.Entity != null && Vector3D.DistanceSquared(Target.Position, candidate) < 5)
                    return Target.Position;
                return candidate;
            }
        }

        protected override void Hit()
        {
            if (!IsLocallyControlled || !IsAdmin)
                return;
            var system = MySession.Static?.Components.Get<VoxelResetSystem>();
            if (system == null)
                return;
            var pos = Position;
            var voxel = MyGamePruningStructureSandbox.GetClosestPlanet(pos);
            if (voxel == null)
                return;
            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Primary:
                    if (Modified)
                        system.ShowSizes ^= true;
                    system.RequestShow(voxel, pos);
                    break;
                case MyHandItemActionEnum.Secondary:
                    system.RequestResetVoxel(voxel, pos);
                    break;
                case MyHandItemActionEnum.Tertiary:
                case MyHandItemActionEnum.None:
                default:
                    return;
            }
        }

        public override void Deactivate()
        {
            MySession.Static?.Components.Get<VoxelResetSystem>()?.RequestHide();
            base.Deactivate();
        }

        public override IEnumerable<string> GetHintTexts()
        {
            if (!IsAdmin)
            {
                yield return "Must be a Medieval Master to reset voxels";
                yield break;
            }

            var pos = Position;
            var voxel = MyGamePruningStructureSandbox.GetClosestPlanet(pos);
            if (voxel == null)
            {
                yield return "Not usable when far from voxels";
                yield break;
            }

            yield return "Press [KEY:ToolPrimary] to show modified chunks";
            var modified = false;
            var okay = MySession.Static?.Components.Get<VoxelResetSystem>()?.TryGetChunkState(voxel, pos, out modified) ?? false;
            if (okay && modified)
                yield return "Press [KEY:ToolSecondary] to reset chunk";
            else if (okay)
                yield return "Chunk is not modified";
            else
                yield return "Chunk state is not known (Press [KEY:ToolPrimary] to refresh)";
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiVoxelResetToolBehaviorDefinition))]
    public class EquiVoxelResetToolBehaviorDefinition : MyToolBehaviorDefinition
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiVoxelResetToolBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
    }
}