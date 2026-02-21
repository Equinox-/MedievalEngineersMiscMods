using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Medieval.Definitions.GameSystems.Factions;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Players;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity.EntityComponents;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiMarketPermissionsComponent))]
    [MyDefinitionRequired(typeof(EquiMarketPermissionsComponentDefinition))]
    [MyDependency(typeof(MyEntityOwnershipComponent))]
    [MyDependency(typeof(EquiMarketHostComponent), Critical = true)]
    public class EquiMarketPermissionsComponent : MyEntityComponent
    {
        private MyDiplomacyManager _diplomacy;
        private MyStringHash _neutral;

        [Automatic]
        private readonly MyEntityOwnershipComponent _ownership = null;

        [Automatic]
        private readonly EquiMarketHostComponent _host = null;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _diplomacy = MySession.Static?.Components.Get<MyDiplomacyManager>();
            _neutral = _diplomacy?.RelationshipNeutral ?? MyStringHash.GetOrCompute("Neutral");
        }

        /// <summary>
        /// Gets the permissions granted to self, against orders created by orderCreator, with local or non-local access.
        /// </summary>
        /// <param name="self">Identity of the acting player</param>
        /// <param name="orderCreatorIdentityId">Identity ID of the order creator.</param>
        /// <param name="local">Should market access be local; will be verified against market positions</param>
        /// <returns>available permissions</returns>
        public EquiMarketPermission PermissionsFor(MyIdentity self, long orderCreatorIdentityId, bool local)
        {
            var selfParty = new MyDiplomaticParty(DiplomaticPartyType.Player, self.Id);
            var marketOwner = _ownership?.OwnerId ?? 0L;
            var controlled = self.ControlledEntity;

            var relationToCreator = _diplomacy != null && orderCreatorIdentityId != 0
                ? _diplomacy.GetRelationshipBetweenParties(selfParty, new MyDiplomaticParty(DiplomaticPartyType.Player, orderCreatorIdentityId)).Status
                : _neutral;
            var relationToOwner = _diplomacy != null && marketOwner != 0
                ? _diplomacy.GetRelationshipBetweenParties(selfParty, new MyDiplomaticParty(DiplomaticPartyType.Player, marketOwner)).Status
                : _neutral;

            return _definition.PermissionsFor(
                local && controlled != null && _host.IsLocal(in controlled.PositionComp.WorldMatrixRef.Translation()),
                relationToCreator, relationToOwner);
        }

        private EquiMarketPermissionsComponentDefinition _definition;

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            _definition = (EquiMarketPermissionsComponentDefinition)def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketPermissionsComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiMarketPermissionsComponentDefinition))]
    [MyDependency(typeof(MyDiplomaticStatusDefinition))]
    public class EquiMarketPermissionsComponentDefinition : MyEntityComponentDefinition
    {
        public EquiMarketPermission PermissionsFor(bool local, MyStringHash relationWithOrderCreator, MyStringHash relationWithMarketOwner)
            => _permissions.GetValueOrDefault(new MarketPermissionKey(local, relationWithOrderCreator, relationWithMarketOwner), (EquiMarketPermission)0);

        private readonly struct MarketPermissionKey : IEquatable<MarketPermissionKey>
        {
            private readonly bool _local;
            private readonly MyStringHash _orderCreatorRelation;
            private readonly MyStringHash _marketOwnerRelation;

            public MarketPermissionKey(bool local, MyStringHash orderCreatorRelation, MyStringHash marketOwnerRelation)
            {
                _local = local;
                _orderCreatorRelation = orderCreatorRelation;
                _marketOwnerRelation = marketOwnerRelation;
            }

            public bool Equals(MarketPermissionKey other) => _local == other._local
                                                             && _orderCreatorRelation.Equals(other._orderCreatorRelation)
                                                             && _marketOwnerRelation.Equals(other._marketOwnerRelation);

            public override bool Equals(object obj) => obj is MarketPermissionKey other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = _local.GetHashCode();
                hashCode = (hashCode * 397) ^ _orderCreatorRelation.GetHashCode();
                hashCode = (hashCode * 397) ^ _marketOwnerRelation.GetHashCode();
                return hashCode;
            }
        }

        private readonly Dictionary<MarketPermissionKey, EquiMarketPermission> _permissions = new Dictionary<MarketPermissionKey, EquiMarketPermission>();

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiMarketPermissionsComponentDefinition)def;
            var relations = MyDefinitionManager.GetOfType<MyDiplomaticStatusDefinition>()
                .Select(x => x.Id.SubtypeId)
                .ToList();
            _permissions.Clear();
            foreach (var local in new[] { false, true })
            foreach (var orderCreatorRelation in relations)
            foreach (var marketOwnerRelation in relations)
            {
                var key = new MarketPermissionKey(local, orderCreatorRelation, marketOwnerRelation);
                EquiMarketPermission allowed = 0;
                if (ob.Rules != null)
                    foreach (var rule in ob.Rules)
                        if ((!rule.LocalOnly || local)
                            && (string.IsNullOrEmpty(rule.OrderCreatorRelation) || rule.OrderCreatorRelation == orderCreatorRelation.String)
                            && (string.IsNullOrEmpty(rule.MarketOwnerRelation) || rule.MarketOwnerRelation == marketOwnerRelation.String)
                            && rule.Allowed != null)
                            foreach (var allow in rule.Allowed)
                                allowed |= allow;
                _permissions.Add(key, allowed);
            }
        }
    }

    [Flags]
    public enum EquiMarketPermission
    {
        /// <summary>
        /// Create a sell order that is paired with an existing buy order.
        /// </summary>
        CreateSellOrderPaired = 1 << 0,
        /// <summary>
        /// Create an arbitrary sell order.
        /// </summary>
        CreateSellOrder = 1 << 1 | CreateSellOrderPaired, // Implies CreateSellOrderPaired
        /// <summary>
        /// Create a buy order that is paired with an existing sell order.
        /// </summary>
        CreateBuyOrderPaired = 1 << 2,
        /// <summary>
        /// Create an arbitrary buy order.
        /// </summary>
        CreateBuyOrder = 1 << 3 | CreateBuyOrderPaired, // Implies CreateBuyOrderPaired
        /// <summary>
        /// Collect money and items from an order.
        /// </summary>
        CollectOrder = 1 << 4,
        /// <summary>
        /// Cancel an order.
        /// </summary>
        CancelOrder = 1 << 5,
    }

    public static class EquiMarketPermissionsExt
    {
        public static bool Has(this EquiMarketPermission has, EquiMarketPermission check) => (has & check) == check;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketPermissionsComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("Rule")]
        public List<MarketPermissionRule> Rules;

        public class MarketPermissionRule
        {
            /// <summary>
            /// Only grant permission when the diplomatic status between the order creator and the interacting player matches this value.
            /// Empty to match any diplomatic status.
            /// </summary>
            [XmlAttribute]
            public string OrderCreatorRelation;

            /// <summary>
            /// Only grant permission when the diplomatic status between the market owner and the interacting player matches this value.
            /// Empty to match any diplomatic status.
            /// </summary>
            [XmlAttribute]
            public string MarketOwnerRelation;

            /// <summary>
            /// When true, only grant permissions when the interacting player is in the same location as this market.
            /// When false, the permission are granted no matter where the interacting player is.
            /// </summary>
            [XmlAttribute]
            public bool LocalOnly;

            [XmlElement("Allow")]
            public List<EquiMarketPermission> Allowed;
        }
    }
}