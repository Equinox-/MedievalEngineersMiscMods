using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util.Memory;

namespace Equinox76561198048419394.Core.Inventory
{
    public readonly struct DynamicLabel : IComparable<DynamicLabel>
    {
        private static readonly DynamicLabel Empty = default;

        private readonly uint _minStackSize;
        private readonly string _format;
        private readonly ulong _firstDivisor;
        private readonly ulong _secondDivisor;
        private readonly ulong _secondModulus;

        private bool IsValid => !string.IsNullOrWhiteSpace(_format);

        private DynamicLabel(in MyObjectBuilder_DynamicLabel ob)
        {
            _minStackSize = ob.MinStackSize;
            _format = ob.Format ?? "";
            _firstDivisor = ob.FirstDivisor <= 0 ? 1 : ob.FirstDivisor;
            _secondDivisor = !_format.Contains("{1") ? 0 : ob.SecondDivisor <= 0 ? 1 : ob.SecondDivisor;
            _secondModulus = ob.SecondModulus <= 0 ? uint.MaxValue : ob.SecondModulus;
        }

        internal static EqReadOnlySpan<DynamicLabel> Of(List<MyObjectBuilder_DynamicLabel> obs) => (obs?
            .Select(x => new DynamicLabel(in x))
            .Where(x => x.IsValid)
            .OrderBy(x => x)
            .ToArray() ?? Array.Empty<DynamicLabel>()).AsEqSpan();

        internal static ref readonly DynamicLabel Access(in EqReadOnlySpan<DynamicLabel> array, ulong amount, out bool okay)
        {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < array.Length; i++)
            {
                ref readonly var dynamicIcon = ref array[i];
                if (amount < dynamicIcon._minStackSize)
                    continue;
                okay = true;
                return ref dynamicIcon;
            }

            okay = false;
            return ref Empty;
        }

        /// <summary>
        /// Given the item amount, formats it as a string.
        /// </summary>
        public string Format(ulong count)
        {
            return _secondDivisor <= 0
                ? string.Format(_format, count / _firstDivisor)
                : string.Format(_format, count / _firstDivisor, count / _secondDivisor % _secondModulus);
        }

        public int CompareTo(DynamicLabel b) => -_minStackSize.CompareTo(b._minStackSize);
    }

    public readonly struct DynamicIcon : IComparable<DynamicIcon>
    {
        internal static readonly DynamicIcon Empty = default;
        private readonly uint _minStackSize;
        private readonly uint _maxDurability;
        public readonly string[] Icons;

        private bool IsValid => Icons?.Length > 0;

        private DynamicIcon(in MyObjectBuilder_DynamicIcon ob)
        {
            _minStackSize = ob.MinStackSize;
            _maxDurability = ob.MaxDurability != 0 ? ob.MaxDurability : uint.MaxValue;
            if (ob.IconAttribute != null && ob.Icons?.Length > 0)
            {
                Icons = new string[ob.Icons.Length + 1];
                Icons[0] = ob.IconAttribute;
                Array.Copy(ob.Icons, 0, Icons, 1, ob.Icons.Length);
            }
            else if (ob.IconAttribute != null)
                Icons = new[] { ob.IconAttribute };
            else
                Icons = ob.Icons ?? Array.Empty<string>();
        }

        internal static EqReadOnlySpan<DynamicIcon> Of(List<MyObjectBuilder_DynamicIcon> obs) => (obs?
            .Select(x => new DynamicIcon(in x))
            .Where(x => x.IsValid)
            .OrderBy(x => x)
            .ToArray() ?? Array.Empty<DynamicIcon>()).AsEqSpan();

        internal static ref readonly DynamicIcon Access(in EqReadOnlySpan<DynamicIcon> array, int amount, int durability, out bool okay)
        {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < array.Length; i++)
            {
                ref readonly var dynamicIcon = ref array[i];
                if (amount < dynamicIcon._minStackSize)
                    continue;
                if (durability >= dynamicIcon._maxDurability)
                    continue;
                okay = true;
                return ref dynamicIcon;
            }

            okay = false;
            return ref Empty;
        }

        public int CompareTo(DynamicIcon b)
        {
            // Higher minimum stack size first.
            if (_minStackSize != b._minStackSize)
                return -_minStackSize.CompareTo(b._minStackSize);
            // Lower max durability first.
            return _maxDurability.CompareTo(b._maxDurability);
        }
    }


    public struct MyObjectBuilder_DynamicLabel
    {
        /// <summary>
        /// Minimum stack size for this dynamic label to be used.
        /// </summary>
        [XmlAttribute("MinStackSize")]
        public uint MinStackSize;

        /// <summary>
        /// Format string used to display the label.
        /// {0} will be the floor(ItemCount / FirstDivisor)
        /// {1} will be the floor(ItemCount / SecondDivisor) % SecondModulus.
        /// </summary>
        [XmlAttribute("Format")]
        public string Format;

        /// <summary>
        /// The stack size is divided by this, then rounded down, and used as the {0} parameter of the format.
        /// </summary>
        [XmlAttribute("FirstDivisor")]
        public uint FirstDivisor;

        /// <summary>
        /// The stack size is divided by this, then taken modulo SecondModulus, and used as the {1} parameter of the format.
        /// </summary>
        [XmlAttribute("SecondDivisor")]
        public uint SecondDivisor;

        /// <summary>
        /// The stack size is divided by SecondDivisor, then taken modulo this, and used as the {1} parameter of the format.
        /// </summary>
        [XmlAttribute("SecondModulus")]
        public uint SecondModulus;
    }

    public struct MyObjectBuilder_DynamicIcon
    {
        /// <summary>
        /// Minimum stack size for this dynamic icon to be used.
        /// </summary>
        [XmlAttribute("MinStackSize")]
        public uint MinStackSize;

        /// <summary>
        /// Maximum durability for this dynamic icon to be used.
        /// </summary>
        [XmlAttribute("MaxDurability")]
        public uint MaxDurability;

        /// <summary>
        /// Icon to use.
        /// </summary>
        [XmlAttribute("Icon")]
        public string IconAttribute;

        /// <summary>
        /// Icons to use.
        /// </summary>
        [XmlElement("Icon")]
        public string[] Icons;
    }
}