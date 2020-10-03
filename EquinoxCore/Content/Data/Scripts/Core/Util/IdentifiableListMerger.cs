using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml.Serialization;
using VRage.ObjectBuilder.Merging;
using VRage.ObjectBuilders.Definitions;

namespace Equinox76561198048419394.Core.Util
{
    public interface IIdentifiable
    {
        [XmlIgnore]
        string Id { get; }
    }

    public sealed class IdentifiableListMerger<T, TColl> : IMyObjectBuilderMerger where T : IIdentifiable where TColl : class, ICollection<T>, new()
    {
        private readonly IMyObjectBuilderMerger _delegate = MyObjectBuilderMerger.GetMerger(typeof(T));

        public void Merge(object @base, ref object changeResult, MyDefinitionMergeMode mode)
        {
            if (mode == MyDefinitionMergeMode.Overwrite)
            {
                changeResult = @base;
                return;
            }

            var srcList = @base as TColl;
            if (srcList == null || srcList.Count == 0)
                return;
            changeResult = changeResult ?? new TColl();
            var destList = changeResult as TColl;
            if (destList == null)
                return;

            foreach (var src in srcList)
            {
                var id = src.Id;
                var found = false;
                object dest = default(T);
                foreach (var opt in destList)
                {
                    if (opt.Id != id) continue;
                    dest = opt;
                    found = true;
                    destList.Remove(opt);
                    break;
                }

                if (!found)
                {
                    destList.Add(src);
                    continue;
                }

                _delegate.Merge(src, ref dest, mode);
                destList.Add((T) dest);
            }
        }
    }
}