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
    }
}