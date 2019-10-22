using System.Collections;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public static class MaterialEditListExtensions
    {
        public static void AddOrReplace(this List<MaterialEdit> list, MaterialEdit edt)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i].Equals(edt))
                {
                    list[i] = edt;
                    return;
                }
            list.Add(edt);
        }
    }
}