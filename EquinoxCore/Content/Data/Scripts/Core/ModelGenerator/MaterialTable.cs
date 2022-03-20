using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public sealed class MaterialTable
    {
        private static readonly FastResourceLock MaterialsLock = new FastResourceLock();
        private static readonly Dictionary<string, MyMaterialDescriptor> Materials = new Dictionary<string, MyMaterialDescriptor>();

        public static MyMaterialDescriptor From(MaterialSpec spec)
        {
            var hasher = new Hashing.HashBuilder();
            using (PoolManager.Get<List<MaterialSpec.Parameter>>(out var sorted))
            {
                sorted.AddRange(spec.Parameters);
                sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                foreach (var param in sorted)
                {
                    hasher.Add(param.Name);
                    hasher.Add(param.Value);
                }
            }

            var materialId = hasher.Build().ToString64();
            using (MaterialsLock.AcquireExclusiveUsing())
            {
                if (Materials.TryGetValue(materialId, out var value))
                    return value;
                Materials.Add(materialId, value = ToMwmMaterial(materialId, spec));
                return value;
            }
        }

        public static bool TryGetById(string id, out MyMaterialDescriptor descriptor)
        {
            using (MaterialsLock.AcquireSharedUsing())
            {
                return Materials.TryGetValue(id, out descriptor);
            }
        }

        private static bool TryRemoveProp(Dictionary<string, string> props, string key, out string value)
        {
            if (!props.TryGetValue(key, out value))
                return false;
            props.Remove(key);
            return true;
        }

        private static MyMaterialDescriptor ToMwmMaterial(string id, MaterialSpec spec)
        {
            const string techniqueKey = "Technique";
            const string glassCwKey = "GlassMaterialCW";
            const string glassCcwKey = "GlassMaterialCCW";
            const string glassSmoothKey = "GlassSmooth";
            
            var desc = new MyMaterialDescriptor(id);
            var props = spec.Parameters.ToDictionary(x => x.Name, x => x.Value);
            if (TryRemoveProp(props, techniqueKey, out var techniqueStr))
                desc.Technique = string.IsNullOrEmpty(techniqueStr) ? MyMeshDrawTechnique.MESH.ToString() : techniqueStr;
            if (TryRemoveProp(props, glassCwKey, out var glassCw))
                desc.GlassCW = glassCw;
            if (TryRemoveProp(props, glassCcwKey, out var glassCcw))
                desc.GlassCCW = glassCcw;
            if (TryRemoveProp(props, glassSmoothKey, out var glassSmooth))
                desc.GlassSmoothNormals = Convert.ToBoolean(glassSmooth);

            foreach (var kv in props)
            {
                if (kv.Key.Contains("Texture"))
                    desc.Textures[kv.Key] = kv.Value;
                else
                    desc.UserData[kv.Key] = kv.Value;
            }

            return desc;
        }
    }
}