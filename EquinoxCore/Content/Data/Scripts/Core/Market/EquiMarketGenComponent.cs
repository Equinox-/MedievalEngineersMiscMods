using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Serialization;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiMarketGenComponent))]
    [MyDependency(typeof(EquiMarketStorageComponent), Critical = true)]
    public partial class EquiMarketGenComponent : MyEntityComponent
    {
        [Automatic]
        private readonly EquiMarketStorageComponent _storage = null;

        internal class EquiMarketGenOrderSet
        {
            public readonly EquiMarketGenOrderSetDefinition Definition;
            public readonly bool Inline;
            public uint Multiplier;
            public readonly Dictionary<MyDefinitionId, EquiMarketGenOrderState> State;

            public EquiMarketGenOrderSet(EquiMarketGenOrderSetDefinition definition, bool inline)
            {
                Definition = definition;
                Inline = inline;
                Multiplier = 1;
                State = new Dictionary<MyDefinitionId, EquiMarketGenOrderState>();
            }

            public static EquiMarketGenOrderSet Deserialize(NamedLogger logger, MyObjectBuilder_EquiMarketGenComponent.EquiMarketGenOrderSet ob)
            {
                EquiMarketGenOrderSet result;
                switch (ob.Inline)
                {
                    case MyObjectBuilder_EquiMarketGenOrderSetConfig orderOb:
                    {
                        var def = new EquiMarketGenOrderSetDefinition();
                        def.InitInternal(orderOb);
                        result = new EquiMarketGenOrderSet(def, true);
                        break;
                    }
                    case EquiMarketGenOrderSetDefinition orderDef:
                        result = new EquiMarketGenOrderSet(orderDef, true);
                        break;
                    default:
                    {
                        if (MyDefinitionManager.TryGet(ob.Referenced, out EquiMarketGenOrderSetDefinition def))
                            result = new EquiMarketGenOrderSet(def, false);
                        else
                        {
                            logger.Warning($"Reference to unknown market order set {ob.Referenced}");
                            return null;
                        }

                        break;
                    }
                }

                result.Multiplier = ob.Multiplier;
                EquiMarketGenOrderState.Deserialize(result.State, ob.OrderStates);
                return result;
            }

            public MyObjectBuilder_EquiMarketGenComponent.EquiMarketGenOrderSet Serialize() => new MyObjectBuilder_EquiMarketGenComponent.EquiMarketGenOrderSet
            {
                Multiplier = Multiplier,
                OrderStates = EquiMarketGenOrderState.Serialize(State),
                Referenced = Definition.Id,
                Inline = Inline ? Definition : null,
            };
        }


        private TimeSpan? _lastGeneration;
        private readonly List<EquiMarketGenOrderSet> _orderSets = new List<EquiMarketGenOrderSet>();
        private List<EquiMarketGenOrderState> _abandonedStates;

        private long _identityId;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            // Clean up the abandoned orders if necessary.
            if (_abandonedStates == null) return;
            foreach (var state in _abandonedStates)
                state.RemoveOrders(_storage);
            _abandonedStates = null;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            Reschedule();
        }

        private void Reschedule()
        {
            RemoveScheduledUpdate(Update);
            var minInterval = TimeSpan.MaxValue;
            foreach (var order in _orderSets)
                if (order.Definition.Interval < minInterval)
                    minInterval = order.Definition.Interval;
            if (minInterval == TimeSpan.MaxValue)
                return;
            AddScheduledUpdate(Update, (long)minInterval.TotalMilliseconds);
        }

        [Update(false)]
        private void Update(long dt)
        {
            var now = Scene.Scheduler.CurrentUpdateTime;
            var last = _lastGeneration ?? now;
            _lastGeneration = now;

            var identity = MyIdentities.Static?.GetIdentity(_identityId);
            if (identity == null) return;
            foreach (var order in _orderSets)
            {
                var def = order.Definition;
                if (now.Ticks / def.Interval.Ticks != last.Ticks / def.Interval.Ticks)
                    def.Apply(
                        _storage,
                        order.State,
                        order.Multiplier * def.Amount,
                        order.Multiplier * def.Stockpile,
                        identity);
            }
        }

        public override bool IsSerialized => _orderSets.Count > 0;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiMarketGenComponent)base.Serialize(copy);
            ob.Identity = _identityId;
            ob.LastGeneration = _lastGeneration?.Ticks;
            ob.OrderSets = _orderSets.Select(x => x.Serialize()).ToList();
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiMarketGenComponent)builder;
            _orderSets.Clear();
            _lastGeneration = ob.LastGeneration.HasValue ? (TimeSpan?)TimeSpan.FromTicks(ob.LastGeneration.Value) : null;
            _identityId = ob.Identity;
            if (ob.OrderSets == null) return;
            foreach (var order in ob.OrderSets)
            {
                var deserialized = EquiMarketGenOrderSet.Deserialize(this.GetLogger(), order);
                if (deserialized != null)
                    _orderSets.Add(deserialized);
                else if (order.OrderStates != null)
                {
                    // Store the abandoned order states so they can be removed once the market storage is available.
                    if (_abandonedStates == null) _abandonedStates = new List<EquiMarketGenOrderState>();
                    foreach (var state in order.OrderStates)
                        _abandonedStates.Add(EquiMarketGenOrderState.Deserialize(state));
                }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketGenComponent : MyObjectBuilder_EntityComponent
    {
        // None of this should be included on client/server sync.

        [XmlElement]
        [NoSerialize]
        public long Identity;

        [XmlElement]
        [NoSerialize]
        public long? LastGeneration;

        [XmlElement("OrderSet")]
        [NoSerialize]
        public List<EquiMarketGenOrderSet> OrderSets;

        public class EquiMarketGenOrderSet
        {
            /// <summary>
            /// Multiply the amounts and stockpile for the order set.
            /// </summary>
            [XmlAttribute]
            public uint Multiplier = 1;

            [XmlIgnore]
            public object Inline;

            [XmlElement]
            public SerializableDefinitionId Referenced;

            public bool ShouldSerializeReferenced() => Inline == null;

            [XmlElement("Inline")]
            public MyObjectBuilder_EquiMarketGenOrderSetConfig InlineOb
            {
                get
                {
                    switch (Inline)
                    {
                        case MyObjectBuilder_EquiMarketGenOrderSetConfig ob:
                            return ob;
                        case EquiMarketGenOrderSetDefinition def:
                            return def.SerializeConfig();
                        default:
                            return null;
                    }
                }
                set => Inline = value;
            }

            public bool ShouldSerializeInlineOb() => Inline != null;

            [XmlElement("Order")]
            public List<MyObjectBuilder_EquiMarketGenOrderState> OrderStates;
        }
    }
}