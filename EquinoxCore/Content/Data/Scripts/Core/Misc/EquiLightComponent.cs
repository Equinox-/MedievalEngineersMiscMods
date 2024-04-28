using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.EntityComponents;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.Animations;
using VRage.Entity.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Models;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.Models;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Lights;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Misc
{
    [MyComponent(typeof(MyObjectBuilder_EquiLightComponent))]
    [MyDependency(typeof(MySkeletonComponent))]
    [MyDependency(typeof(MyModelComponent))]
    [MyDependency(typeof(MyEntityStateComponent))]
    [MyDefinitionRequired(typeof(EquiLightComponentDefinition))]
    public class EquiLightComponent : MyEntityComponent
    {
        private readonly Dictionary<string, int> _boneCache = new Dictionary<string, int>();
        private readonly Dictionary<string, Matrix> _modelBoneCache = new Dictionary<string, Matrix>();
        private readonly Dictionary<string, uint> _lights = new Dictionary<string, uint>();

        [Automatic]
        private readonly MyModelComponent _modelComponent = null;

        [Automatic]
        private readonly MySkeletonComponent _skeletonComponent = null;

        [Automatic]
        private readonly MyPositionComponentBase _positionComponent = null;

        [Automatic]
        private readonly MyEntityStateComponent _stateComponent = null;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _positionComponent.OnPositionChanged += OnPositionChanged;
            if (_modelComponent != null)
                _modelComponent.ModelChanged += OnModelChanged;
            if (_skeletonComponent != null)
                _skeletonComponent.OnPoseChanged += OnPoseChanged;
            if (_stateComponent != null)
                _stateComponent.StateChanged += OnStateChanged;

            _boneCache.Clear();
            _modelBoneCache.Clear();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (_stateComponent != null)
                _stateComponent.StateChanged -= OnStateChanged;
            if (_skeletonComponent != null)
                _skeletonComponent.OnPoseChanged -= OnPoseChanged;
            if (_modelComponent != null)
                _modelComponent.ModelChanged -= OnModelChanged;
            _positionComponent.OnPositionChanged -= OnPositionChanged;
            base.OnBeforeRemovedFromContainer();
        }

        private void OnModelChanged(MyModelComponent.ModelChangedArgs args)
        {
            _boneCache.Clear();
            _modelBoneCache.Clear();
            UpdateLights();
        }

        private void OnStateChanged(MyStringHash oldState, MyStringHash newState)
        {
            UpdateLights();
        }

        private void OnPositionChanged(MyPositionComponentBase obj)
        {
            UpdateLights();
        }

        private void OnPoseChanged(MySkeletonComponent args)
        {
            UpdateLights();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            UpdateLights();
        }

        public override void OnRemovedFromScene()
        {
            foreach (var light in _lights.Values)
                MyRenderProxy.RemoveRenderObject(light);
            _lights.Clear();
            base.OnRemovedFromScene();
        }

        private void UpdateLights()
        {
            if (Entity == null || !Entity.InScene || Definition == null)
                return;
            foreach (var feature in Definition.Features.Values)
            {
                if (!_lights.TryGetValue(feature.Id, out var light))
                    _lights.Add(feature.Id, light = MyRenderProxy.CreateRenderLight($"{Entity}/{DebugName}/{feature.Id}"));

                var turnedOn = _stateComponent == null || feature.States.Count == 0 || feature.States.Contains(_stateComponent.CurrentState);
                var pos = ComputeFeaturePosition(feature);

                var msg = new UpdateRenderLightData
                {
                    ID = light,
                    Position = pos.Translation,
                    // Point light
                    PointLightOn = turnedOn && feature.PointLight.HasValue,
                    PointLight = feature.PointLight ?? default,
                    // Spot light
                    SpotLightOn = turnedOn && feature.SpotLight.HasValue,
                    SpotLight = feature.SpotLight ?? default,
                    ShadowDistance = feature.SpotLight?.ShadowsRange ?? 0,
                    CastShadows = feature.SpotLightCastsShadows,
                    ReflectorTexture = feature.SpotLightMask,
                    // Glares
                    Glare = feature.Flare,
                    // Not optimal, but not really a big deal.
                    PositionChanged = true,
                    AabbChanged = true,
                };
                msg.Glare.Enabled &= turnedOn;
                msg.SpotLight.Direction = (Vector3) pos.Forward;
                msg.SpotLight.Up = (Vector3) pos.Up;

                MyRenderProxy.UpdateRenderLight(ref msg);
            }
        }

        private MatrixD ComputeFeaturePosition(EquiLightComponentDefinition.ImmutableLightFeature feature)
        {
            var localMatrix = feature.Position;
            if (!string.IsNullOrEmpty(feature.Bone))
            {
                var boneMatrix = GetBoneMatrix(feature.Bone);
                if (boneMatrix != null && boneMatrix != Matrix.Zero)
                {
                    localMatrix *= boneMatrix.Value;
                }
            }

            return localMatrix * _positionComponent.WorldMatrix;
        }

        private Matrix? GetBoneMatrix(string bone)
        {
            if (_skeletonComponent != null)
            {
                if (!_boneCache.TryGetValue(bone, out var boneIndex))
                {
                    _skeletonComponent.FindBone(bone, out boneIndex);
                    _boneCache[bone] = boneIndex;
                }

                if (boneIndex >= 0)
                    return _skeletonComponent.RootBoneMatrixInv.GetOrientation() * _skeletonComponent.BoneAbsoluteTransforms[boneIndex];
                return null;
            }

            if (_modelBoneCache.TryGetValue(bone, out var cachedMatrix))
                return cachedMatrix;

            var realModel = _modelComponent.Model;
            if (realModel is MyFracturedCompoundModel fracturedCompoundModel)
                realModel = fracturedCompoundModel.OriginalModel;
            if (!(realModel is MyModel model))
                return null;
            var boneMatrix = Matrix.Identity;
            for (var boneObj = model.GetBoneByName(bone);
                boneObj != null;
                boneObj = model.Bones[boneObj.Parent])
            {
                boneMatrix *= MatrixD.CreateWorld(boneObj.Transform.Translation, boneObj.Transform.Forward,
                    boneObj.Transform.Up);
                if (boneObj.Parent < 0 || boneObj.Parent >= model.Bones.Length)
                    break;
            }

            return _modelBoneCache[bone] = boneMatrix;
        }

        public EquiLightComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiLightComponentDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiLightComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiLightComponentDefinition))]
    [MyDependency(typeof(EquiFlareDefinition))]
    public class EquiLightComponentDefinition : MyEntityComponentDefinition
    {
        private readonly Dictionary<string, ImmutableLightFeature> _features = new Dictionary<string, ImmutableLightFeature>();

        public DictionaryReader<string, ImmutableLightFeature> Features => _features;

        public sealed class ImmutableLightFeature
        {
            public readonly string Id;
            public readonly string Bone;
            public readonly HashSetReader<MyStringHash> States;
            public readonly MatrixD Position;

            public readonly MyLightLayout? PointLight;
            public readonly MySpotLightLayout? SpotLight;
            public readonly bool SpotLightCastsShadows;
            public readonly string SpotLightMask;

            public readonly MyFlareDesc Flare;

            internal delegate void DelReportError(string msg, LogSeverity level);

            internal ImmutableLightFeature(DelReportError reporter, MyObjectBuilder_EquiLightComponentDefinition.LightFeature feature)
            {
                Id = feature.Id ?? throw new NullReferenceException("Light features must have a ID");
                Bone = feature.Bone;
                if (feature.Point != null && feature.Point.IsEnabled)
                    PointLight = feature.Point.LightLayout;
                else
                    PointLight = null;

                if (feature.Spot != null && feature.Spot.IsEnabled)
                {
                    SpotLight = feature.Spot.SpotLightLayout;
                    SpotLightCastsShadows = feature.Spot.CastShadowsOrDefault;
                    SpotLightMask = feature.Spot.MaskOrDefault;
                }
                else
                {
                    SpotLight = null;
                    SpotLightCastsShadows = false;
                    SpotLightMask = null;
                }

                if (!string.IsNullOrEmpty(feature.Flare?.Definition))
                {
                    var flareDef = MyDefinitionManager.Get<EquiFlareDefinition>(feature.Flare.Definition);
                    if (flareDef == null)
                    {
                        reporter($"Light {Id} references unknown flare {feature.Flare.Definition}", LogSeverity.Error);
                        Flare.Enabled = false;
                    }
                    else
                    {
                        var intensityMultiplier = feature.Flare.IntensityMultiplier ?? 1;
                        var sizeMultiplier = feature.Flare.SizeMultiplier ?? 1;
                        Flare.Enabled = true;
                        Flare.Type = MyGlareTypeEnum.Distant;
                        Flare.MaxDistance = feature.Flare.MaxDistanceOverride ?? flareDef.MaxDistance;
                        Flare.Intensity = intensityMultiplier * flareDef.Intensity;
                        Flare.SizeMultiplier = sizeMultiplier * flareDef.Size;

                        Flare.QueryFreqMinMs = 150;
                        Flare.QueryFreqRndMs = 100;

                        Flare.QueryShift = 0;
                        Flare.QuerySize = feature.Flare.OcclusionQuerySize ?? .1f;

                        // ReSharper disable CompareOfFloatsByEqualityOperator
                        if (flareDef.Glares.Length > 0 && (intensityMultiplier != 1 || sizeMultiplier != 1))
                        {
                            Flare.Glares = new MySubGlare[flareDef.Glares.Length];
                            for (var i = 0; i < Flare.Glares.Length; i++)
                            {
                                ref var src = ref flareDef.Glares[i];
                                ref var dst = ref Flare.Glares[i];
                                dst = src;
                                dst.Size *= sizeMultiplier;
                                if (intensityMultiplier == 1) continue;
                                dst.OcclusionToIntensityCurve = new MySubGlare.KeyPoint[src.OcclusionToIntensityCurve.Length];
                                for (var j = 0; j < src.OcclusionToIntensityCurve.Length; j++)
                                {
                                    ref var srcStop = ref src.OcclusionToIntensityCurve[j];
                                    ref var dstStop = ref dst.OcclusionToIntensityCurve[j];
                                    dstStop = srcStop;
                                    dstStop.Intensity *= intensityMultiplier;
                                }
                            }
                        }
                        else
                        {
                            Flare.Glares = flareDef.Glares;
                        }
                        // ReSharper restore CompareOfFloatsByEqualityOperator
                    }
                }

                States = feature.States != null && feature.States.Length > 0
                    ? new HashSet<MyStringHash>(feature.States.Select(MyStringHash.GetOrCompute))
                    : default;

                var position = MatrixD.Identity;
                if (feature.Offset.HasValue)
                    position.Translation = feature.Offset.Value;
                if (feature.Direction.HasValue)
                {
                    var dir = (Vector3) feature.Direction.Value;
                    var len = dir.Normalize();
                    if (len <= 1e-3)
                        reporter($"Light {Id} must have a non-zero direction", LogSeverity.Error);

                    position.Forward = dir;
                    position.Up = Vector3.CalculatePerpendicularVector(dir);
                    position.Right = Vector3D.Cross(dir, position.Up);
                }

                Position = position;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiLightComponentDefinition) def;
            _features.Clear();
            if (ob.Features == null) return;
            foreach (var feature in ob.Features)
            {
                var built = new ImmutableLightFeature((msg, level) => MyDefinitionErrors.Add(ob.Package, msg, level), feature);
                if (_features.ContainsKey(built.Id))
                {
                    MyDefinitionErrors.Add(ob.Package, $"Light {built.Id} is duplicated on {ob.Id}", LogSeverity.Error);
                    continue;
                }

                _features.Add(built.Id, built);
            }
        }
    }

    // ReSharper disable MemberCanBePrivate.Global, UnassignedField.Global
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiLightComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public abstract class LightDataShared
        {
            private const float DefaultFalloff = 1.7f;
            private const float DefaultIntensity = 2f;
            private const float DefaultGlossFactor = 1f;
            private const float DefaultDiffuseFactor = 1f;
            private static readonly SerializableVector3 DefaultColor = new SerializableVector3(0, 0, 0);

            [XmlAttribute]
            public string Enabled;

            public float? Radius;
            public float? Falloff;
            public float? Intensity;
            public float? GlossFactor;
            public float? DiffuseFactor;
            public SerializableVector3? ColorRgb;

            [XmlIgnore]
            public abstract float RadiusOrDefault { get; }


            [XmlIgnore]
            public float IntensityOrDefault => Intensity ?? DefaultIntensity;

            [XmlIgnore]
            public MyLightLayout LightLayout => new MyLightLayout
            {
                // Position doesn't matter here -- renderer overrides it
                Range = RadiusOrDefault,
                Color = ((Vector3) (ColorRgb ?? DefaultColor)).ToLinearRGB() * IntensityOrDefault,
                Falloff = Falloff ?? DefaultFalloff,
                GlossFactor = GlossFactor ?? DefaultGlossFactor,
                DiffuseFactor = DiffuseFactor ?? DefaultDiffuseFactor
            };

            [XmlIgnore]
            public virtual bool IsEnabled => !("false".Equals(Enabled, StringComparison.OrdinalIgnoreCase)) && RadiusOrDefault > 0 && IntensityOrDefault > 0;
        }

        public class PointLightData : LightDataShared
        {
            private const float DefaultRadius = 10;

            [XmlIgnore]
            public override float RadiusOrDefault => Radius ?? DefaultRadius;
        }

        public class SpotLightData : LightDataShared
        {
            private const float DefaultRadius = 100;
            private const float DefaultConeDegrees = 30;
            private const bool DefaultCastShadows = true;
            private const string DefaultMask = "Textures/Lights/reflector_white.dds";

            public string Mask;

            public float? ConeDegrees;

            public float? ShadowRange;

            public bool? CastShadows;

            [XmlIgnore]
            public override float RadiusOrDefault => Radius ?? DefaultRadius;

            public override bool IsEnabled => base.IsEnabled && ConeDegrees > 0;

            public bool CastShadowsOrDefault => CastShadows ?? DefaultCastShadows;

            public string MaskOrDefault => Mask ?? DefaultMask;

            public MySpotLightLayout SpotLightLayout => new MySpotLightLayout
            {
                // Up and direction must be overriden later
                Light = LightLayout,
                ApertureCos = MathHelper.Clamp((float) Math.Cos(MathHelper.ToRadians(ConeDegrees ?? DefaultConeDegrees) / 2), .01f, .999f),
                ShadowsRange = ShadowRange ?? RadiusOrDefault
            };
        }

        public class FlareData
        {
            [XmlAttribute]
            public string Definition;

            public float? SizeMultiplier;

            public float? IntensityMultiplier;

            public float? MaxDistanceOverride;

            public float? OcclusionQuerySize;
        }

        public class LightFeature : IIdentifiable
        {
            [XmlAttribute]
            public string Id;

            public string Bone;

            [XmlElement("State")]
            public string[] States;

            public SerializableVector3D? Offset;

            public SerializableVector3? Direction;

            public PointLightData Point;

            public SpotLightData Spot;

            public FlareData Flare;

            string IIdentifiable.Id => Id;
        }

        [XmlElement("Light")]
        // [FieldMerger(typeof(IdentifiableListMerger<LightFeature, List<LightFeature>>))]
        public List<LightFeature> Features;
    }
    // ReSharper restore MemberCanBePrivate.Global, UnassignedField.Global
}