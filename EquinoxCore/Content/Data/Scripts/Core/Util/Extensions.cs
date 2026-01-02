using System.Collections.Generic;
using Medieval.Definitions.GameSystems.Building;
using Medieval.Entities.UseObject;
using Medieval.GameSystems;
using Medieval.GUI.ContextMenu;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components.Physics;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Logging;
using VRage.Network;
using VRage.Utils;
using VRageMath;
using VRageRender.Animations;
using VRageRender.Import;
using VRage.Session;

namespace Equinox76561198048419394.Core.Util
{
    public static class Extensions
    {
        public static IMyUtilities ApiUtilities => MyAPIUtilities.Static;
        
        public static void SetTransform(this MyCharacterBone bone, Matrix matrix)
        {
            var q = Quaternion.CreateFromRotationMatrix(matrix);
            var t = matrix.Translation;
            bone.SetTransform(ref t, ref q);
        }

        public static void EnsureCapacity<T>(this List<T> list, int capacity)
        {
            if (list.Capacity < capacity)
                list.Capacity = MathHelper.GetNearestBiggerPowerOfTwo(capacity);
        }

        public static NamedLogger GetLogger(this MyContextMenuController ctl) => new NamedLogger(ctl.GetType().Name, MyLog.Default);

        public static MyPhysicsComponentBase ParentedPhysics(this MyEntity e)
        {
            while (e != null)
            {
                if (e.Physics != null)
                    return e.Physics;
                e = e.Parent;
            }

            return null;
        }

        public static string ReplaceAll(this string input, char[] any, char with)
        {
            char[] result = null;
            for (var i = 0; i < input.Length; i++)
            {
                var match = false;
                for (var j = 0; j < any.Length; j++)
                {
                    var k = any[j];
                    if (k == input[i])
                    {
                        match = true;
                        break;
                    }
                }

                if (match)
                {
                    if (result == null)
                    {
                        result = new char[input.Length];
                        input.CopyTo(0, result, 0, i);
                    }

                    result[i] = with;
                }
                else if (result != null)
                {
                    result[i] = input[i];
                }
            }

            return result != null ? new string(result) : input;
        }

        public static string AsAlphaNumeric(this string input, char replacement = '_')
        {
            char[] result = null;
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (!char.IsLetterOrDigit(c))
                {
                    if (result == null)
                    {
                        result = new char[input.Length];
                        input.CopyTo(0, result, 0, i);
                    }

                    result[i] = replacement;
                }
                else if (result != null)
                {
                    result[i] = c;
                }
            }

            return result != null ? new string(result) : input;
        }

        public static bool IsAdminModeEnabled(this IMyPlayer player) => MyAPIGateway.Session.IsAdminModeEnabled(player.IdentityId);

        public static bool IsCreative(this IMyPlayer player) => MyAPIGateway.Session.CreativeMode || player.IsAdminModeEnabled();

        public static bool IsCreative(this MyToolBehaviorBase behavior)
        {
            var player = MyAPIGateway.Players?.GetPlayerControllingEntity(behavior.Holder);
            return player != null && player.IsCreative();
        }

        public static bool IsServerDecider(this IMySession session)
        {
            return MyMultiplayerModApi.Static.IsServer;
        }

        public const float DefaultBuildingDistanceLimit = 10;
        public static float BuildingDistanceLimit(this IMyPlayer player)
        {
            var gridPlacer = MyDefinitionManager.Get<MyGridPlacerDefinition>("Default");

            return player.IsCreative()
                ? gridPlacer?.MaxBuildingDistanceCreative ?? 20
                : gridPlacer?.MaxBuildingDistanceSurvival ?? 10;
        }

        public static bool HasPermission(this IMyPlayer player, Vector3D location, MyStringId id)
        {
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, location, id);
        }

        public static bool IsLocallyControlled(this MyToolBehaviorBase behavior) => MySession.Static.PlayerEntity == behavior.Holder;

        public static bool IsAdmin(this MyToolBehaviorBase behavior) =>
            MyAPIGateway.Players?.GetPlayerControllingEntity(behavior.Holder)?.IsAdminModeEnabled() ?? false;

        public static MyHandItemBehaviorBase GetHeldBehavior(this MyEntity entity)
        {
            return entity.Components.Get<MyCharacterHandItemsComponent>()?.GetBehavior<MyHandItemBehaviorBase>();
        }

        public static bool TryGetSendersHeldBehavior<T>(this MyEventContext context, out T behavior) where T : MyHandItemBehaviorBase
        {
            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(context.Sender.Value, 0));
            behavior = player?.ControlledEntity?.GetHeldBehavior() as T;
            return behavior != null;
        }

        private static MyUseObjectClaimBlock _localizedInteractionHelper;

        public static string GetLocalizedInteractionButton(this IMyInput input)
        {
            var helper = _localizedInteractionHelper ??
                         (_localizedInteractionHelper = new MyUseObjectClaimBlock(new MyEntity(), new MyModelDummy(), 0));
            var args = helper.GetActionInfo(UseActionEnum.OpenTerminal).FormatParams;
            if (args != null && args.Length == 2 && args[0] != null)
                return args[0].ToString();
            return "F";
        }
    }
}