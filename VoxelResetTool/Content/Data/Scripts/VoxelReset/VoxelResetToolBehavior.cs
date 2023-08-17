using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Game.Definitions;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.VoxelReset
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiVoxelResetToolBehaviorDefinition))]
    public class EquiVoxelResetToolBehavior : MyToolBehaviorBase
    {
        private const int ModifiedResetChunksHalfExtent = 2;

        private bool _showSizes;

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

        private int ResetHalfExtent => Modified ? ModifiedResetChunksHalfExtent : 0;

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
                        _showSizes ^= true;
                    system.RequestShow(voxel, pos);
                    break;
                case MyHandItemActionEnum.Secondary:
                    system.RequestResetVoxel(voxel, pos, ResetHalfExtent);
                    break;
                case MyHandItemActionEnum.Tertiary:
                case MyHandItemActionEnum.None:
                default:
                    return;
            }
        }

        public override void Activate()
        {
            base.Activate();
            if (IsLocallyControlled)
                Scene.Scheduler.AddFixedUpdate(Render);
        }

        public override void Deactivate()
        {
            Scene.Scheduler.RemoveFixedUpdate(Render);
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
            var state = VoxelResetSystem.ResetState.NotModified;
            var leafBox = LeafBox(voxel);
            var okay = MySession.Static?.Components.Get<VoxelResetSystem>()?.TryGetLeafStates(voxel, leafBox, out state) ?? false;
            if (!okay)
            {
                yield return "Chunk state is not known (Press [KEY:ToolPrimary] to refresh)";
                yield break;
            }

            switch (state)
            {
                case VoxelResetSystem.ResetState.NotModified:
                    yield return "Area is not modified";
                    break;
                case VoxelResetSystem.ResetState.Modified:
                    yield return "Press [KEY:ToolSecondary] to reset area";
                    break;
                case VoxelResetSystem.ResetState.ModifiedAndForbidden:
                    yield return "Press [KEY:ToolSecondary] to reset area (will desync!)";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void Render()
        {
            var system = MySession.Static?.Components.Get<VoxelResetSystem>();
            if (system == null)
                return;
            var pos = Position;
            var voxel = MyGamePruningStructureSandbox.GetClosestPlanet(pos);
            if (voxel == null || voxel != system.Voxel)
                return;
            var resetBox = LeafBox(voxel);
            system.Render(_showSizes, resetBox);
        }

        private BoundingBoxI LeafBox(IMyVoxelBase voxel)
        {
            var pos = Position;
            var halfExtent = ResetHalfExtent;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref pos, out var leafCoord);
            leafCoord >>= VoxelResetHooks.LeafLodCount;
            // Min inclusive, max exclusive.
            var minLeaf = leafCoord - halfExtent;
            var maxLeaf = leafCoord + halfExtent;
            return new BoundingBoxI(minLeaf, maxLeaf);
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