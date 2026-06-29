using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public static class MaterialEditListExtensions
    {
        public static void AddOrReplace(this List<MaterialEdit> list, MaterialEdit edt)
        {
            var span = list.AsEqSpan();
            for (var i = 0; i < span.Length; i++)
            {
                ref var item = ref span[i];
                if (!item.Equals(edt)) continue;
                item = edt;
                return;
            }
            list.Add(edt);
        }
    }
}