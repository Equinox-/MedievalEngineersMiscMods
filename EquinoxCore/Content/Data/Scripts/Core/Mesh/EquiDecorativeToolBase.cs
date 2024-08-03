using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components.Entity.CubeGrid;
using VRage.Core;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Scene;
using VRage.Utils;
using VRageMath;
using MySession = VRage.Session.MySession;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeToolBaseDefinition))]
    public abstract class EquiDecorativeToolBase<TDef, TMaterial> : MyToolBehaviorBase, IToolWithMenu, IToolWithBuilding
        where TDef : EquiDecorativeToolBaseDefinition, IEquiDecorativeToolBaseDefinition<TMaterial>
        where TMaterial : EquiDecorativeToolBaseDefinition.MaterialDef
    {
        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;
        protected TDef Def { get; private set; }
        protected TMaterial MaterialDef =>
            Def.SortedMaterials[DecorativeToolSettings.MaterialIndex % Def.SortedMaterials.Count];

        protected Vector2 DebugTextAnchor => MyAPIGateway.Session?.CreativeMode ?? false ? new Vector2(-.45f, -.45f) : new Vector2(-.45f, 100);

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            Def = (TDef)definition;
        }

        protected override bool ValidateTarget()
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            return player != null && player.HasPermission(Holder.GetPosition(), MyPermissionsConstants.Build);
        }

        protected override bool Start(MyHandItemActionEnum action) => true;

        protected bool TryRemovePreReqs(int durability, EquiDecorativeToolBaseDefinition.MaterialDef mtl)
        {
            var player = MyAPIGateway.Players?.GetPlayerControllingEntity(Holder);
            // if (player == null || player.IsCreative()) return true;
            if (durability > Item.Durability)
            {
                player.ShowNotification(
                    $"Not enough durability to build.  Tool only has {Item.Durability} of the required {durability}.");
                return false;
            }

            UpdateDurability(-durability);
            return true;
        }

        protected virtual int RenderPoints => RequiredPoints;
        protected abstract int RequiredPoints { get; }

        protected LineD DetectionLine
        {
            get
            {
                var caster = Holder.Get<MyCharacterDetectorComponent>();
                return new LineD(caster.StartPosition, caster.StartPosition + caster.Direction * BuildingState.Distance);
            }
        }

        private bool TrySnapToExisting(in BlockAnchorInteraction anchor, out BlockAnchorInteraction snapped)
        {
            if (!EquiDecorativeMeshComponent.TrySnapPosition(anchor.Grid, anchor.GridLocalPosition, Def.SnapExistingDistance, out var snappedAnchor))
            {
                snapped = default;
                return false;
            }

            snapped = new BlockAnchorInteraction(anchor.Grid, anchor.Grid.GetBlock(snappedAnchor.Block), snappedAnchor,
                BlockAnchorInteraction.SourceType.Existing, anchor.GridLocalNormal);
            return true;
        }

        private bool TrySnapToStaged(in BlockAnchorInteraction anchor, out BlockAnchorInteraction snapped)
        {
            snapped = default;
            var bestSnappedDistanceSq = Def.SnapExistingDistance * Def.SnapExistingDistance;
            foreach (var existing in _anchors)
            {
                if (existing.Grid != anchor.Grid) continue;
                var distSq = Vector3.DistanceSquared(existing.GridLocalPosition, anchor.GridLocalPosition);
                if (distSq > bestSnappedDistanceSq) continue;
                snapped = existing;
                bestSnappedDistanceSq = distSq;
            }

            return snapped.Block != null;
        }

        protected bool TryGetAnchor(out BlockAnchorInteraction anchor, out float nextAnchorDistance)
        {
            var snap = Def.RequireDummySnapping || !Modified;
            nextAnchorDistance = float.PositiveInfinity;
            if (!BlockAnchorInteraction.TryGetAnchorFromModelBvh(DetectionLine,
                    snap ? DecorativeToolSettings.SnapSize : 0,
                    snap && DecorativeToolSettings.MeshSnapping == DecorativeToolSettings.MeshSnappingType.Vertex ? DecorativeToolSettings.SnapSize : 0,
                    snap && DecorativeToolSettings.MeshSnapping == DecorativeToolSettings.MeshSnappingType.Edge ? DecorativeToolSettings.SnapSize : 0,
                    out anchor)) return false;
            if (snap && BlockAnchorInteraction.TrySnapToDummy(in anchor, Def.SnapToDummy, Def.SnapDummyDistance * Def.SnapDummyDistance, out var dummySnapped))
                anchor = dummySnapped;
            else if (Def.RequireDummySnapping)
                return false;
            if (snap)
            {
                if (TrySnapToExisting(in anchor, out var existingSnapped))
                    anchor = existingSnapped;
                if (TrySnapToStaged(in anchor, out var stagedSnapped))
                    anchor = stagedSnapped;
            }

            nextAnchorDistance = Vector3.Distance(
                anchor.GridLocalPosition,
                (Vector3) Vector3D.Transform(DetectionLine.From, anchor.Grid.Entity.PositionComp.WorldMatrixInvScaled));
            return true;
        }

        private EquiDecorativeMeshComponent.HighlightToken _highlight = default;

        public override void Activate()
        {
            base.Activate();
            this.OnActivateWithMenu();
            this.OnActivateWithBuilding();
            if (IsLocallyControlled)
                Scene.Scheduler.AddFixedUpdate(RenderAndUpdateHighlight);
        }

        public override void Deactivate()
        {
            this.OnDeactivateWithMenu();
            this.OnDeactivateWithBuilding();
            Scene.Scheduler.RemoveFixedUpdate(RenderAndUpdateHighlight);
            _highlight.Dispose();
            base.Deactivate();
        }

        private readonly List<BlockAnchorInteraction> _anchors = new List<BlockAnchorInteraction>();

        protected PackedHsvShift PackedHsvShift => Def.AllowRecoloring && DecorativeToolSettings.HsvShift.HasValue
            ? (PackedHsvShift)DecorativeToolSettings.HsvShift.Value
            : default;

        public string ToolContextMenuId => "DecorativeMeshMenu";
        public object[] ToolContextMenuArguments => new object[] { Definition };

        protected virtual void EyeDropperFeature(in EquiDecorativeMeshComponent.FeatureHandle feature)
        {
            var idx = Def.RawSortedMaterials.IndexOf(feature.Material);
            if (idx >= 0)
                DecorativeToolSettings.MaterialIndex = idx;
            if (!feature.Color.Equals(default))
                DecorativeToolSettings.HsvShift = feature.Color; 
        }
        
        protected override void Hit()
        {
            if (!IsLocallyControlled) return;
            if (ActiveAction == MyHandItemActionEnum.Secondary && _highlight.IsValid)
            {
                _highlight.Owner.RequestRemoveFeature(in _highlight.Key);
                return;
            }

            if (ActiveAction == MyHandItemActionEnum.Tertiary
                && _highlight.IsValid
                && _highlight.Owner.TryGetFeature(in _highlight.Key, out var selected)
                && selected.Definition == Definition)
            {
                EyeDropperFeature(in selected);
                return;
            }

            if (!TryGetAnchor(out var anchor, out _))
            {
                if (ActiveAction == MyHandItemActionEnum.Tertiary)
                    DecorativeToolSettings.HsvShift = default;
                return;
            }

            if (ActiveAction == MyHandItemActionEnum.Tertiary)
            {
                DecorativeToolSettings.HsvShift = default;
                var modifierHolder = anchor.Grid.Container.Get<EquiGridModifierComponent>();
                var modifierKey = new EquiGridModifierComponent.BlockModifierKey(anchor.Block.Id, MyStringHash.NullOrEmpty);
                if (modifierHolder != null && modifierHolder.TryGetModifierOutput(in modifierKey, out var modifierOutput))
                {
                    if (modifierOutput.ColorMaskHsv.HasValue)
                        DecorativeToolSettings.HsvShift = modifierOutput.ColorMaskHsv.Value;
                    modifierOutput.Dispose();
                }

                return;
            }

            for (var i = 0; i < _anchors.Count; i++)
            {
                if (_anchors[i].Grid == anchor.Grid) continue;
                _anchors.RemoveAt(i);
                i--;
            }

            _anchors.Add(anchor);
            if (_anchors.Count < RequiredPoints) return;
            HitWithEnoughPoints(_anchors);
            _anchors.Clear();
        }
        
        protected abstract void HitWithEnoughPoints(ListReader<BlockAnchorInteraction> points);

        private void RenderAndUpdateHighlight()
        {
            var hasNextAnchor = TryGetAnchor(out var nextAnchor, out var nextAnchorDistance);

            if (EquiDecorativeMeshComponent.QueryNearestInWorld(DetectionLine, out var result)
                && (!hasNextAnchor || result.Distance <= nextAnchorDistance + 0.01f))
            {
                var newHighlight = result.Owner.HighlightFeature(in result.Key);
                if (!_highlight.Equals(newHighlight))
                {
                    _highlight.Dispose();
                    _highlight = newHighlight;
                }
            }
            else
            {
                _highlight.Dispose();
                _highlight = default;
            }

            RenderHelper(hasNextAnchor, nextAnchor);
        }
        
        protected virtual void RenderHelper(bool hasNextAnchor, in BlockAnchorInteraction nextAnchor)
        {
            var renderedShape = false;
            foreach (var anchor in _anchors)
                anchor.Draw();

            using (PoolManager.Get(out List<Vector3> points))
            {
                var homogenous = true;
                MyGridDataComponent grid = null;
                foreach (var anchor in _anchors)
                {
                    if (grid != null && anchor.Grid != grid)
                    {
                        homogenous = false;
                        break;
                    }

                    grid = anchor.Grid;
                    points.Add(anchor.GridLocalPosition);
                }

                if (hasNextAnchor)
                {
                    nextAnchor.Draw();
                    if (grid != null && nextAnchor.Grid != grid)
                        homogenous = false;
                    else
                        points.Add(nextAnchor.GridLocalPosition);
                }
                else
                {
                    var worldPos = DetectionLine.To;
                    if (grid != null)
                        points.Add((Vector3)Vector3D.Transform(worldPos, grid.Container.Get<MyPositionComponentBase>().WorldMatrixNormalizedInv));
                }

                if (grid != null && homogenous && points.Count >= RenderPoints)
                {
                    renderedShape = true;
                    RenderShape(grid, points);
                }
            }

            if (!renderedShape)
            {
                RenderWithoutShape();
            }
        }

        protected virtual void RenderShape(MyGridDataComponent grid, ListReader<Vector3> positions)
        {
        }

        protected virtual void RenderWithoutShape()
        {
        }

        public virtual Matrix BuildingRotationBias => TryGetAnchor(out var anchor, out _) ? anchor.Grid.Entity.WorldMatrix : default;
        public ToolBuildingState BuildingState { get; } = new ToolBuildingState { DefaultDistance = 2 };
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeToolBaseDefinition))]
    public abstract class EquiDecorativeToolBaseDefinition : MyToolBehaviorDefinition
    {
        public HashSetReader<string> SnapToDummy { get; private set; }
        public float SnapDummyDistance { get; private set; }
        public bool RequireDummySnapping { get; private set; }
        public float SnapExistingDistance { get; private set; }

        public bool AllowRecoloring { get; private set; }

        public abstract DictionaryReader<MyStringHash, MaterialDef> RawMaterials { get; }
        public abstract ListReader<MaterialDef> RawSortedMaterials { get; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeToolBaseDefinition)builder;
            if (ob.SnapToDummy != null)
                SnapToDummy = new HashSet<string>(ob.SnapToDummy);
            SnapDummyDistance = ob.SnapDummyDistance ?? 0.125f;
            SnapExistingDistance = ob.SnapExistingDistance ?? 0.1f;
            RequireDummySnapping = ob.RequireDummySnapping ?? false;
            AllowRecoloring = ob.AllowRecoloring ?? false;
        }

        public abstract class MaterialDef : IMyObject, IEquiIconGridItem
        {
            public readonly EquiDecorativeToolBaseDefinition Owner;
            public readonly MyStringHash Id;

            public string Name { get; }

            public string[] UiIcons { get; }

            public readonly float DurabilityBase;

            public MaterialDef(
                EquiDecorativeToolBaseDefinition owner,
                MyObjectBuilder_EquiDecorativeToolBaseDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeToolBaseDefinition.MaterialDef ob,
                ICollection<string> fallbackIcons = null)
            {
                Owner = owner;
                Id = MyStringHash.GetOrCompute(ob.Id);
                Name = ob.Name ?? EquiIconGridController.NameFromId(Id);
                UiIcons = ob.UiIcons ?? (fallbackIcons?.Count > 0 ? fallbackIcons.ToArray() : Array.Empty<string>());
                DurabilityBase = ob.DurabilityBase ?? ownerOb.DurabilityBase ?? 1;
            }

            void IMyObject.Deserialize(MyObjectBuilder_Base builder) => throw new NotImplementedException();

            MyObjectBuilder_Base IMyObject.Serialize() => throw new NotImplementedException();

            IMyObjectIdentifier IMyObject.Id => throw new NotImplementedException();
            MyDefinitionId IMyObject.DefinitionId => new MyDefinitionId(typeof(MyObjectBuilder_EquiDecorativeToolBaseDefinition), Id);
            bool IMyObject.NeedsSerialize => throw new NotImplementedException();
        }

        public abstract class MaterialDef<TOwner> : MaterialDef where TOwner : EquiDecorativeToolBaseDefinition
        {
            public new readonly TOwner Owner;

            protected MaterialDef(
                TOwner owner,
                MyObjectBuilder_EquiDecorativeToolBaseDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeToolBaseDefinition.MaterialDef ob,
                ICollection<string> fallbackIcons = null) : base(owner, ownerOb, ob, fallbackIcons)
            {
                Owner = owner;
            }
        }

        protected sealed class MaterialHolder<TMaterial> where TMaterial : MaterialDef
        {
            private readonly Dictionary<MyStringHash, TMaterial> _materials = new Dictionary<MyStringHash, TMaterial>(MyStringHash.Comparer);
            private readonly Dictionary<MyStringHash, MaterialDef> _rawMaterials = new Dictionary<MyStringHash, MaterialDef>(MyStringHash.Comparer);
            private readonly EquiDecorativeToolBaseDefinition _owner;
            private List<TMaterial> _sortedMaterials;
            private List<MaterialDef> _sortedRawMaterials;

            public MaterialHolder(EquiDecorativeToolBaseDefinition owner) => _owner = owner;

            public DictionaryReader<MyStringHash, TMaterial> Materials => _materials;

            public ListReader<TMaterial> SortedMaterials => _sortedMaterials ?? (_sortedMaterials = _materials.Values.OrderBy(x => x.Name).ToList());

            public DictionaryReader<MyStringHash, MaterialDef> RawMaterials => _rawMaterials;

            public ListReader<MaterialDef> SortedRawMaterials =>
                _sortedRawMaterials ?? (_sortedRawMaterials = _materials.Values.OrderBy(x => x.Name).Select(x => (MaterialDef)x).ToList());

            public void Add(TMaterial material)
            {
                _materials[material.Id] = material;
                _rawMaterials[material.Id] = material;
                _sortedMaterials = null;
                _sortedRawMaterials = null;
            }
        }
    }

    public interface IEquiDecorativeToolBaseDefinition<TMaterial> where TMaterial : EquiDecorativeToolBaseDefinition.MaterialDef
    {
        DictionaryReader<MyStringHash, TMaterial> Materials { get; }
        ListReader<TMaterial> SortedMaterials { get; }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public abstract class MyObjectBuilder_EquiDecorativeToolBaseDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        [XmlElement("SnapToDummy")]
        public List<string> SnapToDummy;

        public bool? RequireDummySnapping;

        public float? SnapDummyDistance;

        public float? SnapExistingDistance;

        public bool? AllowRecoloring;

        /// <inheritdoc cref="MaterialDef.DurabilityBase"/>
        [XmlElement]
        public float? DurabilityBase;

        public abstract class MaterialDef
        {
            /// <summary>
            /// Unique identifier for the material.
            /// </summary>
            [XmlAttribute("Id")]
            public string Id;

            /// <summary>
            /// Display name for the material.
            /// </summary>
            [XmlAttribute("Name")]
            public string Name;


            /// <summary>
            /// Icons to show in the UI.
            /// </summary>
            [XmlElement("UiIcon")]
            public string[] UiIcons;

            /// <summary>
            /// Durability cost for each placement.
            /// </summary>
            [XmlElement]
            public float? DurabilityBase;
        }
    }
}