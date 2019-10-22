using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Mirz.Definitions;
using Medieval.Constants;
using Medieval.GameSystems;
using Sandbox.Definitions;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Definitions;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.GUI.Crosshair;
using VRage.Network;
using VRage.Session;
using VRage.Systems;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.Core.Mirz.Behaviors
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_MrzVoxelPainterBehaviorDefinition))]
    [StaticEventOwner]
    public partial class MrzVoxelPainterBehavior : MyToolBehaviorBase
    {
        #region Inner Types 

        #endregion

        #region Static

        private static readonly IMyHudNotification _validationDebugNotification = MrzUtils.CreateNotificationDebug("", 100);
        private static readonly MyStorageData StorageData = new MyStorageData();

        #endregion

        #region Private

        private byte _fillMaterial;

        private bool _targetStateIcon = false;
        private VoxelPainterTargetState _targetState = VoxelPainterTargetState.Ok;
        private MyDetectedEntityProperties _targetProperties;

        private IMyHudNotification _wrongToolMessage;
        private MyVoxelMiningDefinition _mining;
        private MyInventoryBase _inventory;

        #endregion

        #region Properties

        protected new MrzVoxelPainterBehaviorDefinition Definition => base.Definition as MrzVoxelPainterBehaviorDefinition;

        #endregion

        #region Lifecycle

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);

            var def = (MrzVoxelPainterBehaviorDefinition)definition;

            _mining = MyDefinitionManager.Get<MyVoxelMiningDefinition>(def.Mining);
            for (var i = 0; i < _filter.Length; i++)
                _filter[i] = _mining.MiningEntries.ContainsKey(i);

            var material = MyDefinitionManager.Get<MyVoxelMaterialDefinition>(def.PaintMaterial);
            _fillMaterial = material?.Index ?? (byte)0;

            _inventory = holder.Get<MyInventoryBase>(MyCharacterConstants.MainInventory);
            _wrongToolMessage = MrzUtils.CreateNotification(string.Format(def.WrongToolMessage, Item.GetDefinition().DisplayNameText), MrzUtils.NotificationType.Error);
        }

        #endregion

        #region Actions

        protected override bool ValidateTarget()
        {
            return Target.Entity is MyVoxelBase;
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            if (action != MyHandItemActionEnum.Primary)
                return false;

            if (Target.Entity == null)
                return false;

            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            return player != null && DeterminePaintPermission(player);
        }

        protected override void Hit()
        {
            if (Target.Entity == null || !(Target.Entity is MyVoxelBase))
                return;

            MrzUtils.ShowNotificationDebug($"VoxelPainter::Hit {Target.Entity}");

            try
            {
                var voxelBase = Target.Entity as MyVoxelBase;
                voxelBase = voxelBase.RootVoxel; // getting the planet
                if (voxelBase.MarkedForClose)
                    return;

                MyVoxelMaterialDefinition voxelMaterial = GetVoxelMaterial(voxelBase, Target.Position);

                MyVoxelMiningDefinition.MiningEntry miningDef;
                if (voxelMaterial != null && _mining.MiningEntries.TryGetValue(voxelMaterial.Index, out miningDef))
                {
                    UpdateDurability(-1);

                    Vector3 pos;
                    CalculateCoords(voxelBase, Target.Position,
                        out pos);
                    
                    MrzUtils.ShowNotificationDebug($"VoxelPainter::Request Paint - Position:{Target.Position} Radius:{Definition.PaintRadius} Material:{_fillMaterial}", 1000);

                    if (MrzUtils.IsServer)
                    {
                        var plane = new Plane(pos, Target.Normal);
                        plane.D = Definition.PaintDepth;
                        DoOperationServer(voxelBase, pos, Definition.PaintRadius, plane);
                    }
                    //MyAPIGateway.Multiplayer.RaiseStaticEvent(x => DoOperationServer, OperationServer.CreateCut(
                    //    Holder.EntityId, voxelBase.EntityId, pos, Modified
                    //));
                }
                else
                {
                    if (Holder == MySession.Static.PlayerEntity)
                        _wrongToolMessage.Show();
                }
            }
            catch (Exception e)
            {
                MrzUtils.ShowNotificationDebug(e.Message);
            }
        }

        #endregion

        #region Helpers

        private bool DeterminePaintPermission(IMyPlayer player)
        {
            if (MyAreaPermissionSystem.Static == null)
                return true;

            if (player == null)
                return false;

            return MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position,
                MyPermissionsConstants.Mining);
        }

        private static void CalculateCoords(MyVoxelBase target, Vector3D targetPosition, out Vector3 localPos)
        {
            // Inverse matrix of the object
            var invMatrix = target.PositionComp.WorldMatrixInvScaled;

            // Local position of fill in.
            Vector3D local;
            Vector3D.TransformNoProjection(ref targetPosition, ref invMatrix, out local);
            localPos = (Vector3)(local + target.SizeInMetresHalf);
        }

        private MyVoxelMaterialDefinition GetVoxelMaterial(MyVoxelBase voxelBase, Vector3D hitPos)
        {
            if (voxelBase.Storage == null || voxelBase.PositionComp == null)
                return null;

            Vector3D localPosition;
            var wm = voxelBase.PositionComp.WorldMatrixInvScaled;
            Vector3D.TransformNoProjection(ref hitPos, ref wm, out localPosition);

            var voxelPosition = voxelBase.StorageMin + new Vector3I(localPosition) + (voxelBase.Size >> 1);

            StorageData.Resize(Vector3I.One);
            voxelBase.Storage.ReadRange(StorageData, MyStorageDataTypeFlags.Material, 0, voxelPosition, voxelPosition);

            byte materialIndex = StorageData.Material(0);

            return MyVoxelMaterialDefinition.Get(materialIndex);
        }

        #endregion

        #region Crosshair Icon

        protected override void OnTargetEntityChanged(MyDetectedEntityProperties properties)
        {
            Target = properties;

            var voxel = properties.Entity as MyVoxelBase;
            if (voxel != null)
            {
                _targetStateIcon = true;
                MyUpdateComponent.Static.AddForUpdate(ValidateVoxel, 100);
                return;
            }

            _targetStateIcon = false;
            MyUpdateComponent.Static.RemoveFromUpdate(ValidateVoxel);
        }

        [Update(20)]
        private void ValidateVoxel(long time)
        {
            if (!_targetStateIcon)
                return;

            var voxel = Target.Entity as MyVoxelBase;
            if (voxel == null || voxel.MarkedForClose)
                return;

            //voxel = voxel.RootVoxel;
            //if (voxel == null || voxel.MarkedForClose)
            //    return;

            _targetState = VoxelPainterTargetState.Ok;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
            {
                _validationDebugNotification.Text = "VoxelPainter::Player == null";
                _validationDebugNotification.ShowDebug();
                _targetState = VoxelPainterTargetState.Invalid;
                return;
            }

            if (!DeterminePaintPermission(player))
            {
                _validationDebugNotification.Text = "VoxelPainter::No Permission";
                _validationDebugNotification.ShowDebug();
                _targetState = VoxelPainterTargetState.Denied;
                return;
            }

            var voxelMaterial = GetVoxelMaterial(voxel, Target.Position);
            if (voxelMaterial == null || _mining == null || !_mining.MiningEntries.ContainsKey(voxelMaterial.Index))
            {
                _validationDebugNotification.Text = "VoxelPainter::Invalid Voxel";
                _validationDebugNotification.ShowDebug();
                _targetState = VoxelPainterTargetState.Invalid;
                return;
            }

            _validationDebugNotification.Text = "VoxelPainter::Valid Voxel";
            _validationDebugNotification.ShowDebug();
            //MrzUtils.ShowNotificationDebug("VoxelPainter: Valid Voxel", 100);
        }

        //Setting crosshair info does not seem to be something avialable to modapi yet

        public override IEnumerable<MyCrosshairIconInfo> GetIconsStates()
        {
            return !_targetStateIcon ? base.GetIconsStates() : GetIconsInternal();
        }

        private IEnumerable<MyCrosshairIconInfo> GetIconsInternal()
        {
            if (!_targetStateIcon || Definition?.TargetMessages == null)
                yield break;

            MrzVoxelPainterBehaviorDefinition.VoxelPainterTargetStateInfo targetInfo;
            if (Definition.TargetMessages.TryGetValue(_targetState, out targetInfo))
                yield return new MyCrosshairIconInfo(targetInfo.Icon);
        }

        public override IEnumerable<string> GetHintTexts()
        {
            if (!_targetStateIcon || Definition?.TargetMessages == null)
                yield break;

            MrzVoxelPainterBehaviorDefinition.VoxelPainterTargetStateInfo targetInfo;
            if (Definition.TargetMessages.TryGetValue(_targetState, out targetInfo))
                yield return MyTexts.GetString(targetInfo.Message);
        }

        #endregion
    }
}
