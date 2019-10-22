using System;

namespace Equinox76561198048419394.Core.Conditions
{
    public class ConstantCondition : ICondition
    {
        public bool State { get; }

        public event Action<bool, bool> StateChanged
        {
            add { }
            remove { }
        }

        private ConstantCondition(bool state)
        {
            State = state;
        }

        public static ConstantCondition For(bool v)
        {
            return v ? True : False;
        }

        public static readonly ConstantCondition True = new ConstantCondition(true);
        public static readonly ConstantCondition False = new ConstantCondition(false);
    }
}