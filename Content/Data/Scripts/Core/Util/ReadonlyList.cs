using System.Collections;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util
{
    public class ReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly IReadOnlyList<T> _backing;

        public ReadOnlyList(IReadOnlyList<T> backing)
        {
            _backing = backing;
        }
        
        public IEnumerator<T> GetEnumerator()
        {
            return _backing.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) _backing).GetEnumerator();
        }

        public int Count => _backing.Count;

        public T this[int index] => _backing[index];
    }
}