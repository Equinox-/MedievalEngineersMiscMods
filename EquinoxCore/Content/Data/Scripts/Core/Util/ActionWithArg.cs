using System;

namespace Equinox76561198048419394.Core.Util
{
    public readonly struct ActionWithArg<TInstance, T1>
    {
        public readonly TInstance Instance;
        public readonly Action<TInstance, T1> Delegate;

        public ActionWithArg(TInstance instance, Action<TInstance, T1> @delegate)
        {
            Instance = instance;
            Delegate = @delegate;
        }

        public void Invoke(T1 t1)
        {
            Delegate.Invoke(Instance, t1);
        }
    }
}