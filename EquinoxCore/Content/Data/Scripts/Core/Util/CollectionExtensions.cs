using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util
{
    public static class CollectionExtensions
    {
        public static void AddMulti<TKey, TValue, TCollection>(this Dictionary<TKey, TCollection> dict, TKey key, TValue value)
            where TCollection : ICollection<TValue>, new()
        {
            if (!dict.TryGetValue(key, out var collect))
                dict.Add(key, collect = new TCollection());
            collect.Add(value);
        }
    }
}