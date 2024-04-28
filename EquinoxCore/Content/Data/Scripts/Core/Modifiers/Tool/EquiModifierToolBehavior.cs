using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Equinox76561198048419394.Core.Util;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Block;
using VRage.Components.Entity;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Session;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.GUI.Crosshair;
using VRage.Library.Collections;
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
        private readonly NamedLogger _log = new NamedLogger(MySession.Static.Log, nameof(EquiModifierToolBehavior));

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiModifierToolBehaviorDefinition) definition;
        }

        private readonly struct CandidateDebug
        {
            public readonly string Source;
            public readonly ModifiableCandidate Candidate;
            public readonly string Material;
            public readonly float Distance;

            public CandidateDebug(string source, ModifiableCandidate candidate, string material, float distance)
            {
                Source = source;
                Candidate = candidate;
                Material = material;
                Distance = distance;
            }
        }

        private readonly struct ModifiableCandidate
        {
            public readonly EquiGridModifierComponent Host;
            public readonly EquiGridModifierComponent.BlockModifierKey Key;
            public readonly MatrixD InvWorldMatrix;

            public ModifiableCandidate(EquiGridModifierComponent host, EquiGridModifierComponent.BlockModifierKey key, in MatrixD invWorldMatrix)
            {
                Host = host;
                Key = key;
                InvWorldMatrix = invWorldMatrix;
            }

            public InterningBag<EquiModifierBaseDefinition> Modifiers => Host.GetModifiers(in Key);

            public bool TryCreateContext(out ModifierContext context) => Host.TryCreateContext(in Key, Modifiers, out context);

            public void AddModifier(EquiModifierBaseDefinition modifier, IModifierData data = null, bool recursive = false)
            {
                Host.AddModifier(in Key, modifier, data, recursive);
            }

            public void RemoveModifier(EquiModifierBaseDefinition modifier, bool recursive = false)
            {
                Host.RemoveModifier(in Key, modifier, recursive);
            }

            public void WriteFriendlyName(StringBuilder sb)
            {
                var blockData = Host.Container?.Get<MyGridDataComponent>()?.GetBlock(Key.Block)?.Definition;
                if (blockData != null)
                    sb.Append(blockData.DisplayNameText);
                else
                    sb.Append(Key.Block);
                if (Key.AttachmentPoint != MyStringHash.NullOrEmpty)
                    sb.Append(", ").Append(Key.AttachmentPoint);
            }
        }

        private static bool TryGetCandidateForBlock(MyGridDataComponent grid, MyBlock block, out ModifiableCandidate candidate)
        {
            var host = grid.Container?.Get<EquiGridModifierComponent>();
            if (host == null)
            {
                candidate = default;
                return false;
            }

            candidate = new ModifiableCandidate(host, new EquiGridModifierComponent.BlockModifierKey(block.Id, MyStringHash.NullOrEmpty),
                MatrixD.Invert(grid.GetBlockWorldMatrix(block, true)));
            return true;
        }

        private static bool TryGetCandidateForAttachment(
            MyEntity ent,
            out ModifiableCandidate candidate)
        {
            candidate = default;
            var block = ent.Parent?.Get<MyBlockComponent>();
            if (block == null)
                return false;
            var id = ent.Parent?.Get<MyModelAttachmentComponent>()?.GetEntityAttachmentPoint(ent);
            if (!id.HasValue)
                return false;
            var grid = block.GridData?.Container?.Get<EquiGridModifierComponent>();
            if (grid == null)
                return false;
            candidate = new ModifiableCandidate(grid, new EquiGridModifierComponent.BlockModifierKey(block.BlockId, id.Value),
                ent.PositionComp.WorldMatrixInvScaled);
            return true;
        }

        private bool TryGetCandidateFromPhysics(out ModifiableCandidate candidate)
        {
            candidate = default;
            var ent = Target.Entity;
            if (ent == null)
                return false;

            // First try: entity is the block
            {
                var block = ent.Get<MyBlockComponent>();
                if (block != null && TryGetCandidateForBlock(block.GridData, block.Block, out candidate))
                    return true;
            }
            // Second try: entity is an attached model
            if (TryGetCandidateForAttachment(ent, out candidate))
                return true;
            {
                // Third try: target has a block associated:
                var block = Target.Block;
                if (block != null && ent.Components.TryGet(out MyGridDataComponent gridData) && TryGetCandidateForBlock(gridData, block, out candidate))
                    return true;
            }
            candidate = default;
            return false;
        }

        private bool TryGetActionFromPhysics(out ModifiableCandidate candidate,
            out ModifierContext context,
            out EquiModifierToolBehaviorDefinition.ModifierAction action,
            List<CandidateDebug> debug)
        {
            if (!TryGetCandidateFromPhysics(out candidate))
            {
                action = null;
                context = default;
                return false;
            }

            if (!candidate.TryCreateContext(out context))
            {
                action = null;
                return false;
            }

            string material = null;
            var distance = Target.HitDistance;

            var originalModel = context.OriginalModel ?? context.GetCurrentModel();
            var mm = MySession.Static.Components.Get<DerivedModelManager>();
            if (mm != null)
            {
                var caster = Holder.Get<MyCharacterDetectorComponent>();
                var localRay = new Ray((Vector3) Vector3D.Transform(caster.StartPosition, in candidate.InvWorldMatrix),
                    (Vector3) Vector3D.TransformNormal(caster.Direction, candidate.InvWorldMatrix));

                var bvh = mm.GetMaterialBvh(originalModel);
                if (bvh != null && bvh.RayCast(in localRay, out _, out material, out var bvhDistance))
                    distance = bvhDistance;
            }

            debug?.Add(new CandidateDebug("physics", candidate, material, distance));

            return TryGetPermittedAction(in context, material, out action);
        }

        private bool TryGetActionFromModelBvh(
            out ModifiableCandidate candidate,
            out ModifierContext context,
            out EquiModifierToolBehaviorDefinition.ModifierAction action,
            List<CandidateDebug> debug)
        {
            candidate = default;
            context = default;
            action = null;
            var mm = MySession.Static.Components.Get<DerivedModelManager>();
            if (mm == null)
                return false;

            var caster = Holder.Get<MyCharacterDetectorComponent>();
            var bestDistance = _definition.MeshIntersectionDistance;
            var worldLine = new LineD(caster.StartPosition, caster.StartPosition + caster.Direction * bestDistance);
            using (PoolManager.Get(out List<MyLineSegmentOverlapResult<ModifiableCandidate>> candidates))
            {
                using (PoolManager.Get(out List<MyLineSegmentOverlapResult<MyEntity>> entities))
                {
                    MyGamePruningStructure.GetAllEntitiesInRay(in worldLine, entities);
                    foreach (var overlap in entities)
                    {
                        var entity = overlap.Element;
                        var invMatrix = entity.PositionComp.WorldMatrixNormalizedInv;
                        var localLine = new LineD(Vector3D.Transform(worldLine.From, in invMatrix), Vector3D.Transform(worldLine.To, in invMatrix));
                        var localLineSingle = (Line) localLine;
                        if (entity.PositionComp.LocalAABB.Intersects(localLineSingle, out var entityHitDist)
                            && TryGetCandidateForAttachment(entity, out var tmpCandidate))
                        {
                            candidates.Add(new MyLineSegmentOverlapResult<ModifiableCandidate>
                            {
                                Distance = Math.Min(entityHitDist, overlap.Distance),
                                Element = tmpCandidate,
                            });
                            continue;
                        }

                        if (!entity.Components.TryGet(out MyGridDataComponent gridDataComponent))
                            continue;
                        using (PoolManager.Get(out List<MyBlock> blocks))
                        {
                            gridDataComponent.GetBlocksInLine(localLine, blocks);
                            foreach (var block in blocks)
                                if (gridDataComponent.GetBlockLocalBounds(block).Intersects(localLineSingle, out var distance)
                                    && TryGetCandidateForBlock(gridDataComponent, block, out tmpCandidate))
                                    candidates.Add(new MyLineSegmentOverlapResult<ModifiableCandidate>
                                    {
                                        Distance = distance,
                                        Element = tmpCandidate
                                    });
                        }
                    }
                }

                candidates.Sort(MyLineSegmentOverlapResult<ModifiableCandidate>.DistanceComparer);
                foreach (var overlap in candidates)
                {
                    // Stop trying if the whole entity is farther away
                    if (overlap.Distance > bestDistance)
                        break;
                    var tmpCandidate = overlap.Element;
                    if (!tmpCandidate.TryCreateContext(out var tmpContext))
                        continue;
                    var localRay = new Ray((Vector3) Vector3D.Transform(caster.StartPosition, in tmpCandidate.InvWorldMatrix),
                        (Vector3) Vector3D.TransformNormal(caster.Direction, tmpCandidate.InvWorldMatrix));
                    var bvh = mm.GetMaterialBvh(tmpContext.OriginalModel ?? tmpContext.GetCurrentModel());
                    if (bvh == null || !bvh.RayCast(in localRay, out _, out var material1, out var dist, bestDistance) || dist > bestDistance)
                    {
                        debug?.Add(new CandidateDebug("mesh", tmpCandidate, null, (float) overlap.Distance));
                        continue;
                    }

                    debug?.Add(new CandidateDebug("mesh", tmpCandidate, material1, dist));
                    if (!TryGetPermittedAction(in tmpContext, material1, out var tmpAction))
                        continue;
                    bestDistance = dist;
                    candidate = tmpCandidate;
                    context = tmpContext;
                    action = tmpAction;
                }
            }

            return action != null;
        }

        private bool TryGetActionAll(out ModifiableCandidate candidate,
            out ModifierContext context,
            out EquiModifierToolBehaviorDefinition.ModifierAction action,
            List<CandidateDebug> debug)
        {
            return TryGetActionFromModelBvh(out candidate, out context, out action, debug)
                   || TryGetActionFromPhysics(out candidate, out context, out action, debug);
        }

        private bool TryGetPermittedAction(in ModifierContext context, string material1, out EquiModifierToolBehaviorDefinition.ModifierAction action)
        {
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
            return TryGetActionAll(out _, out _, out _, null);
        }

        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        protected override bool Start(MyHandItemActionEnum action)
        {
            switch (action)
            {
                case MyHandItemActionEnum.Primary:
                case MyHandItemActionEnum.Secondary:
                    return ValidateTarget();
                case MyHandItemActionEnum.Tertiary:
                    return true;
                case MyHandItemActionEnum.None:
                default:
                    return false;
            }
        }

        private bool ApplyRecursive => Modified;

        protected override void Hit()
        {
            base.UpdateDurability(-1);
            if (!IsLocallyControlled)
                return;

            if (ActiveAction == MyHandItemActionEnum.Tertiary || ActiveAction == MyHandItemActionEnum.Secondary)
            {
                if (_hintInfo == null)
                    _hintInfo = MyAPIGateway.Utilities.CreateNotification("", disappearTimeMs: 5000);

                var sb = new StringBuilder();

                var debug = ActiveAction == MyHandItemActionEnum.Tertiary ? PoolManager.Get<List<CandidateDebug>>() : null;

                if (TryGetActionAll(out var candidate, out _, out var action, debug))
                {
                    sb.Append("Will ").Append(action.Remove ? "remove" : "add");
                    sb.Append(" ");
                    sb.Append(action.Modifier.Id.SubtypeName);
                    if (action.ModifierData != null)
                        sb.Append(" (").Append(action.ModifierData).Append(")");
                    sb.Append(action.Remove ? " from " : " to ");
                    candidate.WriteFriendlyName(sb);
                    if (ApplyRecursive)
                        sb.Append(", recursively");
                }

                if (debug != null)
                {
                    debug.Sort((a, b) =>
                    {
                        var aHasMtl = a.Material != null;
                        var bHasMtl = b.Material != null;
                        if (aHasMtl != bHasMtl) return aHasMtl ? -1 : 1;
                        return a.Distance.CompareTo(b.Distance);
                    });
                    foreach (var debugCandidate in debug)
                    {
                        sb.Append("\n").Append(debugCandidate.Source).Append(": ");
                        debugCandidate.Candidate.WriteFriendlyName(sb);
                        sb.Append(": ").Append(debugCandidate.Material ?? "no material data");
                        sb.Append(" @ ").Append(debugCandidate.Material != null ? "" : "about ").Append(debugCandidate.Distance).Append("m");
                    }

                    if (debug.Count == 0)
                        sb.Append("No hits");

                    PoolManager.Return(ref debug);
                }

                if (sb.Length > 0)
                {
                    _hintInfo.Text = sb.ToString();
                    _hintInfo.Show(); // resets alive time + adds to queue if it's not in it
                }

                return;
            }
            else
            {
                if (TryGetActionAll(out var candidate, out _, out var action, null))
                    HandleHit(candidate, action);
            }
        }

        private IMyHudNotification _hintInfo;

        private void HandleHit(ModifiableCandidate candidate, EquiModifierToolBehaviorDefinition.ModifierAction action)
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
                _log.Info($"{(action.Remove ? "Removing" : "Adding")} {action.Modifier.Id} from {candidate.Key} on {candidate.Host}");
            if (action.Remove)
                candidate.RemoveModifier(action.Modifier, ApplyRecursive);
            else
                candidate.AddModifier(action.Modifier, action.ModifierData, ApplyRecursive);
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
            if (TryGetActionFromPhysics(out _, out _, out var action, null) && !string.IsNullOrEmpty(action.ActionHint))
                yield return action.ActionHint;
        }

        public override IEnumerable<MyCrosshairIconInfo> GetIconsStates()
        {
            if (TryGetActionFromPhysics(out _, out _, out var action, null) && !string.IsNullOrEmpty(action.ActionHint))
                yield return new MyCrosshairIconInfo(action.ActionIcon);
        }

        public override void Deactivate()
        {
            _hintInfo?.Hide();
            _hintInfo = null;
            base.Deactivate();
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiModifierToolBehaviorDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiModifierToolBehaviorDefinition : MyToolBehaviorDefinition
    {
        public float MeshIntersectionDistance { get; private set; }

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
                ItemActions = builder.ItemActions?.ToImmutable() ?? ListReader<ImmutableInventoryAction>.Empty;
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
            MeshIntersectionDistance = ob.MeshIntersectionDistance ?? 2;
            Actions = ob.Actions.Select(x => new ModifierAction(this, x)).ToList();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiModifierToolBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        /// <summary>
        /// How many meters will use the rendered mesh to determine what block the character is targeting instead of the physics collider. 
        /// </summary>
        public float? MeshIntersectionDistance;

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