using System.Collections.Generic;
using Medieval.GUI.ContextMenu;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    public abstract class ContextMenuDropdownDataSource : IMyContextMenuDataSource
    {
        public abstract int Count { get; }
        public abstract int Selected { get; set; }

        public virtual void Close()
        {
        }

        /// <summary>
        /// Revision number of the items list.  UI elements may not update
        /// if this version doesn't change.
        /// </summary>
        public virtual int ItemsVersion => Count;

        public abstract void GetItems(List<DropdownItem> output);

        public readonly struct DropdownItem
        {
            public readonly MyStringId Text;
            public readonly MyStringId? Tooltip;

            public DropdownItem(MyStringId text, MyStringId? tooltip = default)
            {
                Text = text;
                Tooltip = tooltip;
            }
        }
    }
}