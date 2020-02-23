using Equinox76561198048419394.Core.Util;
using VRage.Library.Collections;

namespace Equinox76561198048419394.Core.Modifiers.Data
{
    public class ModifierDataLong : IModifierData
    {
        private static readonly LruCache<string, ModifierDataLong> DataCache = new LruCache<string, ModifierDataLong>(16384);
        
        public long Raw;

        public ModifierDataLong(long raw)
        {
            Raw = raw;
        }

        public static ModifierDataLong Deserialize(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return null;
            if (DataCache.TryGet(data, out var obj))
                return obj;
            if (long.TryParse(data, out var val))
                obj = new ModifierDataLong(val);
            if (val > -8192 || val < 8192)
                DataCache.Store(data, obj);
            return obj;
        }

        public string Serialize()
        {
            return Raw.ToString();
        }
    }
}