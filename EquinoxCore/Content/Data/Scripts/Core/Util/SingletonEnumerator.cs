using System.Collections;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util
{
    public struct SingletonEnumerator<T> : IEnumerator<T>
    {
        private bool _consumed;

        public SingletonEnumerator(T value)
        {
            _consumed = false;
            Current = value;
        }
        
        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            var has = !_consumed;
            _consumed = true;
            return has;
        }

        public void Reset()
        {
            _consumed = false;
        }

        public T Current { get; }

        object IEnumerator.Current => Current;
    }
}