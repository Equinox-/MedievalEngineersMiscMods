using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.Collections;
using VRage.Library.Collections;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public class MaterialEditsBuilder : IDisposable
    {
        private static readonly ConcurrentBag<MaterialEditsBuilder> BuilderPool = new ConcurrentBag<MaterialEditsBuilder>();
        private readonly Dictionary<string, string> _materialSwap = new Dictionary<string, string>();
        private readonly Dictionary<string, List<ListReader<MaterialEdit>>> _builders = new Dictionary<string, List<ListReader<MaterialEdit>>>();

        private MaterialEditsBuilder()
        {
        }

        public void SwapMaterial(string originalMaterial, string newMaterial)
        {
            if (originalMaterial == newMaterial)
                _materialSwap.Remove(originalMaterial);
            else
                _materialSwap[originalMaterial] = newMaterial;
        }

        public void Add(string material, ListReader<MaterialEdit> edit)
        {
            if (!_builders.TryGetValue(material, out var list))
                _builders[material] = list = PoolManager.Get<List<ListReader<MaterialEdit>>>();
            list.Add(edit);
        }

        public bool TryGetMaterialSwap(string material, out string newMaterial) => _materialSwap.TryGetValue(material, out newMaterial);

        public void Get(MaterialInModel material, List<MaterialEdit> dest)
        {
            dest.Clear();
            if (!material.CanEditInternals) return;
            if (!_builders.TryGetValue(material.Name, out var builders)) return;
            if (builders.Count == 0) return;
            // Add first edit list without checking for duplicates.
            var firstBuilder = builders[0];
            dest.EnsureSpace(firstBuilder.Count);
            foreach (var edit in firstBuilder)
                dest.Add(edit);
            // Add remaining edit list while checking for duplicates.
            for (var i = 1; i < builders.Count; i++)
                foreach (var edit in builders[i])
                    dest.AddOrReplace(edit);
        }

        public static MaterialEditsBuilder Allocate()
        {
            return BuilderPool.TryTake(out var tmp) ? tmp : new MaterialEditsBuilder();
        }

        public void Dispose()
        {
            foreach (var k in _builders.Values)
            {
                var tmp = k;
                PoolManager.Return(ref tmp);
            }

            _builders.Clear();
            BuilderPool.Add(this);
        }

        public override string ToString()
        {
            return string.Join(", ", _builders.Keys);
        }
    }
}