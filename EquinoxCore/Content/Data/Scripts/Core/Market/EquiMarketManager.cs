using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Misc;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components.Session;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Scene;
using VRage.Serialization;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    [MySessionComponent(typeof(MyObjectBuilder_EquiMarketManager), AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MySceneComponent), Critical = true)]
    [MyDependency(typeof(EquiCurrencySystems), Critical = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public partial class EquiMarketManager : MySessionComponent, IMyPersistenceComponent
    {
        [Automatic]
        private readonly MyChatSystem _chat = null;
        [Automatic]
        private readonly EquiCurrencySystems _currencySystems = null;

        public EquiMarketManagerDefinition Definition { get; private set; }

        public EquiCurrencySystemDefinition CurrencySystem => _currencySystems?.Default;

        protected override void LoadDefinition(MySessionComponentDefinition def)
        {
            base.LoadDefinition(def);
            Definition = (EquiMarketManagerDefinition)def;
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            if (Definition == null)
            {
                // Load a default from the definition manager.
                Definition = MyDefinitionManager.Get<EquiMarketManagerDefinition>(MyStringHash.NullOrEmpty);
            }

            if (Definition == null)
            {
                // Manufacture a default.
                var ob = new MyObjectBuilder_EquiMarketManagerDefinition
                {
                    Id = new SerializableDefinitionId(typeof(MyObjectBuilder_EquiMarketManagerDefinition), null)
                };
                Definition = new EquiMarketManagerDefinition();
                Definition.Init(ob, MyModContext.BaseGame);
            }

            if (!MyMultiplayerModApi.Static.IsServer) IndexMarketEntitiesOnClient();

            _chat?.RegisterChatCommand("/locational-markets", HandleLocationalMarkets, "Manage locational markets as an admin", MyChatCommandType.Server);
            _chat?.RegisterChatCommand("/locational-markets-client", HandleLocationalMarkets, "Manage locational markets as an admin (client side queries)",
                MyChatCommandType.Client);
        }

        private void IndexMarketEntitiesOnClient()
        {
            Scene.EntityActivated += AddEntityOnClient;
            Scene.EntityDeactivated += RemoveEntity;
            foreach (var ent in Scene.TopLevelEntities)
                AddEntityOnClient(ent);
            return;

            bool Matches(MyEntity ent)
            {
                if (ent.Parent != null) return false;
                foreach (var tag in ent.Tags)
                    if (_persistedTags.Contains(tag))
                        return true;
                return false;
            }

            void AddEntityOnClient(MyEntity ent)
            {
                if (!Matches(ent)) return;
                // Clients overwrite the entity if duplicate IDs occur.
                _entities[ent.Id] = ent;
            }
        }

        #region Accessors

        /// <summary>
        /// Called whenever an order changes state in any market.
        /// </summary>
        public event DelMarketOrderChanged OnOrderChanged;

        /// <summary>
        /// Called whenever two orders exchange items in any market.
        /// </summary>
        public event DelMarketOrderSolved OnOrderSolved;

        internal void RaiseOrderChanged(EquiMarketStorageComponent market, MarketOrderOperation op, in LocalMarketOrder order)
        {
            OnOrderChanged?.Invoke(market, op, in order);
            _touchedItems.Write.Add(order.Item);
            _touchedStorageEntities.Write.Add(market.Entity.Id);
        }

        internal void RaiseOrderSolved(
            EquiMarketStorageComponent seller, in LocalMarketOrder sellOrder, uint sellPricePerItem,
            EquiMarketStorageComponent buyer, in LocalMarketOrder buyOrder, uint buyPricePerItem,
            uint count)
        {
            OnOrderSolved?.Invoke(seller, in sellOrder, sellPricePerItem, buyer, in buyOrder, buyPricePerItem, count);
        }

        public MarketEnumerable Markets => new MarketEnumerable(this);

        internal int MarketCount => _entities.Count;

        public struct MarketEnumerator : IEnumerator<EquiMarketStorageComponent>
        {
            private Dictionary<EntityId, MyEntity>.ValueCollection.Enumerator _backing;

            internal MarketEnumerator(EquiMarketManager manager) => _backing = manager._entities.Values.GetEnumerator();

            public void Dispose() => _backing.Dispose();

            public bool MoveNext()
            {
                while (_backing.MoveNext())
                    if (_backing.Current?.Components.Contains<EquiMarketStorageComponent>() ?? false)
                        return true;
                return false;
            }

            public EquiMarketStorageComponent Current => _backing.Current?.Get<EquiMarketStorageComponent>();

            void IEnumerator.Reset() => ((IEnumerator)_backing).Reset();

            object IEnumerator.Current => Current;
        }

        #endregion

        #region Market Solving

        private PendingSet<MyDefinitionId> _touchedItems = new PendingSet<MyDefinitionId>(MyDefinitionId.Comparer);
        private PendingSet<EntityId> _touchedStorageEntities = new PendingSet<EntityId>(EntityId.Comparer);

        [Update(1_000)]
        private void SolveMarketsFast(long dt)
        {
            _touchedItems.Swap();
            _touchedStorageEntities.Swap();

            foreach (var id in _touchedStorageEntities.Read)
                if (_entities.TryGetValue(id, out var entity) && entity.Components.TryGet(out EquiMarketStorageComponent storage))
                    MarketSolverIsolated.SolveIsolatedMarket(storage, _touchedItems.Read);
        }

        private struct PendingSet<T>
        {
            public HashSet<T> Read, Write;

            public PendingSet(IEqualityComparer<T> comparer)
            {
                Read = new HashSet<T>(comparer);
                Write = new HashSet<T>(comparer);
            }

            public void Swap()
            {
                MyUtils.Swap(ref Read, ref Write);
                Write.Clear();
            }
        }

        #endregion

        #region Persistence

        public void Save()
        {
        }

        private readonly Dictionary<EntityId, MyEntity> _entities = new Dictionary<EntityId, MyEntity>(EntityId.Comparer);

        public void AddEntity(MyEntity entity) => _entities.Add(entity.Id, entity);

        public void RemoveEntity(MyEntity entity) => _entities.Remove(entity.Id);

        public void AddGroup(MyGroup group) => throw new NotImplementedException();

        public void RemoveGroup(MyGroup group) => throw new NotImplementedException();

        private readonly HashSet<MyStringHash> _persistedTags = new HashSet<MyStringHash> { MyStringHash.GetOrCompute("EquiMarketStorage") };
        public IEnumerable<MyStringHash> PersistedTags => _persistedTags;
        public IEnumerable<string> DataFolders { get; } = new HashSet<string>();
        public bool Default => false;

        protected override bool IsSerialized => LocationalMarketsOverride != null || _entities.Count > 0;

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_EquiMarketManager)base.Serialize();
            ob.LocationalMarketsOverride = _locationalMarketsOverride;
            ob.StorageEntities = _entities.Values.Select(x => new MyObjectBuilder_EquiMarketManager.MarketStorage
                {
                    Id = x.EntityId,
                    Subtype = x.DefinitionId?.SubtypeName,
                    Components = x.Components.Serialize(),
                })
                .Where(x => x.Components?.Components?.Count > 0)
                .ToList();
            return ob;
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var ob = (MyObjectBuilder_EquiMarketManager)objectBuilder;
            _locationalMarketsOverride = ob.LocationalMarketsOverride;
            if (ob.StorageEntities == null) return;
            // MyEntity.Init uses MyEntity.CreateStandardRenderComponentsExtCallback, which is not initialized until the static constructor
            // of MyEntities is invoked.
            // ReSharper disable once UnusedVariable
            var initializeEntitiesDueToWeirdRaceCondition = MyEntities.Count;
            var staging = new MyStagingScene("EquiMarkets");
            foreach (var storedEntity in ob.StorageEntities)
            {
                var entityBuilder = EntityBuilder(storedEntity.Id, storedEntity.Subtype);
                entityBuilder.ComponentContainer = storedEntity.Components;
                staging.LoadEntity(entityBuilder);
            }

            Scene.Merge(staging);
        }

        #endregion

        private static MyObjectBuilder_EntityBase EntityBuilder(
            EntityId id, string subtype) => new MyObjectBuilder_EntityBase
        {
            EntityId = (long)id.Value,
            EntityDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), subtype),
            PersistentFlags = MyPersistentEntityFlags2.None,
            PositionAndOrientation = new MyPositionAndOrientation(MatrixD.CreateTranslation(new Vector3D(1e9))),
        };

        internal MyEntity CreateMarketStorage(EntityId id, string subtype, MyObjectBuilder_EquiMarketHostComponent host)
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                throw new Exception("Only possible on the server");
            if (!MyDefinitionManager.TryGet(new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), subtype), out MyContainerDefinition def))
            {
                this.GetLogger().Warning($"Attempted to create market storage for host {host.GetType()} with subtype {subtype} that does not exist");
                return null;
            }

            var ob = EntityBuilder(id, def.Id.SubtypeName);
            ob.ComponentContainer = new MyObjectBuilder_ComponentContainer();
            ob.ComponentContainer.AddComponent(host);
            return Scene.LoadEntity(ob, activate: true);
        }
    }

    public readonly struct MarketEnumerable : IReadOnlyCollection<EquiMarketStorageComponent>,
        IConcreteEnumerable<EquiMarketStorageComponent, EquiMarketManager.MarketEnumerator>
    {
        private readonly EquiMarketManager _manager;

        internal MarketEnumerable(EquiMarketManager manager) => _manager = manager;

        public EquiMarketManager.MarketEnumerator GetEnumerator() => new EquiMarketManager.MarketEnumerator(_manager);

        public int Count => _manager.MarketCount;

        IEnumerator<EquiMarketStorageComponent> IEnumerable<EquiMarketStorageComponent>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketManager : MyObjectBuilder_SessionComponent
    {
        [XmlElement("Storage")]
        [NoSerialize] // Don't replicate as part of the initial scene setup, markets are streamed after initial load.
        public List<MarketStorage> StorageEntities;

        [XmlElement]
        public LocationalMarketsMode? LocationalMarketsOverride;

        public class MarketStorage
        {
            [XmlAttribute]
            public long Id;

            [XmlAttribute]
            public string Subtype;

            [XmlIgnore]
            [Serialize]
            public MyObjectBuilder_ComponentContainer Components;

            [NoSerialize]
            [XmlElement("Component")]
            public AbstractXmlProxy<MyObjectBuilder_EntityComponent>[] ComponentsForXml
            {
                get => AbstractXmlProxy.WrapList(Components?.Components);
                set
                {
                    if (value == null)
                    {
                        Components = null;
                        return;
                    }

                    Components = new MyObjectBuilder_ComponentContainer();
                    // drop null values that came from removed mods
                    AbstractXmlProxy.Unwrap(value, Components.Components, dropDefault: true);
                }
            }
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiMarketManagerDefinition))]
    [MyDependency(typeof(EquiCurrencySystemDefinition))]
    public class EquiMarketManagerDefinition : MySessionComponentDefinition
    {
        public LocationalMarketsMode LocationalMarkets { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiMarketManagerDefinition)def;
            LocationalMarkets = ob.LocationalMarkets ?? LocationalMarketsMode.Disabled;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketManagerDefinition : MyObjectBuilder_SessionComponentDefinition
    {
        [XmlElement]
        public LocationalMarketsMode? LocationalMarkets;
    }
}