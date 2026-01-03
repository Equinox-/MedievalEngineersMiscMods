using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.Components.Crafting.Power;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.ObjectBuilders.Inventory;
using VRage.Serialization;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Inventory
{
    [MyComponent(typeof(MyObjectBuilder_EquiItemGeneratorComponent))]
    [MyDependency(typeof(MyInventoryBase), Recursive = true)]
    [MyDependency(typeof(MyComponentEventBus))]
    [MyDependency(typeof(IPowerProvider))]
    public class EquiItemGeneratorComponent : MyEntityComponent, IMyComponentEventProvider
    {
        private const string EventGenerationStart = "GenerationStart";
        private const string EventGenerationEnd = "GenerationEnd";
        private const string EventGenerationHappened = "GenerationHappened";

        private MyInventoryBase _outputInventory;
        private string _outputInventoryKey;
        private TimeSpan _interval;
        private TimeSpan? _lastGeneration;
        private readonly List<ImmutableInventoryAction> _actions = new List<ImmutableInventoryAction>();

        private bool _running;
        private bool _updatingInventory;
        private bool _updatedInventory;
        private bool _hasImmediateUpdate;

        public TimeSpan Interval => _interval;

        public ListReader<ImmutableInventoryAction> Actions => _actions;

        public EquiItemGeneratorComponent() : this(null)
        {
        }

        public EquiItemGeneratorComponent(string outputInventoryKey)
        {
            _outputInventoryKey = outputInventoryKey;
        }

        public void UpdateSettings(TimeSpan? interval = null, List<ImmutableInventoryAction> actions = null)
        {
            if (interval != null)
                _interval = interval.Value;
            if (actions != null)
            {
                _actions.Clear();
                _actions.AddCollection(actions);
            }

            Reschedule();
        }

        [Automatic]
        private readonly MyComponentEventBus _eventBus = null;

        private const string Usage = "/item-gen [intervalSec] ([itemId] [amount] [mode])...\n /item-gen register [inventoryName]\n /item-gen unregister";

        public static bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            bool Respond(string response)
            {
                MyChatSystem.Static.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"),
                    0, response);
                return true;
            }

            if (!MyAPIGateway.Session.IsAdminModeEnabled(sender))
                return Respond("You need to enable Medieval Master to use this command.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerTarget = player?.ControlledEntity?.Get<MyTargetingComponentBase>();
            if (playerTarget == null)
                return Respond("You must have a character to use this command");

            var tokens = message.Split(' ');
            var entity = playerTarget.Detected.Entity;
            if (tokens.Length > 1)
            {
                if (tokens[1] == "register")
                {
                    entity.Components.RemoveAll<EquiItemGeneratorComponent>();
                    MyAPIGateway.Multiplayer?.RaiseEvent(entity, e => e.RemoveItemGenerator);
                    string inventory;
                    if (tokens.Length > 2)
                    {
                        inventory = tokens[2];
                        if (!entity.Components.TryGet<MyInventoryBase>(MyStringHash.GetOrCompute(inventory), out _))
                            return Respond($"Failed to find inventory {inventory} on {entity.DefinitionId}");
                    }
                    else
                    {
                        inventory = null;
                        if (!entity.Components.TryGet<MyInventoryBase>(out _))
                            return Respond($"Failed to find inventory on {entity.DefinitionId}");
                    }

                    entity.Components.Add(new EquiItemGeneratorComponent(inventory));
                    MyAPIGateway.Multiplayer?.RaiseEvent(entity, e => e.AddItemGenerator, inventory ?? "");
                    return Respond($"Added item generator to {entity.DefinitionId}");
                }

                if (tokens[1] == "unregister")
                {
                    if (!entity.Components.TryGet<EquiItemGeneratorComponent>(out _))
                        return Respond($"No item generator on {entity.DefinitionId}");
                    entity.Components.RemoveAll<EquiItemGeneratorComponent>();
                    MyAPIGateway.Multiplayer?.RaiseEvent(entity, e => e.RemoveItemGenerator);
                    return Respond($"Removed item generator from {entity.DefinitionId}");
                }
            }

            var itemGenerator = entity?.Get<EquiItemGeneratorComponent>();
            if (itemGenerator == null)
                return Respond("An entity with the EquiItemGenerator component must be targeted.  Did you mean to /item-gen register");

            if (tokens.Length >= 2)
            {
                if (!double.TryParse(tokens[1], out var intervalSec) || intervalSec <= 0)
                    return Respond($"Duration '{tokens[2]}' must be greater than zero");
                var actions = new List<ImmutableInventoryAction>();
                var i = 2;
                while (i < tokens.Length)
                {
                    var defParts = tokens[i].Split('/');
                    MyDefinitionId id;
                    switch (defParts.Length)
                    {
                        case 1:
                            if (!EquiDefinitions.TryGetItemDefinition(defParts[0], out var itemDef))
                                return Respond(
                                    $"Failed to find item definition '{defParts[0]}'.  You may need to provide an explicit typeId as 'typeId/subtypeId'");
                            id = itemDef.Id;
                            break;
                        case 2:
                            var type = defParts[0];
                            var subtype = defParts[1];
                            MyObjectBuilderType typeReal;
                            if (type.Equals("tag", StringComparison.OrdinalIgnoreCase))
                                typeReal = typeof(MyObjectBuilder_ItemTagDefinition);
                            else if (type.Equals("loot", StringComparison.OrdinalIgnoreCase))
                                typeReal = typeof(MyObjectBuilder_LootTableDefinition);
                            else if (!MyObjectBuilderType.TryParse(type, out typeReal))
                                return Respond($"Failed to parse definition '{tokens[i]}'");
                            id = new MyDefinitionId(typeReal, subtype);
                            if (!MyDefinitionManager.TryGet(id, out MyInventoryItemDefinition _)
                                && !MyDefinitionManager.TryGet(id, out MyLootTableDefinition _)
                                && !MyDefinitionManager.TryGet(id, out MyItemTagDefinition _))
                                return Respond($"Failed to find an item, tag, or loot table with id '{id.TypeId}/{id.SubtypeName}'");
                            break;
                        default:
                            return Respond($"Failed to parse item reference '{tokens[i]}'.  Must either be 'subtypeId' or 'typeId/subtypeId'");
                    }

                    i++;
                    if (i < tokens.Length && int.TryParse(tokens[i], out var amount))
                    {
                        if (amount <= 0)
                            return Respond($"Amount '{tokens[i]}' must be greater than zero");
                        i++;
                    }
                    else
                        amount = 1;

                    if (i < tokens.Length && Enum.TryParse(tokens[i], out ImmutableInventoryAction.InventoryActionMode mode))
                        i++;
                    else
                        mode = id.TypeId == typeof(MyObjectBuilder_LootTableDefinition)
                            ? ImmutableInventoryAction.InventoryActionMode.GiveTakeLootTable
                            : ImmutableInventoryAction.InventoryActionMode.GiveTakeItem;
                    actions.Add(new ImmutableInventoryAction(id, amount, mode));
                }

                itemGenerator.UpdateSettings(TimeSpan.FromSeconds(intervalSec), actions.Count > 0 ? actions : null);
            }

            var interval = itemGenerator.Interval;
            var msg = tokens.Length == 1 ? Usage + "\n" : "";
            msg += $" Target: {playerTarget.Detected.Entity.DefinitionId}";
            msg += $"\n Interval: {interval.Days * 24 + interval.Hours}:{interval.Minutes}:{interval.Seconds}.{interval.Milliseconds:D3}";
            foreach (var action in itemGenerator.Actions)
                msg += $"\n  {action.Mode} {action.Amount} of {action.TargetId}";
            return Respond(msg);
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (_outputInventory != null)
                _outputInventory.ContentsChanged -= ContentsChanged;
            base.OnBeforeRemovedFromContainer();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            if (_outputInventoryKey != null)
            {
                _outputInventory = Container.Get<MyInventoryBase>(MyStringHash.GetOrCompute(_outputInventoryKey));
                if (_outputInventory == null)
                {
                    this.GetLogger().Warning($"Failed to find inventory {_outputInventoryKey} in {Entity?.DefinitionId}");
                }
            }
            else
            {
                _outputInventory = Container.Get<MyInventoryBase>();
                if (_outputInventory == null)
                {
                    this.GetLogger().Warning($"Failed to find inventory in {Entity?.DefinitionId}");
                }
            }

            if (_outputInventory != null)
                _outputInventory.ContentsChanged += ContentsChanged;
            _running = false;
            _hasImmediateUpdate = false;
            if (MyMultiplayerModApi.Static.IsServer && _outputInventory != null)
                Reschedule();
        }

        private void ContentsChanged(MyInventoryBase obj)
        {
            _updatedInventory = true;
            // Don't try to reschedule during evaluation.
            if (_updatingInventory)
                return;
            if (Entity == null || !Entity.InScene || !MyMultiplayerModApi.Static.IsServer)
                return;
            if (!_running)
            {
                Reschedule();
            }
        }

        private void Reschedule()
        {
            if (_hasImmediateUpdate)
                return;
            RemoveScheduledUpdate(GiveAndSchedule);
            if (Entity == null || !Entity.InScene)
                return;
            AddScheduledCallback(GiveAndSchedule);
            _hasImmediateUpdate = true;
        }

        [Update(false)]
        private void GiveAndSchedule(long dt)
        {
            _hasImmediateUpdate = false;
            try
            {
                _updatingInventory = true;
                _updatedInventory = false;

                var now = Scene.Scheduler.CurrentUpdateTime;
                var last = _lastGeneration ?? now;
                if (_interval.Ticks > 0 && _actions.Count > 0)
                {
                    var deltaTicks = Math.Max(0, (now - last).Ticks);
                    var count = Math.Min(10_000, deltaTicks / _interval.Ticks);
                    var extra = TimeSpan.FromTicks(deltaTicks % _interval.Ticks);
                    var delay = MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS + (long)(_interval - extra).TotalMilliseconds - 1;
                    _lastGeneration = now - extra;
                    if (count <= 0)
                    {
                        if (!_running)
                            _eventBus?.Invoke(EventGenerationStart, true);
                        _running = true;
                        AddScheduledCallback(GiveAndSchedule, delay);
                        return;
                    }

                    if (count == 1)
                    {
                        InventoryActionApplier.Apply(_outputInventory, _actions, continueOnFailure: true);
                    }
                    else
                    {
                        using (PoolManager.Get(out List<ImmutableInventoryAction> actions))
                        {
                            actions.EnsureCapacity(_actions.Count);
                            foreach (var action in _actions)
                            {
                                actions.Add(new ImmutableInventoryAction(action.TargetId, (int)(action.Amount * count), action.Mode));
                            }

                            InventoryActionApplier.Apply(_outputInventory, actions, continueOnFailure: true);
                        }
                    }

                    if (_updatedInventory)
                    {
                        if (!_running)
                            _eventBus?.Invoke(EventGenerationStart, true);
                        _running = true;

                        _eventBus?.Invoke(EventGenerationHappened, true);
                        AddScheduledCallback(GiveAndSchedule, delay);
                        return;
                    }
                }

                _lastGeneration = null;
                if (_running)
                    _eventBus?.Invoke(EventGenerationEnd, true);
                _running = false;
            }
            finally
            {
                _updatingInventory = false;
            }
        }

        public bool HasEvent(string eventName)
        {
            switch (eventName)
            {
                case EventGenerationStart:
                case EventGenerationEnd:
                case EventGenerationHappened:
                    return true;
                default:
                    return false;
            }
        }

        public override bool IsSerialized => true;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiItemGeneratorComponent)base.Serialize(copy);
            ob.OutputInventory = _outputInventoryKey;
            ob.LastGeneration = _lastGeneration?.Ticks;
            ob.Interval = _interval.Ticks;
            ob.Actions = new List<InventoryActionBuilder>(_actions.Count);
            ob.CopyProtection = Entity.EntityId;
            foreach (var action in _actions)
                ob.Actions.Add(action.ToBuilder());
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiItemGeneratorComponent)builder;
            _outputInventoryKey = ob.OutputInventory;
            if (ob.LastGeneration.HasValue)
                _lastGeneration = TimeSpan.FromTicks(ob.LastGeneration.Value);
            _interval = TimeSpan.FromTicks(ob.Interval);
            if (ob.Actions != null)
                foreach (var action in ob.Actions)
                    _actions.Add(action.ToImmutable());
            // Clear interval if this is a copy so it doesn't continue working.
            // This prevents blueprints from copying the entire config.
            if (ob.CopyProtection != Entity.EntityId)
                _interval = TimeSpan.Zero;
        }
    }

    [StaticEventOwner]
    internal static class EquiItemGeneratorComponentSync
    {
        [Event]
        [Broadcast]
        [Reliable]
        public static void RemoveItemGenerator(this MyEntity entity)
        {
            entity.Components.RemoveAll<EquiItemGeneratorComponent>();
        }

        [Event]
        [Broadcast]
        [Reliable]
        public static void AddItemGenerator(this MyEntity entity, string outputInventory)
        {
            entity.Components.Add(new EquiItemGeneratorComponent(outputInventory == "" ? null : outputInventory));
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiItemGeneratorComponent : MyObjectBuilder_EntityComponent
    {
        // Entity ID of the owner.  Do NOT remap this value, because it is used to detect when the entity
        // is copied/blueprinted and prevent the generator from continuing to work.
        public long CopyProtection;

        [Nullable]
        public string OutputInventory;

        public long? LastGeneration;

        public long Interval;

        [XmlElement("Action")]
        public List<InventoryActionBuilder> Actions;
    }
}