using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Entity.Animations;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;
using VRageRender.Animations;

namespace Equinox76561198048419394.Core.Stats
{
    [MyComponent(typeof(MyObjectBuilder_EquiVoxelEffectComponent))]
    [MyDefinitionRequired(typeof(EquiVoxelEffectComponentDefinition))]
    [MyDependency(typeof(MyEntityStatComponent), Critical = true)]
    [MyDependency(typeof(MyEntityTerrainProviderComponent), Critical = true)]
    [MyDependency(typeof(MySkeletonComponent), Critical = false)]
    public class EquiVoxelEffectComponent : MyEntityComponent
    {
        private EquiVoxelEffectComponentDefinition Definition { get; set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiVoxelEffectComponentDefinition) def;
        }

        private MyEntityStatComponent _statComponent;
        private MyEntityTerrainProviderComponent _terrainProvider;
        private MySkeletonComponent _skeleton;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!IsServer)
                return;
            if (!Container.TryGet(out _terrainProvider))
                return;
            if (!Container.TryGet(out _statComponent))
                return;
            Container.TryGet(out _skeleton);
            if (_skeleton != null)
                _skeleton.OnReloadBones += SkeletonOnOnReloadBones;
            AddScheduledUpdate(AddEffect, 250L);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (!IsServer)
                return;
            RemoveScheduledUpdate(AddEffect);
            _terrainProvider = null;
            _statComponent = null;
            if (_skeleton != null)
                _skeleton.OnReloadBones -= SkeletonOnOnReloadBones;
            _skeleton = null;
        }


        private void SkeletonOnOnReloadBones(MySkeletonComponent obj)
        {
            _cachedBoneIndex = null;
        }

        private int? _cachedBoneIndex;

        [Update(false)]
        private void AddEffect(long dt)
        {
            if (_terrainProvider == null || _statComponent == null)
                return;
            var bonePos = Vector3.Zero;
            var boneIndex = 0;
            if (_skeleton != null && !string.IsNullOrEmpty(Definition.SourceBone))
            {
                MyCharacterBone bone = null;
                if (_cachedBoneIndex.HasValue)
                {
                    if (_cachedBoneIndex.Value >= 0 && _cachedBoneIndex.Value < _skeleton.CharacterBones.Length)
                    {
                        boneIndex = _cachedBoneIndex.Value;
                        bone = _skeleton.CharacterBones[boneIndex];
                    }
                }
                else
                {
                    bone = _skeleton.FindBone(Definition.SourceBone, out boneIndex);
                    _cachedBoneIndex = bone != null ? boneIndex : -1;
                    boneIndex = Math.Max(0, boneIndex);
                }

                if (bone != null)
                    bonePos = bone.Transform.AbsoluteTransform.Position;
            }

            var contact = _terrainProvider.GetMaterial(boneIndex, bonePos);
            if (!contact.HasValue)
                return;
            var contactMtl = contact.Value;
            IReadOnlyCollection<MyDefinitionId> effects;
            if (!Definition.Effects.TryGetValue(contactMtl, out effects)) return;
            var hitInfo = _terrainProvider.GetTerrainHitInfo(boneIndex, bonePos);
            foreach (var e in effects)
                _statComponent.AddEffect(e, hitInfo?.LastHitEntity?.EntityId ?? 0);
        }

        private static bool IsServer => MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiVoxelEffectComponent : MyObjectBuilder_EntityComponent
    {
    }
}