using System;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage.Logging;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public readonly struct MaterialEdit : IEquatable<MaterialEdit>
    {
        public static readonly string TechniqueKey = "__technique";

        public readonly ModeEnum Mode;
        public readonly string Key;
        public readonly string Value;
        public readonly Hashing.Hash128 Hash;

        public MaterialEdit(ModeEnum mode, string key, string value)
        {
            Mode = mode;
            Key = string.Intern(key);
            Value = value;
            var builder = Hashing.Builder();
            builder.Add(Key);
            builder.Add(Value);
            builder.Add((byte) Mode);
            Hash = builder.Build();
        }

        public enum ModeEnum
        {
            Texture,
            UserData,
            FieldKey
        }

        public bool Equals(MaterialEdit other)
        {
            return Mode == other.Mode && ReferenceEquals(Key, other.Key);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialEdit other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int) Mode;
                hashCode = (hashCode * 397) ^ (Key != null ? Key.GetHashCode() : 0);
                return hashCode;
            }
        }

        public void ApplyTo(MyMaterialDescriptor mtl)
        {
            var technique = mtl.TechniqueEnum;
            var isDecal = technique == MyMeshDrawTechnique.HOLO
                          || technique == MyMeshDrawTechnique.DECAL
                          || technique == MyMeshDrawTechnique.FOLIAGE
                          || technique == MyMeshDrawTechnique.DECAL_CUTOUT
                          || technique == MyMeshDrawTechnique.DECAL_NOPREMULT;
            switch (Mode)
            {
                case ModeEnum.Texture:
                    if (Key == "AddMapsTexture" && isDecal)
                    {
                        MyLog.Default.Warning($"Edit to material {mtl.MaterialName} with a decal type technique can not have an ADD texture");
                        break;
                    }
                    mtl.Textures[Key] = Value;
                    break;
                case ModeEnum.UserData:
                    mtl.UserData[Key] = Value;
                    break;
                case ModeEnum.FieldKey:
                    if (Key == TechniqueKey)
                        mtl.Technique = Value;
                    break;
            }
        }
    }
}