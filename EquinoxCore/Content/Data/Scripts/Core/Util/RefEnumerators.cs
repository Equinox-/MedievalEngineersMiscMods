using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util
{
    public interface IRefEnumerable<out T, out TEnumerator> : IEnumerable<T> where TEnumerator : IRefEnumerator<T>
    {
        new TEnumerator GetEnumerator();
    }

    public interface IRefReadonlyEnumerable<out T, out TEnumerator> : IEnumerable<T> where TEnumerator : IRefReadonlyEnumerator<T>
    {
        new TEnumerator GetEnumerator();
    }

    public interface IRefEnumerator<T> : IEnumerator<T>
    {
        new ref T Current { get; }
    }

    public interface IRefReadonlyEnumerator<T> : IEnumerator<T>
    {
        new ref readonly T Current { get; }
    }
}