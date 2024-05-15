using System;
using System.Collections.Generic;
using System.Globalization;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public sealed class KeenMaterial : IEquatable<KeenMaterial>
    {
        public readonly MyMaterialDescriptor Descriptor;

        private const string ColorMetalKey = "ColorMetalTexture";
        private const string NormalGlossKey = "NormalGlossTexture";
        private const string ExtensionKey = "AddMapsTexture";
        private const string AlphaMaskKey = "AlphamaskTexture";

        private string Texture(string key) => Descriptor.Textures.GetValueOrDefault(key, null);

        private void Texture(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
                Descriptor.Textures.Remove(key);
            else
                Descriptor.Textures[key] = value;
        }

        private KeenMaterial(MyMaterialDescriptor descriptor) => Descriptor = descriptor;

        public KeenMaterial(string name) => Descriptor = new MyMaterialDescriptor(name);

        public KeenMaterial Rename(string name) => new KeenMaterial(Descriptor.Clone(name));

        public string Name => Descriptor.MaterialName;

        public MyMeshDrawTechnique Technique
        {
            get => Descriptor.TechniqueEnum;
            set => Descriptor.TechniqueEnum = value;
        }

        public string ColorMetalTexture
        {
            get => Texture(ColorMetalKey);
            set => Texture(ColorMetalKey, value);
        }

        public string NormalGlossTexture
        {
            get => Texture(NormalGlossKey);
            set => Texture(NormalGlossKey, value);
        }

        public string ExtensionTexture
        {
            get => Texture(ExtensionKey);
            set => Texture(ExtensionKey, value);
        }

        public string AlphaMaskTexture
        {
            get => Texture(AlphaMaskKey);
            set => Texture(AlphaMaskKey, value);
        }

        private void UserData<T>(string key, T value)
        {
            Descriptor.UserData[key] = value.ToString();
        }

        private float UserDataFloat(string key)
        {
            if (!Descriptor.UserData.TryGetValue(nameof(WindScale), out var s))
                return 0;
            if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
                return result;
            return 0;
        }

        public MyMaterialFlags MaterialFlags
        {
            get => Descriptor.MaterialFlags;
            set => UserData("Flags", value);
        }

        public float ParallaxHeight
        {
            get => Descriptor.ParallaxHeight;
            set => UserData(nameof(Descriptor.ParallaxHeight), value);
        }

        public float ParallaxBackOffset
        {
            get => Descriptor.ParallaxBackOffset;
            set => UserData(nameof(Descriptor.ParallaxBackOffset), value);
        }

        public bool ParallaxCutout
        {
            get => Descriptor.ParallaxCutout;
            set => UserData(nameof(Descriptor.ParallaxCutout), value);
        }

        public float WindScale
        {
            get => UserDataFloat(nameof(WindScale));
            set => UserData(nameof(WindScale), value);
        }

        public float WindFrequency
        {
            get => UserDataFloat(nameof(WindFrequency));
            set => UserData(nameof(WindFrequency), value);
        }

        public override string ToString() => Name;

        public bool Equals(KeenMaterial other)
        {
            if (ReferenceEquals(null, other)) return false;
            return ReferenceEquals(this, other) || MaterialEquals.Equals(Descriptor, other.Descriptor);
        }

        public override bool Equals(object obj) => obj is KeenMaterial other && Equals(other);

        public override int GetHashCode() => MaterialEquals.GetHashCode(Descriptor);

        public static readonly IEqualityComparer<MyMaterialDescriptor> MaterialEquals = new MaterialEquality();

        private static readonly IgnoreCaseComparer DictEquals = new IgnoreCaseComparer();

        private sealed class MaterialEquality : IEqualityComparer<MyMaterialDescriptor>
        {
            public bool Equals(MyMaterialDescriptor x, MyMaterialDescriptor y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.MaterialName == y.MaterialName
                       && x.Technique == y.Technique
                       && DictEquals.Equals(x.Textures, y.Textures)
                       && DictEquals.Equals(x.UserData, y.UserData);
            }

            public int GetHashCode(MyMaterialDescriptor obj)
            {
                unchecked
                {
                    var hashCode = obj.MaterialName.GetHashCode();
                    hashCode = (hashCode * 397) ^ obj.Technique.GetHashCode();
                    hashCode = (hashCode * 397) ^ DictEquals.GetHashCode(obj.Textures);
                    hashCode = (hashCode * 397) ^ DictEquals.GetHashCode(obj.UserData);
                    return hashCode;
                }
            }
        }

        private sealed class IgnoreCaseComparer : IEqualityComparer<Dictionary<string, string>>
        {
            public bool Equals(Dictionary<string, string> x, Dictionary<string, string> y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count)
                    return false;
                foreach (var kv in x)
                    if (!y.TryGetValue(kv.Key, out var ov))
                        return false;
                    else if (!StringComparer.OrdinalIgnoreCase.Equals(kv.Value, ov))
                        return false;
                return true;
            }

            public int GetHashCode(Dictionary<string, string> obj)
            {
                var hash = 0;
                foreach (var kv in obj)
                    hash += kv.Key.GetHashCode() * StringComparer.OrdinalIgnoreCase.GetHashCode(kv.Value);
                return hash;
            }
        }
    }

    public static class KeenMaterialExt
    {
        public static void ProcessMaterial(
            this KeenMod mod, KeenMaterial mtl, 
            KeenProcessingFlags defaultFlags = KeenProcessingFlags.None,
            KeenProcessingFlags? cmFlags = null,
            KeenProcessingFlags? ngFlags = null,
            KeenProcessingFlags? addFlags = null,
            KeenProcessingFlags? alphaMaskFlags = null)
        {
            mtl.ColorMetalTexture = mod.ConvertTexture(mtl.ColorMetalTexture, KeenTextureType.ColorMetal, cmFlags ?? defaultFlags);
            mtl.NormalGlossTexture = mod.ConvertTexture(mtl.NormalGlossTexture, KeenTextureType.NormalGloss, ngFlags ?? defaultFlags);
            mtl.ExtensionTexture = mod.ConvertTexture(mtl.ExtensionTexture, KeenTextureType.Extension, addFlags ?? defaultFlags);
            mtl.AlphaMaskTexture = mod.ConvertTexture(mtl.AlphaMaskTexture, KeenTextureType.AlphaMask, alphaMaskFlags ?? defaultFlags);
        }

        public static void ProcessMaterials(this KeenMod mod,
            IEnumerable<KeenMaterial> materials, 
            KeenProcessingFlags defaultFlags = KeenProcessingFlags.None,
            KeenProcessingFlags? cmFlags = null,
            KeenProcessingFlags? ngFlags = null,
            KeenProcessingFlags? addFlags = null,
            KeenProcessingFlags? alphaMaskFlags = null)
        {
            foreach (var material in materials)
                mod.ProcessMaterial(material, defaultFlags, cmFlags, ngFlags, addFlags, alphaMaskFlags);
        }
    }
}