using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    public sealed class EnumDataSource<T> : ContextMenuDropdownDataSource where T : struct
    {
        private static readonly DropdownItem[] Names;
        private static readonly T[] Values;
        private static readonly Dictionary<T, int> Lookup = new Dictionary<T, int>();

        static EnumDataSource()
        {
            Values = (T[])Enum.GetValues(typeof(T));
            var names = Enum.GetNames(typeof(T));
            Names = new DropdownItem[names.Length];
            for (var i = 0; i < Values.Length; i++)
            {
                Names[i] = new DropdownItem(MyStringId.GetOrCompute(names[i]));
                Lookup[Values[i]] = i;
            }
        }

        private readonly Func<T> _getter;
        private readonly Action<T> _setter;
        private readonly bool _excludeLast;

        public EnumDataSource(
            Func<T> getter,
            Action<T> setter,
            bool excludeLast = false)
        {
            _getter = getter;
            _setter = setter;
            _excludeLast = excludeLast;
        }

        public override int Count => Names.Length - (_excludeLast ? 1 : 0);
        
        public override int Selected
        {
            get => Lookup.GetValueOrDefault(_getter(), 0);
            set => _setter(Values[value]);
        }

        public override void GetItems(List<DropdownItem> output)
        {
            output.Clear();
            var names = Names.AsEqSpan();
            if (_excludeLast)
                names = names.Slice(0, names.Length - 1);
            output.AddSpan(names);
        }
    }
}