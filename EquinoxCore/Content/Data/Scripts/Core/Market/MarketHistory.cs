using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using VRage.Game;

namespace Equinox76561198048419394.Core.Market
{
    public static class MarketHistory
    {
        public static bool TryGetHistory(this EquiMarketStorageComponent storage, MyDefinitionId item,
            out TemporalHistoryReader<MarketItemHistoryEntry> history)
        {
            if (storage.Container.TryGet(out EquiMarketHistoryComponent historyComponent) && historyComponent.TryGetHistory(item, out var itemHistory))
            {
                history = itemHistory.History;
                return true;
            }

            history = default;
            return false;
        }

        public static TemporalHistoryReader<MarketItemHistoryEntry> History(this EquiMarketStorageComponent storage, MyDefinitionId item)
            => storage.TryGetHistory(item, out var history) ? history : default;

        public static TemporalHistoryReader<MarketItemHistoryEntry> MergedHistory(this in MarketEnumerable markets, MyDefinitionId item) =>
            MergedHistoryInternal<MarketEnumerable, EquiMarketManager.MarketEnumerator>(in markets, item);

        public static TemporalHistoryReader<MarketItemHistoryEntry> MergedHistory(this in FilteredMarketEnumerable markets, MyDefinitionId item) =>
            MergedHistoryInternal<FilteredMarketEnumerable, MarketQueries.FilteredMarketEnumerator>(in markets, item);

        public static MarketItemHistoryEntry HistoryAt(this EquiMarketStorageComponent storage, MyDefinitionId item, DateTime time)
            => storage.TryGetHistory(item, out var history) ? history.BucketAt(time) : default;

        public static MarketItemHistoryEntry MergedHistoryAt(this in MarketEnumerable markets, MyDefinitionId item, DateTime time) =>
            MergedHistoryAtInternal<MarketEnumerable, EquiMarketManager.MarketEnumerator>(in markets, item, time);

        public static MarketItemHistoryEntry MergedHistoryAt(this in FilteredMarketEnumerable markets, MyDefinitionId item, DateTime time) =>
            MergedHistoryAtInternal<FilteredMarketEnumerable, MarketQueries.FilteredMarketEnumerator>(in markets, item, time);

        public static MarketItemHistoryEntry HistoryOver(this EquiMarketStorageComponent storage, MyDefinitionId item, DateTime time, TimeSpan length)
            => storage.TryGetHistory(item, out var history) ? history.BucketOver(time, length) : default;

        public static MarketItemHistoryEntry MergedHistoryOver(this in MarketEnumerable markets, MyDefinitionId item, DateTime time, TimeSpan length) =>
            MergedHistoryOverInternal<MarketEnumerable, EquiMarketManager.MarketEnumerator>(in markets, item, time, length);

        public static MarketItemHistoryEntry MergedHistoryOver(this in FilteredMarketEnumerable markets, MyDefinitionId item, DateTime time, TimeSpan length) =>
            MergedHistoryOverInternal<FilteredMarketEnumerable, MarketQueries.FilteredMarketEnumerator>(in markets, item, time, length);

        private static TemporalHistoryReader<MarketItemHistoryEntry> MergedHistoryInternal<TEnumerable, TEnumerator>(
            in TEnumerable markets, MyDefinitionId item)
            where TEnumerable : IConcreteEnumerable<EquiMarketStorageComponent, TEnumerator>
            where TEnumerator : IEnumerator<EquiMarketStorageComponent>
        {
            TemporalHistoryReader<MarketItemHistoryEntry>? single = null;
            TemporalHistory<MarketItemHistoryEntry> mutable = null;
            using (var enumerator = markets.GetEnumerator())
                while (enumerator.MoveNext())
                {
                    if (!enumerator.Current.TryGetHistory(item, out var history) || history.IsEmpty)
                        continue;
                    if (mutable != null)
                        mutable.Add(history);
                    else if (!single.HasValue)
                        single = history;
                    else
                    {
                        mutable = single.Value.TryCopy();
                        mutable.Add(history);
                    }
                }

            if (mutable != null) return mutable;
            return single ?? default;
        }

        private static MarketItemHistoryEntry MergedHistoryAtInternal<TEnumerable, TEnumerator>(
            in TEnumerable markets, MyDefinitionId item, DateTime time)
            where TEnumerable : IConcreteEnumerable<EquiMarketStorageComponent, TEnumerator>
            where TEnumerator : IEnumerator<EquiMarketStorageComponent>
        {
            MarketItemHistoryEntry result = default;
            using (var enumerator = markets.GetEnumerator())
                while (enumerator.MoveNext())
                {
                    if (!enumerator.Current.TryGetHistory(item, out var history)) continue;
                    result.MergeWith(in history.BucketAt(time));
                }

            return result;
        }

        private static MarketItemHistoryEntry MergedHistoryOverInternal<TEnumerable, TEnumerator>(
            in TEnumerable markets, MyDefinitionId item, DateTime start, TimeSpan length)
            where TEnumerable : IConcreteEnumerable<EquiMarketStorageComponent, TEnumerator>
            where TEnumerator : IEnumerator<EquiMarketStorageComponent>
        {
            MarketItemHistoryEntry result = default;
            using (var enumerator = markets.GetEnumerator())
                while (enumerator.MoveNext())
                {
                    if (!enumerator.Current.TryGetHistory(item, out var history)) continue;
                    result.MergeWith(history.BucketOver(start, length));
                }

            return result;
        }
    }
}