using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Components;
using VRage.Components.Entity.Animations;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRageMath;
using VRageRender.Animations;

namespace Equinox76561198048419394.Core.Misc
{
    [MyComponent(typeof(MyObjectBuilder_EquiParentedSkeletonComponent))]
//    [UpdateBefore(typeof(MyAnimationControllerComponent))]
    [MyDependency(typeof(MySkeletonComponent), Critical = true)]
    public class EquiParentedSkeletonComponent : MyEntityComponent
    {
        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            Entity.Hierarchy.ParentChanged += ParentChanged;
            _mySkeleton = Entity.Get<MySkeletonComponent>();
            _mySkeleton.OnReloadBones += InvalidateBoneMapping;
            ParentChanged(Entity.Hierarchy, null, Entity.Hierarchy.Parent);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (_mySkeleton != null)
                _mySkeleton.OnReloadBones -= InvalidateBoneMapping;
            var h = Entity?.Hierarchy;
            if (h != null)
                h.ParentChanged -= ParentChanged;
            ParentChanged(Entity.Hierarchy, Entity.Hierarchy.Parent, null);
            _mySkeleton = null;
        }

        private MySkeletonComponent _mySkeleton;
        private MyEntity _parentCache;
        private MySkeletonComponent _parentSkeleton;

        private void ParentChanged(MyHierarchyComponent target, MyHierarchyComponent oldParent, MyHierarchyComponent newParentH)
        {
            var newParent = newParentH?.Entity;
            if (newParent == _parentCache)
                return;
            if (_parentCache != null)
            {
                _parentCache.Components.ComponentAdded -= CheckSkeleton;
                _parentCache.Components.ComponentRemoved -= CheckSkeleton;
            }

            _parentCache = newParent;
            if (_parentCache != null)
            {
                _parentCache.Components.ComponentAdded += CheckSkeleton;
                _parentCache.Components.ComponentRemoved += CheckSkeleton;
            }

            CheckSkeleton();
        }

        private void CheckSkeleton(MyEntityComponent e)
        {
            CheckSkeleton();
        }

        private void CheckSkeleton()
        {
            var newSkeleton = _parentCache?.Get<MySkeletonComponent>();
            if (_parentSkeleton == newSkeleton)
                return;
            if (_parentSkeleton != null)
            {
                _parentSkeleton.OnReloadBones -= InvalidateBoneMapping;
                _parentSkeleton.OnPoseChanged -= UpdatePose;
                _parentSkeleton = null;
            }

            _parentSkeleton = newSkeleton;
            if (_parentSkeleton == null)
                return;
            _parentSkeleton.OnReloadBones += InvalidateBoneMapping;
            _parentSkeleton.OnPoseChanged += UpdatePose;
            InvalidateBoneMapping(_parentSkeleton);
        }


        public EquiParentedSkeletonComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiParentedSkeletonComponentDefinition) def;
        }

        private int[] _boneMapping;

        private void InvalidateBoneMapping(MySkeletonComponent skeleton)
        {
            if (_parentSkeleton == null)
                return;
            Array.Resize(ref _boneMapping, _mySkeleton.CharacterBones.Length);
            for (var i = 0; i < _mySkeleton.CharacterBones.Length; i++)
            {
                var bone = _mySkeleton.CharacterBones[i];
                string source = null;
                if (Definition == null || (!Definition.ExplicitMapping.TryGetValue(bone.Name, out source) && Definition.AutomaticMapping))
                    source = bone.Name;
                var match = -1;
                MyCharacterBone matchBone = null;
                if (source != null)
                    for (var j = 0; j < _parentSkeleton.CharacterBones.Length; j++)
                    {
                        var parentBone = _parentSkeleton.CharacterBones[j];
                        if (source.Equals(parentBone.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            matchBone = parentBone;
                            match = j;
                            break;
                        }
                    }

                _boneMapping[i] = match;
            }

            UpdatePose(skeleton);
        }

        [FixedUpdate]
        private void Update()
        {
            UpdatePose(null);
            if (Definition == null || Definition.MoveToOrigin)
                Entity.PositionComp.LocalMatrix = Matrix.Identity;
        }

        private void UpdatePose(MySkeletonComponent skeleton)
        {
            if (_parentSkeleton == null || _boneMapping == null)
                return;
            for (var i = 0; i < _boneMapping.Length; i++)
            {
                var matched = _boneMapping[i];
                var dest = _mySkeleton.CharacterBones[i];
                if (matched != -1)
                {
                    var source = _parentSkeleton.CharacterBones[matched];
                    var srcT = source.Transform.AbsoluteTransform;
                    var dstParent = dest.Parent?.Transform.AbsoluteTransform ?? MyTransform.Identity;
                    dstParent.Rotation.Conjugate();
                    var tmpPos = srcT.Position - dstParent.Position;
                    var finalPos = Vector3.Transform(tmpPos, dstParent.Rotation);
                    var finalRot = dest.InheritRotation ? Quaternion.Multiply(dstParent.Rotation, srcT.Rotation) : srcT.Rotation;
                    dest.SetTransform(ref finalPos, ref finalRot);
                }

                dest.ComputeAbsoluteTransform(null, false);
            }

            _mySkeleton.MarkPoseChanged();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiParentedSkeletonComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiParentedSkeletonComponentDefinition))]
    public class EquiParentedSkeletonComponentDefinition : MyEntityComponentDefinition
    {
        public bool AutomaticMapping { get; private set; }

        public bool MoveToOrigin { get; private set; }

        /// <summary>
        /// Maps destination bone to source bone
        /// </summary>
        public readonly Dictionary<string, string> ExplicitMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiParentedSkeletonComponentDefinition) def;
            AutomaticMapping = ob.AutomaticMapping ?? false;
            MoveToOrigin = ob.MoveToOrigin ?? true;
            ExplicitMapping.Clear();
            if (ob.Mapping == null) return;

            foreach (var k in ob.Mapping)
            {
                if (string.IsNullOrWhiteSpace(k.Source))
                {
                    MyDefinitionErrors.Add(Package, $"Null or empty mapping source bone", LogSeverity.Warning);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(k.Dest))
                {
                    MyDefinitionErrors.Add(Package, $"Null or empty mapping destination bone", LogSeverity.Warning);
                    continue;
                }

                ExplicitMapping[k.Dest] = k.Source;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiParentedSkeletonComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("Mapping")]
        public PairInformation[] Mapping;

        [XmlElement]
        public bool? AutomaticMapping;

        [XmlElement]
        public bool? MoveToOrigin;

        public struct PairInformation
        {
            [XmlAttribute]
            public string Source;

            [XmlAttribute]
            public string Dest;
        }
    }
}