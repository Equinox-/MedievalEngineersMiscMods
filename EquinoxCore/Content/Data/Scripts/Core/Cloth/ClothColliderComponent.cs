using System;
using System.Linq;
using System.Xml.Serialization;
using VRage;
using VRage.Components;
using VRage.Components.Entity.Animations;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Core.Cloth
{
    [MyComponent(typeof(MyObjectBuilder_ClothColliderComponent))]
    [MyDependency(typeof(MySkeletonComponent), Critical = false)]
    [MyDefinitionRequired(typeof(ClothColliderComponentDefinition))]
    public class ClothColliderComponent : MyEntityComponent
    {
        private MySkeletonComponent _skeleton;
        private Cloth.Sphere[] _spheres;
        private Cloth.Capsule[] _capsules;
        private int[] _sphereBones;
        private int[] _capsuleBones;
        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (ClothColliderComponentDefinition) def;
            Array.Resize(ref _spheres, Definition.SphereColliders.Length);
            Array.Resize(ref _capsules, Definition.CapsuleColliders.Length);
            if (!Definition.IsSkinned)
                return;
            Array.Resize(ref _sphereBones, Definition.SphereColliders.Length);
            Array.Resize(ref _capsuleBones, Definition.CapsuleColliders.Length);
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _skeleton = Container.Get<MySkeletonComponent>();
            if (_skeleton != null && Definition.IsSkinned)
            {
                _skeleton.OnReloadBones += BonesReloaded;
                _skeleton.OnPoseChanged += PoseChanged;
                BonesReloaded(_skeleton);
            }

            _collidersDirty = true;
        }

        private bool _collidersDirty = false;
        private void PoseChanged(MySkeletonComponent obj)
        {
            lock (this)
                _collidersDirty = true;
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (_skeleton != null)
            {
                _skeleton.OnReloadBones -= BonesReloaded;
                _skeleton.OnPoseChanged -= PoseChanged;
            }

            _skeleton = null;
        }

        private void BonesReloaded(MySkeletonComponent obj)
        {
            for (var i = 0; i < Definition.SphereColliders.Length; i++)
            {
                int idx;
                if (_skeleton.FindBone(Definition.SphereColliders[i].Bone, out idx) != null)
                    _sphereBones[i] = idx;
                else
                    _sphereBones[i] = -1;
            }
            
            for (var i = 0; i < Definition.CapsuleColliders.Length; i++)
            {
                int idx;
                if (_skeleton.FindBone(Definition.CapsuleColliders[i].Bone, out idx) != null)
                    _capsuleBones[i] = idx;
                else
                    _capsuleBones[i] = -1;
            }
        }

        public ClothColliderComponentDefinition Definition { get; private set; }

        public void GetColliders(out Cloth.Sphere[] spheres, out Cloth.Capsule[] capsules)
        {
            if (_collidersDirty)
            {
                lock (this)
                {
                    if (_collidersDirty)
                    {
                        UpdateColliders();
                        _collidersDirty = false;
                    }
                }
            }

            spheres = _spheres;
            capsules = _capsules;
        }
        
        private void UpdateColliders()
        {
            var skeletonBones = _skeleton?.CharacterBones;
            for (var i = 0; i < Definition.SphereColliders.Length; i++)
            {
                var sphere = Definition.SphereColliders[i];
                Cloth.Sphere? sphereResult = null;

                if (!string.IsNullOrEmpty(sphere.Bone) && skeletonBones != null && _sphereBones != null && i < _sphereBones.Length)
                {
                    var boneIdx = _sphereBones[i]; 
                    if (boneIdx >= 0 && boneIdx < skeletonBones.Length)
                    {
                        var bone = skeletonBones[boneIdx];
                        if (sphere.Center.HasValue)
                            sphereResult = new Cloth.Sphere(Vector3.Transform(
                                Vector3.Transform(sphere.Center.Value, bone.Transform.AbsoluteBindTransformInv),
                                bone.Transform.AbsoluteMatrix), sphere.Radius);
                        else
                            sphereResult = new Cloth.Sphere(bone.Transform.AbsoluteTransform.Position, sphere.Radius);
                    }
                }

                _spheres[i] = new Cloth.Sphere(sphere.Center ?? Vector3.Zero, sphere.Radius);
            }

            for (var i = 0; i < Definition.CapsuleColliders.Length; i++)
            {
                var capsule = Definition.CapsuleColliders[i];
                Vector3 a = capsule.A ?? Vector3.Zero, b = capsule.B ?? Vector3.Up;
                
                if (!string.IsNullOrEmpty(capsule.Bone) && skeletonBones != null && _sphereBones != null && i < _sphereBones.Length)
                {
                    var boneIdx = _capsuleBones[i]; 
                    if (boneIdx >= 0 && boneIdx < skeletonBones.Length)
                    {
                        var bone = skeletonBones[boneIdx];
                        var mod = bone.Transform.AbsoluteBindTransformInv * bone.Transform.AbsoluteMatrix;
                        a = capsule.A.HasValue ? Vector3.Transform(capsule.A.Value, mod) : bone.Transform.AbsoluteTransform.Position;
                        b = capsule.B.HasValue
                            ? Vector3.Transform(capsule.B.Value, mod)
                            : (bone.GetChildBone(0)?.Transform.AbsoluteTransform.Position ?? Vector3.Zero);
                    }
                }

                _capsules[i] =  new Cloth.Capsule(a, b, capsule.Radius);
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_ClothColliderComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_ClothColliderComponentDefinition))]
    public class ClothColliderComponentDefinition : MyEntityComponentDefinition
    {
        public MyObjectBuilder_ClothColliderComponentDefinition.Capsule[] CapsuleColliders { get; private set; }
        public MyObjectBuilder_ClothColliderComponentDefinition.Sphere[] SphereColliders { get; private set; }
        
        public bool IsSkinned { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_ClothColliderComponentDefinition) def;

            CapsuleColliders = ob.CapsuleColliders ?? new MyObjectBuilder_ClothColliderComponentDefinition.Capsule[0];
            SphereColliders = ob.SphereColliders ?? new MyObjectBuilder_ClothColliderComponentDefinition.Sphere[0];
            IsSkinned = CapsuleColliders.Any(x => !string.IsNullOrEmpty(x.Bone)) || SphereColliders.Any(x => !string.IsNullOrEmpty(x.Bone));
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_ClothColliderComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public class Capsule
        {
            public string Bone;
            public SerializableVector3? A, B;
            public float Radius;
        }

        public class Sphere
        {
            public string Bone;
            public SerializableVector3? Center;
            public float Radius;
        }

        [XmlElement("Capsule")]
        public Capsule[] CapsuleColliders;

        [XmlElement("Sphere")]
        public Sphere[] SphereColliders;
    }
}