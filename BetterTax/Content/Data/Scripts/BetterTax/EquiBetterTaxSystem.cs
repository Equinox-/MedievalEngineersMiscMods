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
    public partial class EquiBetterTaxSystem : MySessionComponent
    {
        [Automatic]
        private readonly MyAreaUpkeepSystem _vanilla = null;

        public EquiBetterTaxSystem()
        {
            _definition = new EquiBetterTaxSystemDefinition();
            _definition.Init(new MyObjectBuilder_EquiBetterTaxSystemDefinition(), null);
        }

        private EquiBetterTaxSystemDefinition _definition;

        public TimeSpan GetValue(EquiBetterTaxComponentDefinition definition, MyInventoryItemDefinition item)
        {
            var value = _vanilla?.GetTime(item.Id, 1) ?? TimeSpan.Zero;
            return MultiplyWithGranularity(value, definition.ValueMultiplier);
        }

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

        private static TimeSpan MultiplyWithGranularity(TimeSpan time, double amount) => EnforceGranularity(TimeSpan.FromTicks((long) (time.Ticks * amount)));

        #region Config

        protected override void LoadDefinition(MySessionComponentDefinition definition)
        {
            base.LoadDefinition(definition);
            _definition = (EquiBetterTaxSystemDefinition)definition;
        }

        #endregion
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterTaxSystem : MyObjectBuilder_SessionComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiBetterTaxSystemDefinition))]
    public class EquiBetterTaxSystemDefinition : MySessionComponentDefinition
    {
        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiBetterTaxSystemDefinition)builder;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterTaxSystemDefinition : MyObjectBuilder_SessionComponentDefinition
    {
    }
}