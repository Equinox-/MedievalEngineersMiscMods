using System.Collections.Generic;
using Medieval.Entities.UseObject;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using VRage.Components.Physics;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRageMath;
using VRageRender.Animations;

namespace Equinox76561198048419394.Core.Controller
{
    public static class Extensions
    {
        public static void SetTransform(this MyCharacterBone bone, Matrix matrix)
        {
            var q = Quaternion.CreateFromRotationMatrix(matrix);
            var t = matrix.Translation;
            bone.SetTransform(ref t, ref q);
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

        #region Get Dummy Name for Use Object

        private class UgIdentifier : IMyGenericUseObjectInterface
        {
            public string Dummy { get; set; }

            public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
            {
            }

            public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
            {
                Dummy = dummyName;
                return new MyActionDescription();
            }

            public UseActionEnum SupportedActions => UseActionEnum.None;
            public UseActionEnum PrimaryAction  => UseActionEnum.None;
            public UseActionEnum SecondaryAction => UseActionEnum.None;
            public bool ContinuousUsage => false;
        }

        private static readonly Stack<UgIdentifier> UseObjectIdentifiers = new Stack<UgIdentifier>();

        public static string GetDummyName(this MyUseObjectGeneric ug)
        {
            return ug.Dummy?.Name ?? "null_dummy";
        }

        #endregion
    }
}