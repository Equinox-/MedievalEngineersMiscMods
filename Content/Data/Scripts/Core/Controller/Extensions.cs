using System.Collections.Generic;
using Medieval.Entities.UseObject;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
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
            var curr = ug.Interface;
            try
            {
                UgIdentifier id;
                lock (UseObjectIdentifiers)
                    id = UseObjectIdentifiers.Count > 0 ? UseObjectIdentifiers.Pop() : new UgIdentifier();
                id.Dummy = null;
                ug.Interface = id;
                ug.GetActionInfo(UseActionEnum.None);
                var resultName = id.Dummy;
                lock (UseObjectIdentifiers)
                    UseObjectIdentifiers.Push(id);
                return resultName;
            }
            finally
            {
                ug.Interface = curr;
            }
        }

        #endregion

        #region Get Target Info for Character

        private class ShimGetDetectorTarget : MyToolBehaviorBase
        {
            protected override bool ValidateTarget()
            {
                return false;
            }

            protected override bool Start(MyHandItemActionEnum action)
            {
                return false;
            }

            protected override void Hit()
            {
            }

            public MyDetectedEntityProperties Detected => Target;
        }

        private static readonly Stack<ShimGetDetectorTarget> ShimDetectorTarget = new Stack<ShimGetDetectorTarget>();


        public static MyDetectedEntityProperties GetDetectedEntity(MyEntity holder)
        {
            ShimGetDetectorTarget id;
            lock (ShimDetectorTarget)
                id = ShimDetectorTarget.Count > 0 ? ShimDetectorTarget.Pop() : new ShimGetDetectorTarget();
            id.Init(holder, null, null);
            id.SetTarget();
            var detected = id.Detected;
            lock (ShimDetectorTarget)
                ShimDetectorTarget.Push(id);
            return detected;
        }

        #endregion
    }
}