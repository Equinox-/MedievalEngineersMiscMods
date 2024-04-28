using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage.Collections;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Session;
using VRage.Entity.Block;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Mesh
{
    public readonly struct BlockAnchorInteraction : IEquatable<BlockAnchorInteraction>
    {
        public readonly MyGridDataComponent Grid;
        public readonly MyBlock Block;
        public readonly BlockAndAnchor Anchor;
        public readonly SourceType Source;
        public readonly Vector3 GridLocalNormal;

        public RpcBlockAndAnchor RpcAnchor => Anchor;

        public BlockAnchorInteraction(MyGridDataComponent grid, MyBlock block,
            BlockAndAnchor anchor, SourceType source,
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

        private Color SourceColor
        {
            get
            {
                switch (Source)
                {
                    case SourceType.Mesh:
                        return Color.White;
                    case SourceType.MeshVertex:
                    case SourceType.MeshEdge:
                    case SourceType.Dummy:
                        return Color.BlueViolet;
                    default:
                    case SourceType.Existing:
                        return Color.Lime;
                }
            }
        }

        public bool Equals(BlockAnchorInteraction other) => Equals(Grid, other.Grid) && Anchor.Equals(other.Anchor);

        public override bool Equals(object obj) => obj is BlockAnchorInteraction other && Equals(other);

        public override int GetHashCode() => ((Grid != null ? Grid.GetHashCode() : 0) * 397) ^ Anchor.GetHashCode();

        public enum SourceType
        {
            Mesh,
            MeshVertex,
            MeshEdge,
            Dummy,
            Existing,
        }

        #region Queries

        /// <summary>
        /// Attempts to create a block anchor interaction by querying block models along a line.
        /// </summary>
        /// <param name="worldLine">line in world space</param>
        /// <param name="snapToVertexDistance">snap to vertices closer than the distance away, or 0 to not snap</param>
        /// <param name="snapToEdgeDistance">snap to edges closer than the distance away, or 0 to not snap</param>
        /// <param name="snapToGridSize">snap to a grid of this size, or 0 to not snap</param>
        /// <param name="anchor">resulting anchor</param>
        /// <returns>true if an anchor was found</returns>
        public static bool TryGetAnchorFromModelBvh(
            LineD worldLine,
            float snapToGridSize,
            float snapToVertexDistance,
            float snapToEdgeDistance,
            out BlockAnchorInteraction anchor)
        {
            anchor = default;
            var mm = MySession.Static.Components.Get<DerivedModelManager>();
            if (mm == null)
                return false;

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

                var source = SourceType.Mesh;
                var bestDistance = (float)worldLine.Length;
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
                    var localRay = new Ray((Vector3)Vector3D.Transform(worldLine.From, in blockInvWorldMatrix),
                        (Vector3)Vector3D.TransformNormal(worldLine.Direction, ref blockInvWorldMatrix));
                    var bvh = mm.GetMaterialBvh(model);
                    if (bvh == null || !bvh.RayCast(in localRay, out _, out _, out var dist, out var triangleId, bestDistance) || dist > bestDistance)
                        continue;
                    bestDistance = dist;
                    var pos = localRay.Position + localRay.Direction * dist;
                    ref readonly var tri = ref bvh.GetTriangle(triangleId);

                    if (TrySnapToVertex(in tri, ref pos))
                        source = SourceType.MeshVertex;
                    else if (TrySnapToEdge(in tri, ref pos))
                        source = SourceType.MeshEdge;
                    else
                        TrySnapToGrid(in tmpCandidate, ref pos);

                    var gridLocalNormal = Vector3.TransformNormal(tri.RawNormal, ref blockLocalMatrix);
                    gridLocalNormal.Normalize();
                    anchor = new BlockAnchorInteraction(
                        tmpCandidate.Grid,
                        tmpCandidate.Block,
                        EquiDecorativeMeshComponent.CreateAnchorFromBlockLocalPosition(tmpCandidate.Grid,
                            tmpCandidate.Block,
                            pos),
                        source,
                        gridLocalNormal);
                    found = true;
                }
            }

            return found;

            bool TrySnapToVertex(in Triangle tri, ref Vector3 pos)
            {
                if (snapToVertexDistance <= 0) return false;

                ref readonly var nearest = ref tri.NearestVertex(in pos, out var nearestDistanceSquared);
                if (nearestDistanceSquared > snapToVertexDistance) return false;
                pos = nearest;
                return true;
            }

            bool TrySnapToEdge(in Triangle tri, ref Vector3 pos)
            {
                if (snapToEdgeDistance <= 0) return false;

                var nearest = tri.NearestEdge(in pos, out var nearestDistanceSquared);
                if (nearestDistanceSquared > snapToEdgeDistance) return false;
                pos = nearest;
                return true;
            }

            bool TrySnapToGrid(in DecorCandidate candidate, ref Vector3 pos)
            {
                if (snapToGridSize <= 0) return false;
                // Snapping is performed on the grid's coordinate system, but anchors are relative to the block's coordinate system.
                var snapOffset = ((BoundingBox)candidate.Block.Definition.BoundingBox).Center * candidate.Grid.Size;
                pos = Vector3.Round((pos - snapOffset) / snapToGridSize) * snapToGridSize + snapOffset;
                return true;
            }
        }


        public static bool TrySnapToDummy(
            in BlockAnchorInteraction anchor,
            HashSetReader<string> snapToDummies,
            float snapToDistSq,
            out BlockAnchorInteraction snapped)
        {
            snapped = default;
            if (snapToDummies.Count == 0) return false;
            var hasSnapped = false;
            Matrix snapTo = default;
            var blockLocalPos = anchor.BlockLocalPosition;
            foreach (var dummy in anchor.Block.Model.Dummies)
                if (snapToDummies.Contains(dummy.Name))
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

            snapped = new BlockAnchorInteraction(anchor.Grid, anchor.Block,
                EquiDecorativeMeshComponent.CreateAnchorFromBlockLocalPosition(anchor.Grid, anchor.Block, snapTo.Translation),
                SourceType.Dummy,
                snappedGridNormal);
            return true;
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

        #endregion
    }
}