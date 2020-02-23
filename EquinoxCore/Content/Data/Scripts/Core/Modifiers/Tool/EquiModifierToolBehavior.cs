using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Harvest;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Medieval.GameSystems;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Block;
using VRage.Components.Entity;
using VRage.Components.Entity.CubeGrid;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.GUI.Crosshair;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Tool
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiModifierToolBehaviorDefinition))]
    public class EquiModifierToolBehavior : MyToolBehaviorBase
    {
        private EquiModifierToolBehaviorDefinition _definition;
        private NamedLogger _log = new NamedLogger(MySession.Static.Log, nameof(EquiModifierToolBehavior));

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiModifierToolBehaviorDefinition) definition;
        }

        private bool TryGetGridKey(out EquiGridModifierComponent grid, out EquiGridModifierComponent.BlockModifierKey key, out MatrixD worldMatrix)
        {
            var ent = Target.Entity;
            if (ent == null)
            {
                grid = null;
                key = default;
                worldMatrix = default;
                return false;
            }

            // First try: entity is the block
            {
                var block = ent.Get<MyBlockComponent>();
                if (block != null)
                {
                    grid = block.GridData?.Container?.Get<EquiGridModifierComponent>();
                    key = new EquiGridModifierComponent.BlockModifierKey(block.BlockId, MyStringHash.NullOrEmpty);
                    worldMatrix = block.GridData?.GetBlockWorldMatrix(block.Block, true) ?? ent.WorldMatrix;
                    return grid != null;
                }
            }
            // Second try: entity is an attached model
            {
                var block = ent.Parent?.Get<MyBlockComponent>();
                if (block != null)
                {
                    var id = ent.Parent?.Get<MyModelAttachmentComponent>()?.GetEntityAttachmentPoint(ent);
                    if (id.HasValue)
                    {
                        grid = block.GridData?.Container?.Get<EquiGridModifierComponent>();
                        key = new EquiGridModifierComponent.BlockModifierKey(block.BlockId, id.Value);
                        worldMatrix = ent.WorldMatrix;
                        return grid != null;
                    }
                }
            }
            {
                // Third try: target has a block associated:
                var block = Target.Block;
                if (block != null && ent.Components.TryGet(out MyGridDataComponent gridData))
                {
                    grid = ent.Get<EquiGridModifierComponent>();
                    key = new EquiGridModifierComponent.BlockModifierKey(block.Id, MyStringHash.NullOrEmpty);
                    worldMatrix = gridData.GetBlockWorldMatrix(block, true);
                    return grid != null;
                }
            }
            grid = null;
            key = default;
            worldMatrix = default;
            return false;
        }

        private bool TryGetAction(out EquiGridModifierComponent grid,
            out EquiGridModifierComponent.BlockModifierKey key,
            out ModifierContext context,
            out EquiModifierToolBehaviorDefinition.ModifierAction action)
        {
            if (!TryGetGridKey(out grid, out key, out var worldMatrix))
            {
                action = null;
                context = default;
                return false;
            }

            if (!grid.TryCreateContext(in key, grid.GetModifiers(in key), out context))
            {
                action = null;
                return false;
            }

            string material1 = null;

            var originalModel = context.OriginalModel ?? context.GetCurrentModel();
            var mm = MySession.Static.Components.Get<DerivedModelManager>();
            if (mm != null)
            {
                var caster = Holder.Get<MyCharacterDetectorComponent>();
                var worldToLocal = MatrixD.Invert(worldMatrix);
                var localRay = new Ray((Vector3) Vector3D.Transform(caster.StartPosition, worldToLocal),
                    (Vector3) Vector3D.TransformNormal(caster.Direction, worldToLocal));

                mm.GetMaterialBvh(originalModel)?.RayCast(in localRay, out _, out material1, out _);
            }

            foreach (var act in _definition.Actions)
                if (act.IsPermitted(in context, material1))
                {
                    action = act;
                    return true;
                }

            action = default;
            return false;
        }

        protected override bool ValidateTarget()
        {
            return TryGetAction(out _, out _, out _, out _);
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            switch (action)
            {
                case MyHandItemActionEnum.Primary:
                    return ValidateTarget();
                case MyHandItemActionEnum.None:
                case MyHandItemActionEnum.Secondary:
                case MyHandItemActionEnum.Tertiary:
                default:
                    return false;
            }
        }

        protected override void Hit()
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            base.UpdateDurability(-1);

            if (TryGetAction(out var grid, out var key, out _, out var action))
                HandleHit(grid, key, action);
        }

        private void HandleHit<TRtKey, TObKey>(EquiModifierStorageComponent<TRtKey, TObKey> storage, TRtKey key,
            EquiModifierToolBehaviorDefinition.ModifierAction action)
            where TRtKey : struct, IModifierRtKey<TObKey>, IEquatable<TRtKey>
            where TObKey : struct, IModifierObKey<TRtKey>, IMyRemappable
        {
            if (action.ItemActions.Count > 0)
            {
                var inv = Holder?.Get<MyInventoryBase>(action.Inventory);
                var player = MyAPIGateway.Players?.GetPlayerControllingEntity(Holder);
                var reporter = player != null
                    ? new ActionWithArg<IMyPlayer, ImmutableInventoryAction>(player, InventoryActionApplier.NotifyUserIncapableAction)
                    : (ActionWithArg<IMyPlayer, ImmutableInventoryAction>?) null;
                var creative = player.IsCreative();
                if (!creative && (inv == null
                                  || !InventoryActionApplier.CanApply(inv, action.ItemActions, errorReporter: reporter)
                                  || !InventoryActionApplier.Apply(inv, action.ItemActions, errorReporter: reporter)))
                {
                    if (DebugFlags.Debug(typeof(EquiModifierToolBehavior)))
                        _log.Info($"Failed to apply removal actions for {_definition.Id}: {string.Join(", ", action.ItemActions)}");
                    return;
                }
            }

            if (DebugFlags.Trace(typeof(EquiModifierToolBehavior)))
                _log.Info($"{(action.Remove ? "Removing" : "Adding")} {action.Modifier.Id} from {key} on {storage}");
            if (action.Remove)
                storage.RemoveModifier(key, action.Modifier);
            else
                storage.AddModifier(key, action.Modifier, action.ModifierData);
        }

        protected override void OnTargetEntityChanged(MyDetectedEntityProperties myEntityProps)
        {
            base.OnTargetEntityChanged(myEntityProps);
            SetTarget();
        }

        public override IEnumerable<string> GetHintTexts()
        {
            if (!ValidateTarget())
                yield break;
            if (TryGetAction(out _, out _, out _, out var action) && !string.IsNullOrEmpty(action.ActionHint))
                yield return action.ActionHint;
        }

        public override IEnumerable<MyCrosshairIconInfo> GetIconsStates()
        {
            if (TryGetAction(out _, out _, out _, out var action) && !string.IsNullOrEmpty(action.ActionHint))
                yield return new MyCrosshairIconInfo(action.ActionIcon);
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiModifierToolBehaviorDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiModifierToolBehaviorDefinition : MyToolBehaviorDefinition
    {
        public ListReader<ModifierAction> Actions { get; private set; }

        public class ModifierAction
        {
            private readonly EquiModifierToolBehaviorDefinition _owner;
            private readonly MyDefinitionId _modifier;
            private readonly string _modifierData;
            public readonly bool Remove;
            public readonly string ActionHint;
            public readonly MyStringHash ActionIcon;
            public readonly MyStringHash Inventory;
            public readonly ListReader<ImmutableInventoryAction> ItemActions;
            private readonly HashSetReader<string> _requireMaterial;
            private readonly HashSetReader<MyDefinitionId> _requireModifier;
            private readonly HashSetReader<MyDefinitionId> _prohibitModifier;

            private IModifierData _memorizedModifierData;
            private EquiModifierBaseDefinition _memorizedModifier;

            public EquiModifierBaseDefinition Modifier
            {
                get
                {
                    if (_memorizedModifier != null)
                        return _memorizedModifier;
                    _memorizedModifier = MyDefinitionManager.Get<EquiModifierBaseDefinition>(_modifier);
                    if (_memorizedModifier == null)
                        MyDefinitionErrors.Add(_owner.Package, $"Failed to find modifier {_modifier} for {_owner.Id}", LogSeverity.Critical);
                    return _memorizedModifier;
                }
            }

            public IModifierData ModifierData
            {
                get
                {
                    if (_memorizedModifierData != null || _modifierData == null)
                        return _memorizedModifierData;
                    return _memorizedModifierData = Modifier?.CreateData(_modifierData);
                }
            }


            public ModifierAction(EquiModifierToolBehaviorDefinition owner,
                MyObjectBuilder_EquiModifierToolBehaviorDefinition.ModifierAction builder)
            {
                _owner = owner;
                _modifier = builder.Modifier;
                _modifierData = builder.ModifierData;
                Remove = builder.Remove;
                ActionHint = builder.ActionHint;
                ActionIcon = MyStringHash.GetOrCompute(builder.ActionIcon);
                Inventory = MyStringHash.GetOrCompute(builder.Inventory);
                ItemActions = builder.ItemActions != null && builder.ItemActions.Length > 0
                    ? builder.ItemActions.Select(x => x.ToImmutable()).ToList()
                    : ListReader<ImmutableInventoryAction>.Empty;
                _requireMaterial = builder.RequireMaterial != null && builder.RequireMaterial.Length > 0
                    ? new HashSet<string>(builder.RequireMaterial.Select(string.Intern))
                    : default;
                _requireModifier = builder.RequireModifier != null && builder.RequireModifier.Length > 0
                    ? new HashSet<MyDefinitionId>(builder.RequireModifier.Select(x => (MyDefinitionId) x))
                    : default;
                _prohibitModifier = builder.ProhibitModifier != null && builder.ProhibitModifier.Length > 0
                    ? new HashSet<MyDefinitionId>(builder.ProhibitModifier.Select(x => (MyDefinitionId) x))
                    : default;
            }

            public bool IsPermitted(in ModifierContext ctx, string targetMaterial)
            {
                if (Remove != ctx.Modifiers.Contains(Modifier))
                    return false;
                if (!Modifier.CanApply(in ctx))
                    return false;
                if (_requireMaterial.Count > 0)
                {
                    if (targetMaterial == null || !_requireMaterial.Contains(targetMaterial))
                        return false;
                }

                if (_prohibitModifier.Count > 0)
                {
                    foreach (var m in ctx.Modifiers)
                    {
                        if (_prohibitModifier.Contains(m.Id))
                            return false;
                        foreach (var t in _prohibitModifier)
                            if (t.TypeId == typeof(MyObjectBuilder_ItemTagDefinition))
                                if (m.Tags.Contains(t.SubtypeId))
                                    return false;
                    }
                }

                if (_requireModifier.Count > 0)
                {
                    foreach (var m in ctx.Modifiers)
                    {
                        if (_requireModifier.Contains(m.Id))
                            return true;
                        foreach (var t in _requireModifier)
                            if (t.TypeId == typeof(MyObjectBuilder_ItemTagDefinition))
                                if (m.Tags.Contains(t.SubtypeId))
                                    return true;
                    }

                    return false;
                }

                return true;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiModifierToolBehaviorDefinition) builder;
            Actions = ob.Actions.Select(x => new ModifierAction(this, x)).ToList();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiModifierToolBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        [XmlElement("Action")]
        public ModifierAction[] Actions;

        public class ModifierAction
        {
            [XmlElement]
            public SerializableDefinitionId Modifier;

            [XmlElement]
            public string ModifierData;

            [XmlElement]
            [DefaultValue(false)]
            public bool Remove;

            [XmlElement]
            public string ActionHint;

            [XmlElement]
            public string ActionIcon;

            [XmlElement]
            public string Inventory;

            [XmlElement("ItemAction")]
            public InventoryActionBuilder[] ItemActions;

            [XmlElement]
            public string[] RequireMaterial;

            [XmlElement]
            public DefinitionTagId[] RequireModifier;

            [XmlElement]
            public DefinitionTagId[] ProhibitModifier;
        }
    }
}