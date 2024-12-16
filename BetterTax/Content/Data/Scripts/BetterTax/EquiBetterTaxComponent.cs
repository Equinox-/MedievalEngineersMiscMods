using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.Components.Planet;
using Medieval.GameSystems;
using Medieval.ObjectBuilders.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components.Session;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.Network;
using VRage.ObjectBuilder.Merging;
using VRage.ObjectBuilders;
using VRage.Serialization;
using VRage.Session;

namespace Equinox76561198048419394.BetterTax
{
    [StaticEventOwner]
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyAreaUpkeepSystem), Critical = true)]
    public partial class EquiBetterTaxComponent : MySessionComponent
    {
        [Automatic]
        private readonly MyAreaUpkeepSystem _vanilla = null;

        public EquiBetterTaxComponent()
        {
            _definition = new EquiBetterTaxComponentDefinition();
            _definition.Init(new MyObjectBuilder_EquiBetterTaxComponentDefinition(), null);
        }

        private EquiBetterTaxComponentDefinition _definition;
        private MyObjectBuilder_EquiBetterTaxComponentConfig _savedConfig;
        private EquiBetterTaxComponentDefinition.Config _cachedConfig;

        private EquiBetterTaxComponentDefinition.Config Config
        {
            get
            {
                if (_cachedConfig != null) return _cachedConfig;
                return _cachedConfig = _definition.ResolveConfig(_savedConfig);
            }
        }

        public TimeSpan GetValue(MyInventoryItemDefinition item) => _vanilla?.GetTime(item.Id, 1) ?? TimeSpan.Zero;

        public TimeSpan MaxPayableTime => TimeSpan.FromMilliseconds(_vanilla.MaxPayableTime);

        public TimeSpan AreaExpirationTime(MyPlanetAreaUpkeepComponent upkeep, long areaId) => TimeSpan.FromMilliseconds(upkeep.GetExpirationTime(areaId));

        public TimeSpan AreaMaxPayment(MyPlanetAreaUpkeepComponent upkeep, long areaId)
        {
            if (upkeep.IsTaxFree(areaId)) return TimeSpan.Zero;
            var expiresAt = AreaExpirationTime(upkeep, areaId);
            var now = EnforceGranularity(Session.ElapsedGameTime);
            // If it's already expired the max payment is constant.
            if (expiresAt <= now) return MaxPayableTime;
            var maxPayment = now + MaxPayableTime - expiresAt;
            // If the expiry time is more than max payment in the future the max payment is zero.
            return maxPayment >= TimeSpan.Zero ? maxPayment : TimeSpan.Zero;
        }

        internal static readonly TimeSpan Granularity = TimeSpan.FromMilliseconds(1);

        private static TimeSpan EnforceGranularity(TimeSpan time) => TimeSpan.FromTicks(time.Ticks / Granularity.Ticks * Granularity.Ticks);

        private static TimeSpan DivideWithGranularity(TimeSpan time, int amount) => EnforceGranularity(TimeSpan.FromTicks(time.Ticks / amount));

        #region Config

        protected override void LoadDefinition(MySessionComponentDefinition definition)
        {
            base.LoadDefinition(definition);
            _definition = (EquiBetterTaxComponentDefinition)definition;
            _cachedConfig = null;
        }

        protected override bool IsSerialized => _savedConfig != null;

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_EquiBetterTaxComponent)base.Serialize();
            ob.Config = _savedConfig;
            return ob;
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var ob = (MyObjectBuilder_EquiBetterTaxComponent)objectBuilder;
            _savedConfig = ob.Config;
            _cachedConfig = null;
        }

        #endregion
    }

    public class EquiBetterTaxAreaSelection
    {
        public enum SelectionMode
        {
            Local,
            Connected,
            All,
        }

        [Serialize]
        public SelectionMode Mode;

        [Serialize]
        public bool IncludeFaction = true;

        [Serialize]
        public long PlanetId;

        [Serialize]
        public long AreaId;

        public void SelectedAreas(MyPlanetAreaOwnershipComponent planetOwnership, MyIdentity identity, HashSet<long> selectedAreas)
        {
            switch (Mode)
            {
                case SelectionMode.Local:
                    if (AreaQueries.IsPayable(planetOwnership, identity.Id, AreaId, IncludeFaction))
                        selectedAreas.Add(AreaId);
                    break;
                case SelectionMode.Connected:
                    AreaQueries.ConnectedPayableAreas(planetOwnership, selectedAreas, identity, AreaId, IncludeFaction);
                    break;
                case SelectionMode.All:
                    foreach (var area in planetOwnership.GetAreas(MyObjectBuilder_PlanetAreaOwnershipComponent.AreaState.Claimed))
                        if (AreaQueries.IsPayable(planetOwnership, identity.Id, area, IncludeFaction))
                            selectedAreas.Add(area);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterTaxComponent : MyObjectBuilder_SessionComponent
    {
        [XmlElement]
        public MyObjectBuilder_EquiBetterTaxComponentConfig Config;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiBetterTaxComponentDefinition))]
    public class EquiBetterTaxComponentDefinition : MySessionComponentDefinition
    {
        private MyObjectBuilder_EquiBetterTaxComponentConfig _config;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiBetterTaxComponentDefinition)builder;

            _config = ob.Config;
        }

        internal Config ResolveConfig(MyObjectBuilder_EquiBetterTaxComponentConfig overrides) => new Config(overrides, _config);

        internal class Config
        {
            public readonly bool SupportPayConnected;
            public readonly bool SupportPayAll;

            public Config(MyObjectBuilder_EquiBetterTaxComponentConfig first, MyObjectBuilder_EquiBetterTaxComponentConfig second)
            {
                SupportPayConnected = first?.SupportPayConnected ?? second?.SupportPayConnected ?? true;
                SupportPayAll = first?.SupportPayAll ?? second?.SupportPayConnected ?? true;
            }

            public bool IsSupported(EquiBetterTaxAreaSelection.SelectionMode mode)
            {
                switch (mode)
                {
                    case EquiBetterTaxAreaSelection.SelectionMode.Local:
                        return true;
                    case EquiBetterTaxAreaSelection.SelectionMode.Connected:
                        return SupportPayConnected;
                    case EquiBetterTaxAreaSelection.SelectionMode.All:
                        return SupportPayAll;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
                }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterTaxComponentDefinition : MyObjectBuilder_SessionComponentDefinition
    {
        [XmlElement]
        [FieldMerger(typeof(MyObjectBuilderMerger<MyObjectBuilder_EquiBetterTaxComponentConfig>))]
        public MyObjectBuilder_EquiBetterTaxComponentConfig Config;
    }

    public class MyObjectBuilder_EquiBetterTaxComponentConfig
    {
        /// <summary>
        /// Allow paying for areas connected to the opened claim block.
        /// </summary>
        [XmlElement]
        public bool? SupportPayConnected;

        /// <summary>
        /// Allow paying for any area the player owns.
        /// </summary>
        [XmlElement]
        public bool? SupportPayAll;
    }
}