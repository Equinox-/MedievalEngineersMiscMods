using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage.Collections;
using VRage.Library.Collections;
using VRage.Library.Collections.Concurrent;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public class MaterialEditsBuilder : IDisposable
    {
        private static readonly ConcurrentBag<MaterialEditsBuilder> BuilderPool = new ConcurrentBag<MaterialEditsBuilder>();
        private readonly Dictionary<string, List<ListReader<MaterialEdit>>> _builders = new Dictionary<string, List<ListReader<MaterialEdit>>>();

        private MaterialEditsBuilder()
        {
        }

        public void Add(string material, ListReader<MaterialEdit> edit)
        {
            if (!_builders.TryGetValue(material, out var list))
                _builders[material] = list = PoolManager.Get<List<ListReader<MaterialEdit>>>();
            list.Add(edit);
        }

        public void Get(string material, List<MaterialEdit> dest)
        {
            dest.Clear();
            if (!_builders.TryGetValue(material, out var builders)) return;
            foreach (var builder in builders)
            foreach (var edit in builder)
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