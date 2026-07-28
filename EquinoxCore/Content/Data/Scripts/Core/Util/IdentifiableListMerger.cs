using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Library.Collections;
using VRage.ObjectBuilder.Merging;
using VRage.ObjectBuilders.Definitions;

namespace Equinox76561198048419394.Core.Util
{
    public interface IIdentifiable
    {
        [XmlIgnore]
        string Id { get; }
    }

    public static class IdentifiableListMerger
    {
        public static IEnumerable<T> LastById<T>(this List<T> items) where T : IIdentifiable
        {
            if (items == null)
                yield break;
            using (PoolManager.Get(out Dictionary<string, int> seen))
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var id = items[i].Id;
                    if (!string.IsNullOrEmpty(id))
                        seen[id] = i;
                }

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (string.IsNullOrEmpty(item.Id) || seen[item.Id] == i)
                        yield return item;
                }
            }
        }
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
                if (!string.IsNullOrEmpty(id))
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
                destList.Add((T)dest);
            }
        }
    }
}