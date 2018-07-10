using System;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using VRage.Components.Session;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRageMath;

namespace Equinox76561198048419394.Core.Power
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiSolarObservationComponentDefinition))]
    public class EquiSolarObservationComponentDefinition : MyEntityComponentDefinition
    {
        private BooleanMath.DelEvaluate<SolarObservation> _test;

        public int UpdateIntervalMs { get; private set; }
        public int UpdateVarianceMs { get; private set; }
        public ScheduleTransition Transition { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiSolarObservationComponentDefinition) def;

            _test = BooleanMath.And(ob.Rules.Select(x => x.Compile()));
            UpdateIntervalMs = ob.UpdateInterval.HasValue ? (int) ((TimeSpan) ob.UpdateInterval).TotalMilliseconds : 5000;
            UpdateVarianceMs = ob.UpdateIntervalVariance.HasValue ? (int) ((TimeSpan) ob.UpdateIntervalVariance).TotalMilliseconds : 0;
            Transition = ob.Transition ?? ScheduleTransition.Sparkle;
        }

        public enum ScheduleTransition
        {
            Immediate,
            Wave,
            WaveExpand,
            Sparkle
        }

        public bool Test(SolarObservation o)
        {
            return _test(o);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiSolarObservationComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public TimeDefinition? UpdateInterval;

        public TimeDefinition? UpdateIntervalVariance;

        public EquiSolarObservationComponentDefinition.ScheduleTransition? Transition;

        // implicit AND between rules at the top level

        [XmlElement("Rule", typeof(SolarRule))]
        [XmlElement("Any", typeof(AnyRule))]
        [XmlElement("All", typeof(AllRule))]
        public BinaryRule[] Rules;

        public abstract class BinaryRule
        {
            /// <summary>
            /// Is this rule inverted.
            /// </summary>
            [XmlAttribute("Inverted")]
            [DefaultValue(false)]
            public bool Inverted;

            protected abstract BooleanMath.DelEvaluate<SolarObservation> CompileInternal();

            public BooleanMath.DelEvaluate<SolarObservation> Compile()
            {
                var expr = CompileInternal();
                return Inverted ? expr.Inverted() : expr;
            }
        }

        public abstract class CompositeRule : BinaryRule
        {
            [XmlElement("Rule", typeof(SolarRule))]
            [XmlElement("Any", typeof(AnyRule))]
            [XmlElement("All", typeof(AllRule))]
            public BinaryRule[] Rules;
        }

        public class AnyRule : CompositeRule
        {
            protected override BooleanMath.DelEvaluate<SolarObservation> CompileInternal()
            {
                return BooleanMath.Or(Rules.Select(x => x.Compile()));
            }
        }

        public class AllRule : CompositeRule
        {
            protected override BooleanMath.DelEvaluate<SolarObservation> CompileInternal()
            {
                return BooleanMath.And(Rules.Select(x => x.Compile()));
            }
        }

        public class SolarRule : BinaryRule
        {
            // ReSharper disable UnassignedField.Global MemberCanBePrivate.Global
            /// <summary>
            /// Which planet, if any, does this rule apply to?
            /// </summary>
            public SerializableDefinitionId? Planet;

            /// <summary>
            /// Which latitude, if any, does this rule apply to?
            /// </summary>
            public SerializableRange? Latitude;

            /// <summary>
            /// Which time(s) of day, if any, does this rule apply to?
            /// </summary>
            public TimesOfDay? TimeOfDay;

            /// <summary>
            /// Which season(s), if any, does this rule apply to?
            /// </summary>
            public Season? Season;
            // ReSharper restore UnassignedField.Global MemberCanBePrivate.Global


            protected override BooleanMath.DelEvaluate<SolarObservation> CompileInternal()
            {
                var hasAnyFilter = false;
                MyDefinitionId? planet = null;
                if (Planet != null)
                {
                    hasAnyFilter = true;
                    planet = Planet.Value;
                }

                SerializableRange? latitude = null;
                if (Latitude != null)
                {
                    hasAnyFilter = true;
                    latitude = Latitude.Value;
                }

                TimesOfDay? timeOfDay = null;
                if (TimeOfDay != null)
                {
                    hasAnyFilter = true;
                    timeOfDay = TimeOfDay.Value;
                }

                Season? season = null;
                // ReSharper disable once InvertIf
                if (Season != null)
                {
                    hasAnyFilter = true;
                    season = Season.Value;
                }

                if (!hasAnyFilter)
                    return BooleanMath.Helper<SolarObservation>.True;

                return (latestObservation) =>
                {
                    if (latestObservation == null)
                        return false;

                    if (planet != null)
                    {
                        if (latestObservation.Planet == null)
                            return false;

                        var observedPlanet = latestObservation.Planet.DefinitionId;
                        var value = planet.Value;
                        if (observedPlanet == null || observedPlanet.Value != value)
                            return false;
                    }

// TODO super secret marker
                    if (latitude != null && !latitude.Value.ValueBetween((float) latestObservation.Latitude))
                        return false;
                    if (season != null && season.Value != latestObservation.Season) // TODO also super secret
                        return false;
                    return timeOfDay == null || timeOfDay.Value.HasFlagEq(latestObservation.TimeOfDay);
                };
            }
        }
    }
}