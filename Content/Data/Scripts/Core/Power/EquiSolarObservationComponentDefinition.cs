using System;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.GameSystems;
using Medieval.ObjectBuilders.Components;
using Sandbox.Game.SessionComponents;
using VRage.Components.Session;
using VRage.Game;
using VRage.Game.Definitions;
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
            public MyDefinitionId? Planet;

            /// <summary>
            /// Which biome, if any, does this rule apply to?
            /// </summary>
            public MyDefinitionId? Biome;

            /// <summary>
            /// Which latitude, if any, does this rule apply to?
            /// </summary>
            public SerializableRange? Latitude;

            /// <summary>
            /// Which longitude, if any, does this rule apply to?
            /// </summary>
            public SerializableRange? Longitude;

            /// <summary>
            /// Which altitude, if any, does this rule apply to?
            /// </summary>
            public SerializableRange? Altitude;

            /// <summary>
            /// Which altitude, expressed in planet ratio (0 is center, 1 is end of atmosphere), if any, does this rule apply to?
            /// </summary>
            public SerializableRange? HeightRatio;

            /// <summary>
            /// Which time(s) of day, if any, does this rule apply to?
            /// </summary>
            public TimesOfDay? TimeOfDay;

            /// <summary>
            /// Which season(s), if any, does this rule apply to?
            /// </summary>
            public Season? Season;

            /// <summary>
            /// What distance to the surface, if any, does this rule apply to?
            /// </summary>
            public SerializableRange? DistanceToSurface;

            /// <summary>
            /// Does this only play inside claimed areas, or only play outside of claimed areas?
            /// </summary>
            public bool? ClaimedArea;
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

                MyDefinitionId? biome = null;
                if (Biome != null)
                {
                    hasAnyFilter = true;
                    biome = Biome.Value;
                }

                SerializableRange? latitude = null;
                if (Latitude != null)
                {
                    hasAnyFilter = true;
                    latitude = Latitude.Value;
                }

                SerializableRange? longitude = null;
                if (Longitude != null)
                {
                    hasAnyFilter = true;
                    longitude = Longitude.Value;
                }

                SerializableRange? altitude = null;
                if (Altitude != null)
                {
                    hasAnyFilter = true;
                    altitude = Altitude.Value;
                }

                SerializableRange? heightRatio = null;
                if (HeightRatio != null)
                {
                    hasAnyFilter = true;
                    heightRatio = HeightRatio.Value;
                }

                SerializableRange? distanceToSurface = null;
                if (DistanceToSurface != null)
                {
                    hasAnyFilter = true;
                    distanceToSurface = DistanceToSurface.Value;
                }

                TimesOfDay? timeOfDay = null;
                if (TimeOfDay != null)
                {
                    hasAnyFilter = true;
                    timeOfDay = TimeOfDay.Value;
                }

                Season? season = null;
                if (Season != null)
                {
                    hasAnyFilter = true;
                    season = Season.Value;
                }

                bool? claimedArea = null;
                if (ClaimedArea != null)
                {
                    hasAnyFilter = true;
                    claimedArea = ClaimedArea.Value;
                }

                if (!hasAnyFilter)
                    return BooleanMath.Helper<SolarObservation>.True;

                return (value) =>
                {
                    if (planet != null)
                    {
                        if (value.Planet == null)
                            return false;
                        if (value.Planet.DefinitionId != planet.Value)
                            return false;
                    }

                    if (biome != null && (value.Biome == null || value.Biome.Value != biome.Value))
                        return false;

                    if (altitude != null && !altitude.Value.ValueBetween((float) value.Altitude))
                        return false;

                    if (heightRatio != null)
                    {
                        if (value.Planet == null)
                            return false;

                        var gen = value.Planet.Generator;
                        var radius = value.Planet.AverageRadius;
                        var maxHillHeight = gen.HillParams.Max * radius;
                        var minHillHeight = gen.HillParams.Min * radius;
                        var valueHeightRatio = (float) (value.Altitude - minHillHeight) / (maxHillHeight - minHillHeight);
                        if (!heightRatio.Value.ValueBetween(valueHeightRatio))
                            return false;
                    }

                    if (distanceToSurface != null && !distanceToSurface.Value.ValueBetween((float) value.DistanceToSurface))
                        return false;

                    if (latitude != null && !latitude.Value.ValueBetween((float) value.Latitude))
                        return false;

                    if (longitude != null && !longitude.Value.ValueBetween((float) value.Longitude))
                        return false;

                    if (season != null && !season.Value.HasFlagEq(value.Season))
                        return false;

                    if (timeOfDay != null && !timeOfDay.Value.HasFlagEq(value.TimeOfDay))
                        return false;

                    // ReSharper disable once InvertIf
                    if (claimedArea != null)
                    {
                        var areaInfo = MyAreaOwnershipSystem.Instance.GetAreaInfo(value.Location);
                        if (claimedArea.Value && (areaInfo == null || areaInfo.State == MyObjectBuilder_PlanetAreaOwnershipComponent.AreaState.Unclaimed))
                            return false;
                        if (!claimedArea.Value && areaInfo != null && areaInfo.State != MyObjectBuilder_PlanetAreaOwnershipComponent.AreaState.Unclaimed)
                            return false;
                    }

                    return true;
                };
            }
        }
    }
}