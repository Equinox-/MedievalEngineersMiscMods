using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Serialization;

namespace Equinox76561198048419394.Core.Misc
{
    public partial class EquiCurrencySystems
    {
        private MyDefinitionId? _defaultOverride;
        private EquiCurrencySystemDefinition _default;

        /// <summary>
        /// The default currency system if <see cref="_defaultOverride"/> is null.
        /// </summary>
        private EquiCurrencySystemDefinition _defaultFromDefinitions;

        /// <summary>
        /// The default currency system for this world.
        /// </summary>
        public EquiCurrencySystemDefinition Default
        {
            get
            {
                if (_default != null) return _default;
                if (_defaultOverride != null && !MyDefinitionManager.TryGet(_defaultOverride.Value, out _default))
                    this.GetLogger().Warning($"Unknown currency system {_defaultOverride.Value}");
                if (_default != null) return _default;
                return _default = _defaultFromDefinitions;
            }
        }

        /// <summary>
        /// Override the default currency system for a single save file.
        /// </summary>
        private void ChangeDefault(MyDefinitionId? value)
        {
            if (!MyMultiplayerModApi.Static.IsServer) return;
            _defaultOverride = value;
            _default = null;
            MyAPIGateway.Multiplayer?.RaiseEvent(this, a => a.BroadcastChangeDefault, (SerializableDefinitionId?) value);
        }

        [Event]
        [Reliable]
        [Broadcast]
        private void BroadcastChangeDefault([Nullable] SerializableDefinitionId? id)
        {
            _defaultOverride = id;
            _default = null;
        }
    }
}