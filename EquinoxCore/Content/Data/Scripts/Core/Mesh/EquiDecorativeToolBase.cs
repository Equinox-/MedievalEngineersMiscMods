using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Equinox76561198048419394.Core.Util;
using Medieval.GameSystems;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Session;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Utils;
using VRageMath;
using VRageRender;
using MySession = VRage.Session.MySession;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeToolBaseDefinition))]
    public abstract class EquiDecorativeToolBase : MyToolBehaviorBase
    {
        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;
        private EquiDecorativeToolBaseDefinition _definition;

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiDecorativeToolBaseDefinition)definition;
        }

        protected bool HasPermission(MyStringId id)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position, id);
        }

        protected bool TryRemoveDurability(int durability)
        {
            var player = MyAPIGateway.Players?.GetPlayerControllingEntity(Holder);
            if (player == null || player.IsCreative()) return true;
            if (durability > Item.Durability)
            {
                player.ShowNotification(
                    $"Not enough durability to build.  Tool only has {Item.Durability} of the required {durability}.");
                return false;
            }

            UpdateDurability(-durability);
            return true;
        }

        protected abstract int RequiredPoints { get; }

        protected enum AnchorSource
        {
            Mesh,
            Dummy,
            Existing,
        }

        protected readonly struct DecorAnchor : IEquatable<DecorAnchor>
        {
            public readonly MyGridDataComponent Grid;
            public readonly MyBlock Block;
            public readonly EquiDecorativeMeshComponent.BlockAndAnchor Anchor;
            public readonly AnchorSource Source;
            public readonly Vector3 GridLocalNormal;

            public DecorAnchor(MyGridDataComponent grid, MyBlock block, 
                EquiDecorativeMeshComponent.BlockAndAnchor anchor, AnchorSource source,
                Vector3 gridLocalNormal)
            {
                Grid = grid;
                Block = block;
                Anchor = anchor;
                Source = source;
                GridLocalNormal = gridLocalNormal;
            }

            public Vector3 BlockLocalPosition => Anchor.GetBlockLocalAnchor(Grid, Block);
            public Vector3 GridLocalPosition => Anchor.GetGridLocalAnchor(Grid, Block);

            public bool TryGetWorldPosition(out Vector3D pos)
            {
                var gridPos = Grid?.Container?.Get<MyPositionComponentBase>();
                if (gridPos != null && Anchor.TryGetGridLocalAnchor(Grid, out var localPos))
                {
                    pos = Vector3D.Transform(localPos, gridPos.WorldMatrix);
                    return true;
                }

                pos = default;
                return false;
            }

            public void Draw()
            {
                if (!TryGetWorldPosition(out var pos)) return;
                var color = SourceColor;
                const float size = 0.0625f;
                for (var i = 0; i < 3; i++)
                {
                    var dir = new Vector3D();
                    dir.SetDim(i, size);
                    MyRenderProxy.DebugDrawLine3D(pos - dir, pos + dir, color, color, false);
                }
            }

            public Color SourceColor
            {
                get
                {
                    switch (Source)
                    {
                        case AnchorSource.Mesh:
                            return Color.White;
                        case AnchorSource.Dummy:
                            return Color.BlueViolet;
                        default:
                        case AnchorSource.Existing:
                            return Color.Lime;
                    }
                }
            }

            public bool Equals(DecorAnchor other) => Equals(Grid, other.Grid) && Anchor.Equals(other.Anchor);

            public override bool Equals(object obj) => obj is DecorAnchor other && Equals(other);

            public override int GetHashCode() => ((Grid != null ? Grid.GetHashCode() : 0) * 397) ^ Anchor.GetHashCode();
        }

        private readonly struct DecorCandidate
        {
            public readonly MyGridDataComponent Grid;
            public readonly MyBlock Block;

            public DecorCandidate(MyGridDataComponent grid, MyBlock block)
            {
                Grid = grid;
                Block = block;
            }

            public string ModelName
            {
                get
                {
                    var model = Block.Model.AssetName;
                    if (!Grid.Container.TryGet(out EquiGridModifierComponent modifier)) return model;
                    var key = new EquiGridModifierComponent.BlockModifierKey(Block.Id, MyStringHash.NullOrEmpty);
                    if (!modifier.TryCreateContext(in key, modifier.GetModifiers(in key), out var ctx)) return model;
                    return ctx.OriginalModel ?? model;
                }
            }
        }

        private bool TryGetAnchorFromModelBvh(bool snapToGrid, out DecorAnchor anchor)
        {
            anchor = default;
            var mm = MySession.Static.Components.Get<DerivedModelManager>();
            if (mm == null)
                return false;

            var caster = Holder.Get<MyCharacterDetectorComponent>();
            var bestDistance = caster.GetDetectionDistance();
            if (Target.HitDistance >= bestDistance || Target.Block == null) return false;
            var worldLine = new LineD(caster.StartPosition, caster.StartPosition + caster.Direction * bestDistance);
            var found = false;
            using (PoolManager.Get(out List<MyLineSegmentOverlapResult<DecorCandidate>> candidates))
            {
                using (PoolManager.Get(out List<MyLineSegmentOverlapResult<MyEntity>> entities))
                {
                    MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref worldLine, entities);
                    foreach (var overlap in entities)
                    {
                        var entity = overlap.Element;
                        if (!entity.Components.TryGet(out MyGridDataComponent gridDataComponent))
                            continue;
                        var invMatrix = entity.PositionComp.WorldMatrixNormalizedInv;
                        var localLine = new LineD(Vector3D.Transform(worldLine.From, in invMatrix), Vector3D.Transform(worldLine.To, in invMatrix));
                        var localLineSingle = (Line)localLine;
                        using (PoolManager.Get(out List<MyBlock> blocks))
                        {
                            gridDataComponent.GetBlocksInLine(localLine, blocks);
                            foreach (var block in blocks)
                                if (gridDataComponent.GetBlockLocalBounds(block).Intersects(localLineSingle, out var distance))
                                    candidates.Add(new MyLineSegmentOverlapResult<DecorCandidate>
                                    {
                                        Distance = distance,
                                        Element = new DecorCandidate(gridDataComponent, block)
                                    });
                        }
                    }
                }

                candidates.Sort(MyLineSegmentOverlapResult<DecorCandidate>.DistanceComparer);
                foreach (var overlap in candidates)
                {
                    // Stop trying if the whole entity is farther away
                    if (overlap.Distance > bestDistance)
                        break;
                    var tmpCandidate = overlap.Element;
                    var model = tmpCandidate.ModelName;
                    var blockLocalMatrix = tmpCandidate.Grid.GetBlockLocalMatrix(tmpCandidate.Block);
                    var blockWorldMatrix = blockLocalMatrix * tmpCandidate.Grid.Entity.WorldMatrix;
                    MatrixD.Invert(ref blockWorldMatrix, out var blockInvWorldMatrix);
                    var localRay = new Ray((Vector3)Vector3D.Transform(caster.StartPosition, in blockInvWorldMatrix),
                        (Vector3)Vector3D.TransformNormal(caster.Direction, ref blockInvWorldMatrix));
                    var bvh = mm.GetMaterialBvh(model);
                    if (bvh == null || !bvh.RayCast(in localRay, out _, out _, out var dist, out var triangleId, bestDistance) || dist > bestDistance)
                        continue;
                    bestDistance = dist;
                    var pos = localRay.Position + localRay.Direction * dist;
                    if (snapToGrid)
                    {
                        const float snapSize = 0.25f / 16;
                        pos = Vector3.Round(pos / snapSize) * snapSize;
                    }

                    var gridLocalNormal = Vector3.TransformNormal(bvh.GetTriangle(triangleId).RawNormal, ref blockLocalMatrix);
                    gridLocalNormal.Normalize();
                    anchor = new DecorAnchor(tmpCandidate.Grid, tmpCandidate.Block,
                        EquiDecorativeMeshComponent.CreateAnchorFromBlockLocalPosition(tmpCandidate.Grid,
                            tmpCandidate.Block,
                            pos),
                        AnchorSource.Mesh,
                        gridLocalNormal);
                    found = true;
                }
            }

            return found;
        }

        private bool TrySnapToDummy(in DecorAnchor anchor, out DecorAnchor snapped)
        {
            snapped = default;
            if (_definition.SnapToDummy.Count == 0) return false;
            var hasSnapped = false;
            Matrix snapTo = default;
            var snapToDistSq = _definition.SnapDummyDistance * _definition.SnapDummyDistance;
            var blockLocalPos = anchor.BlockLocalPosition;
            foreach (var dummy in anchor.Block.Model.Dummies)
                if (_definition.SnapToDummy.Contains(dummy.Name))
                {
                    var dist2 = Vector3.DistanceSquared(dummy.Matrix.Translation, blockLocalPos);
                    if (dist2 >= snapToDistSq) continue;
                    hasSnapped = true;
                    snapTo = dummy.Matrix;
                    snapToDistSq = dist2;
                }

            if (!hasSnapped) return false;

            // Snap to dummy axes
            Matrix.Transpose(ref snapTo, out var snapToTranspose);
            var snappedBlockNormal = Vector3.TransformNormal(anchor.GridLocalNormal, ref snapToTranspose);
            snappedBlockNormal = Vector3.DominantAxisProjection(snappedBlockNormal);
            var snappedGridNormal = Vector3.TransformNormal(snappedBlockNormal, ref snapTo);
            snappedGridNormal.Normalize();
            
            snapped = new DecorAnchor(anchor.Grid, anchor.Block,
                EquiDecorativeMeshComponent.CreateAnchorFromBlockLocalPosition(anchor.Grid, anchor.Block, snapTo.Translation),
                AnchorSource.Dummy,
                snappedGridNormal);
            return true;
        }

        private bool TrySnapToExisting(in DecorAnchor anchor, out DecorAnchor snapped)
        {
            if (!EquiDecorativeMeshComponent.TrySnapPosition(anchor.Grid, anchor.GridLocalPosition, _definition.SnapExistingDistance, out var snappedAnchor))
            {
                snapped = default;
                return false;
            }

            snapped = new DecorAnchor(anchor.Grid, anchor.Grid.GetBlock(snappedAnchor.Block), snappedAnchor,
                AnchorSource.Existing, anchor.GridLocalNormal);
            return true;
        }

        private bool TrySnapToStaged(in DecorAnchor anchor, out DecorAnchor snapped)
        {
            snapped = default;
            var bestSnappedDistanceSq = _definition.SnapExistingDistance * _definition.SnapExistingDistance;
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

        protected bool TryGetAnchor(out DecorAnchor anchor)
        {
            var snap = _definition.RequireDummySnapping || !Modified;
            if (!TryGetAnchorFromModelBvh(snap, out anchor)) return false;
            if (snap && TrySnapToDummy(in anchor, out var dummySnapped))
                anchor = dummySnapped;
            else if (_definition.RequireDummySnapping)
                return false;
            if (!snap)
                return true;
            if (TrySnapToExisting(in anchor, out var existingSnapped))
                anchor = existingSnapped;
            if (TrySnapToStaged(in anchor, out var stagedSnapped))
                anchor = stagedSnapped;
            return true;
        }

        public override void Activate()
        {
            base.Activate();
            if (IsLocallyControlled)
                Scene.Scheduler.AddFixedUpdate(RenderHelper);
        }

        public override void Deactivate()
        {
            Scene.Scheduler.RemoveFixedUpdate(RenderHelper);
            base.Deactivate();
        }

        private readonly List<DecorAnchor> _anchors = new List<DecorAnchor>();

        protected override void Hit()
        {
            if (!IsLocallyControlled) return;
            if (!TryGetAnchor(out var anchor)) return;
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

        protected abstract void HitWithEnoughPoints(ListReader<DecorAnchor> points);

        protected virtual void RenderHelper()
        {
            SetTarget();

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

                if (TryGetAnchor(out var nextAnchor))
                {
                    nextAnchor.Draw();
                    if (grid != null && nextAnchor.Grid != grid)
                        homogenous = false;
                    else
                        points.Add(nextAnchor.GridLocalPosition);
                }
                else
                {
                    var caster = Holder.Get<MyCharacterDetectorComponent>();
                    var worldPos = caster == null ? Holder.GetPosition() : caster.StartPosition + caster.Direction * 2;
                    if (grid != null)
                        points.Add((Vector3)Vector3D.Transform(worldPos, grid.Container.Get<MyPositionComponentBase>().WorldMatrixNormalizedInv));
                }

                if (grid != null && homogenous && points.Count >= RequiredPoints)
                    RenderShape(grid, points);
            }
        }

        protected abstract void RenderShape(MyGridDataComponent grid, ListReader<Vector3> positions);
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeToolBaseDefinition))]
    public class EquiDecorativeToolBaseDefinition : MyToolBehaviorDefinition
    {
        public HashSetReader<string> SnapToDummy { get; private set; }
        public float SnapDummyDistance { get; private set; }
        public bool RequireDummySnapping { get; private set; }
        public float SnapExistingDistance { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeToolBaseDefinition)builder;
            if (ob.SnapToDummy != null)
                SnapToDummy = new HashSet<string>(ob.SnapToDummy);
            SnapDummyDistance = ob.SnapDummyDistance ?? 0.125f;
            SnapExistingDistance = ob.SnapExistingDistance ?? 0.1f;
            RequireDummySnapping = ob.RequireDummySnapping ?? false;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeToolBaseDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        [XmlElement("SnapToDummyh")]
        public List<string> SnapToDummy;

        public bool? RequireDummySnapping;

        public float? SnapDummyDistance;

        public float? SnapExistingDistance;
    }
}