using System.Collections.Generic;
using Medieval.Entities.UseObject;
using Medieval.GameSystems;
using Medieval.GameSystems.Tools;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components.Physics;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Network;
using VRage.Utils;
using VRageMath;
using VRageRender.Animations;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Util
{
    public static class Extensions
    {
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

        public static bool IsCreative(this IMyPlayer player)
        {
            return MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.IsAdminModeEnabled(player.IdentityId);
        }

        public static bool IsServerDecider(this IMySession session)
        {
            return MyMultiplayerModApi.Static.IsServer;
        }

        public static bool HasPermission(this IMyPlayer player, Vector3D location, MyStringId id)
        {
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, location, id);
        }

        public static bool TryGetSendersHeldBehavior<T>(this MyEventContext context, out T behavior) where T : MyHandItemBehaviorBase
        {
            behavior = default;
            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(context.Sender.Value, 0));
            var playerEntity = player?.ControlledEntity;
            if (playerEntity == null)
                return false;
            if (!playerEntity.Components.TryGet(out MyCharacterHandItemsComponent handItems))
                return false;
            behavior = handItems.GetBehavior<T>();
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