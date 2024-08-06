using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Equinox76561198048419394.Core.Util.Memory;
using Equinox76561198048419394.Core.Util.Struct;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.Camera;
using VRage.Components.Entity.CubeGrid;
using VRage.Entities.Gravity;
using VRage.Entity.Block;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyComponent(typeof(MyObjectBuilder_EquiDynamicMeshComponent), AllowAutomaticCreation = true)]
    [MyDependency(typeof(MyRenderComponentGrid), Critical = true)]
    public class EquiDynamicMeshComponent : MyEntityComponent, IComponentDebugDraw
    {
        public const float GhostDithering = 0.4f;

#pragma warning disable CS0649
        [Automatic]
        private readonly MyRenderComponentGrid _gridRender;
#pragma warning restore CS0649

        public const ulong NullId = 0;
        private const float CellSize = 8;
        private bool _scheduled;
        private readonly HashSet<CellKey> _dirtyCells = new HashSet<CellKey>();
        private readonly Dictionary<CellKey, RenderCell> _renderCells = new Dictionary<CellKey, RenderCell>();
        private readonly MyDynamicAABBTree _cellTree = new MyDynamicAABBTree();

        private ulong _nextId = 1;
        private readonly Dictionary<ulong, CellKey> _objectToCell = new Dictionary<ulong, CellKey>();

        private uint _currentCullObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

        private uint RequestedCullObject => _gridRender.RenderObjectIDs.Length > 0 ? _gridRender.RenderObjectIDs[0] : MyRenderProxy.RENDER_ID_UNASSIGNED;

        private static bool IsDedicated => ((IMyUtilities)MyAPIUtilities.Static).IsDedicated;

        private readonly struct CellArgs : IEquatable<CellArgs>
        {
            public readonly PackedHsvShift Color;
            public readonly bool Ghost;

            public CellArgs(PackedHsvShift color, bool ghost)
            {
                Color = color;
                Ghost = ghost;
            }

            public bool Equals(CellArgs other) => Color.Equals(other.Color) && Ghost.Equals(other.Ghost);

            public override bool Equals(object obj) => obj is CellArgs other && Equals(other);

            public override int GetHashCode() => (Color.GetHashCode() * 397) ^ Ghost.GetHashCode();
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly Vector3I Position;
            public readonly CellArgs Args;

            public CellKey(Vector3I position, in CellArgs args)
            {
                Position = position;
                Args = args;
            }

            public bool Equals(CellKey other) => Position.Equals(other.Position) && Args.Equals(other.Args);

            public override bool Equals(object obj) => obj is CellKey other && Equals(other);

            public override int GetHashCode() => (Position.GetHashCode() * 397) ^ Args.GetHashCode();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!IsDedicated)
            {
                _gridRender.BlockRenderablesChanged += BlockRenderablesChanged;
            }

            MarkDirty();
        }

        private void BlockRenderablesChanged(MyRenderComponentGrid owner, BlockId block, ListReader<uint> renderObjects)
        {
            if (RequestedCullObject != _currentCullObject)
                MarkDirty();
        }

        public override void OnRemovedFromScene()
        {
            if (!IsDedicated)
            {
                _gridRender.BlockRenderablesChanged -= BlockRenderablesChanged;
                foreach (var cell in _renderCells.Values)
                    cell.RemoveRenderObject();
            }

            base.OnRemovedFromScene();
        }

        private void MarkDirty()
        {
            if (_scheduled) return;
            _scheduled = true;
            AddScheduledCallback(ApplyChanges);
        }

        private void MarkCellDirty(CellKey cell)
        {
            MarkDirty();
            _dirtyCells.Add(cell);
        }

        [Update(false)]
        public void ApplyChanges(long dt)
        {
            _scheduled = false;
            var newCullObject = RequestedCullObject;

            // Update only dirty cells
            foreach (var cellKey in _dirtyCells)
                if (_renderCells.TryGetValue(cellKey, out var cell))
                    if (cell.ApplyChanges())
                        _renderCells.Remove(cellKey);
                    else
                        cell.SetCullObject(newCullObject);

            // Switch parent object of non-dirty cells if needed
            if (_currentCullObject != newCullObject)
                foreach (var cell in _renderCells)
                    if (!_dirtyCells.Contains(cell.Key))
                        cell.Value.SetCullObject(newCullObject);

            _dirtyCells.Clear();
            _currentCullObject = newCullObject;
        }

        public ulong CreateDecal(in EquiMeshHelpers.DecalData data)
        {
            AllocateObjectForCell(data.Position, new CellArgs(data.ColorMask, data.Ghost), out var cell, out var id);
            cell.Decals.Add(id).Data = data;
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public ulong CreateLine(in EquiMeshHelpers.LineData data)
        {
            AllocateObjectForCell((data.Pt0 + data.Pt1) / 2, new CellArgs(data.ColorMask, data.Ghost), out var cell, out var id);
            cell.Lines.Add(id).Data = data;
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public ulong CreateSurface(in EquiMeshHelpers.SurfaceData data)
        {
            var center = data.Pt0.Position + data.Pt1.Position + data.Pt2.Position;
            if (data.Pt3.HasValue)
                center += data.Pt3.Value.Position;
            AllocateObjectForCell(center / (data.Pt3.HasValue ? 4 : 3), new CellArgs(data.ColorMask, data.Ghost), out var cell, out var id);
            cell.Surfaces.Add(id).Data = data;
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public bool DestroyObject(ulong obj)
        {
            if (!_objectToCell.TryGetValue(obj, out var cellKey) || !_renderCells.TryGetValue(cellKey, out var cell))
                return false;
            if (cell.Decals.TryGetValue(obj, out var decalData))
            {
                cell.MaterialToObject.Remove(decalData.Value.Data.Material, obj);
                cell.Decals.Remove(obj);
            }

            if (cell.Lines.TryGetValue(obj, out var lineData))
            {
                cell.MaterialToObject.Remove(lineData.Value.Data.Material, obj);
                cell.Lines.Remove(obj);
            }

            if (cell.Surfaces.TryGetValue(obj, out var triangleData))
            {
                cell.MaterialToObject.Remove(triangleData.Value.Data.Material, obj);
                cell.Surfaces.Remove(obj);
            }

            MarkCellDirty(cellKey);
            return true;
        }

        internal void DrawHighlight(ulong obj)
        {
            if (!_objectToCell.TryGetValue(obj, out var cellKey) || !_renderCells.TryGetValue(cellKey, out var cell))
                return;
            var entries = cell.BvhEntries;
            if (entries == null) return;
            var lines = MyRenderProxy.DebugDrawLine3DOpenBatch(false);
            lines.WorldMatrix = Entity.PositionComp.WorldMatrix;
            lines.WorldMatrix.Translation = Vector3D.Transform(cell.Origin, ref lines.WorldMatrix);

            if (cell.Decals.TryGetValue(obj, out var decalData))
                DrawBvhEntry(ref decalData.Value);

            if (cell.Lines.TryGetValue(obj, out var lineData))
                DrawBvhEntry(ref lineData.Value);

            if (cell.Surfaces.TryGetValue(obj, out var triangleData))
                DrawBvhEntry(ref triangleData.Value);

            MyRenderProxy.DebugDrawLine3DSubmitBatch(lines);
            return;

            void DrawBvhEntry<T>(ref WrappedData<T> data) where T : struct
            {
                for (var i = data.BvhEntryMin; i < data.BvhEntryMax; i++)
                    entries[i].DrawHighlight(lines, Color.Red);
            }
        }

        private void AllocateObjectForCell(Vector3 objectCenter, in CellArgs args, out RenderCell cell, out ulong id)
        {
            var cellPos = PosToCell(objectCenter);
            var cellKey = new CellKey(cellPos, in args);
            id = _nextId++;
            _objectToCell.Add(id, cellKey);
            MarkCellDirty(cellKey);
            if (!_renderCells.TryGetValue(cellKey, out cell))
                _renderCells.Add(cellKey, cell = new RenderCell(this, cellKey));
        }

        private static Vector3I PosToCell(Vector3 pos) => Vector3I.Floor((pos / CellSize) - 0.5f);

        private struct WrappedData<T> where T : struct
        {
            public T Data;
            public int BvhEntryMin;
            public int BvhEntryMax;
        }

        private sealed class RenderCell
        {
            private readonly EquiDynamicMeshComponent _owner;
            private readonly CellKey _key;

            internal readonly OffloadedDictionary<ulong, WrappedData<EquiMeshHelpers.DecalData>> Decals =
                new OffloadedDictionary<ulong, WrappedData<EquiMeshHelpers.DecalData>>();

            internal readonly OffloadedDictionary<ulong, WrappedData<EquiMeshHelpers.LineData>> Lines =
                new OffloadedDictionary<ulong, WrappedData<EquiMeshHelpers.LineData>>();

            internal readonly OffloadedDictionary<ulong, WrappedData<EquiMeshHelpers.SurfaceData>> Surfaces =
                new OffloadedDictionary<ulong, WrappedData<EquiMeshHelpers.SurfaceData>>();

            internal readonly MyHashSetDictionary<string, ulong> MaterialToObject = new MyHashSetDictionary<string, ulong>();
            private readonly string _modelPrefix;
            private string _modelName;
            private uint _meshGeneration;
            private uint _renderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;
            private int _ownerBvhProxy = MyDynamicAABBTree.NullNode;

            internal PackedBvh Bvh;
            internal PackedBvhEntry[] BvhEntries;

            public BoundingBox LocalBounds;
            public int Triangles;
            public int Materials;

            public RenderCell(EquiDynamicMeshComponent owner, CellKey key)
            {
                _owner = owner;
                _key = key;
                _modelPrefix = $"dyn_mesh_{_owner.Entity.Id}_{_key.Position}_{_key.Args.Color}_{_key.Args.Ghost}";
            }

            internal Vector3 Origin => (_key.Position + 1) * CellSize;

            private void BuildMesh(MyModelData mesh, List<PackedBvhEntry> bvhEntries)
            {
                var offset = -Origin;

                var localGravity = Vector3.TransformNormal(
                    MyGravityProviderSystem.CalculateNaturalGravityInPoint(Vector3.Transform(
                        _owner.Entity.PositionComp.LocalAABB.Center,
                        _owner.Entity.WorldMatrix)),
                    _owner.Entity.PositionComp.WorldMatrixNormalizedInv);

                foreach (var kv in MaterialToObject)
                {
                    int vertexOffset;
                    int indexOffset;
                    MaterialDescriptor mtl;
                    if (mesh != null)
                    {
                        if (MaterialTable.TryGetById(kv.Key, out mtl))
                            mtl.EnsurePrepared();
                        vertexOffset = mesh.Positions.Count;
                        indexOffset = mesh.Indices.Count;
                    }
                    else
                    {
                        vertexOffset = indexOffset = -1;
                        mtl = default;
                    }

                    foreach (var obj in kv.Value)
                    {
                        if (Lines.TryGetValue(obj, out var wrappedLine))
                        {
                            ref var line = ref wrappedLine.Value.Data;
                            wrappedLine.Value.BvhEntryMin = bvhEntries.Count;
                            using (PoolManager.Get<List<Vector3>>(out var points))
                            {
                                EquiMeshHelpers.PrepareLine(in line, points, localGravity, offset);
                                if (mesh != null)
                                    EquiMeshHelpers.BuildPreparedLine(in line, mesh, points, localGravity);

                                // Index buffered lines.
                                var invSegments = 1 / (float)line.Segments;
                                for (var i = 0; i < points.Count - 1; ++i)
                                {
                                    var fraction = i * invSegments;
                                    bvhEntries.Add(new PackedBvhEntry(obj, points[i], points[i + 1],
                                        MathHelper.Lerp(line.Width0, line.Width1, fraction) / 2,
                                        MathHelper.Lerp(line.Width0, line.Width1, fraction + invSegments) / 2));
                                }
                            }

                            wrappedLine.Value.BvhEntryMax = bvhEntries.Count;
                        }

                        if (Surfaces.TryGetValue(obj, out var surface))
                        {
                            ref readonly var surf = ref surface.Value.Data;
                            surface.Value.BvhEntryMin = bvhEntries.Count;
                            if (surf.Pt3.HasValue)
                                bvhEntries.Add(new PackedBvhEntry(obj,
                                    surf.Pt0.Position + offset,
                                    surf.Pt1.Position + offset,
                                    surf.Pt2.Position + offset,
                                    surf.Pt3.Value.Position + offset));
                            else
                                bvhEntries.Add(new PackedBvhEntry(obj,
                                    surf.Pt0.Position + offset,
                                    surf.Pt1.Position + offset,
                                    surf.Pt2.Position + offset));
                            if (mesh != null)
                                EquiMeshHelpers.BuildSurface(in surf, mesh, offset);
                            surface.Value.BvhEntryMax = bvhEntries.Count;
                        }

                        if (Decals.TryGetValue(obj, out var decal))
                        {
                            decal.Value.BvhEntryMin = bvhEntries.Count;
                            ref readonly var decalData = ref decal.Value.Data;
                            var center = decalData.Position + offset;
                            bvhEntries.Add(new PackedBvhEntry(obj,
                                center + decalData.Up + decalData.Left,
                                center - decalData.Up + decalData.Left,
                                center - decalData.Up - decalData.Left,
                                center + decalData.Up - decalData.Left));
                            if (mesh != null)
                                EquiMeshHelpers.BuildDecal(in decalData, mesh, offset);
                            decal.Value.BvhEntryMax = bvhEntries.Count;
                        }
                    }

                    if (mesh == null || mesh.Positions.Count == vertexOffset || mesh.Indices.Count == indexOffset)
                        continue;
                    mesh.Sections.Add(new MyRuntimeSectionInfo
                    {
                        IndexStart = indexOffset,
                        TriCount = (mesh.Indices.Count - indexOffset) / 3,
                        MaterialName = mtl.MaterialName,
                    });
                }
            }

            internal void RemoveRenderObject()
            {
                if (_renderObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                    MyRenderProxy.RemoveRenderObject(ref _renderObject);
            }

            private void ClearStats()
            {
                Bvh = null;
                BvhEntries = null;
                Materials = 0;
                Triangles = 0;
                LocalBounds = default;
                if (_ownerBvhProxy != MyDynamicAABBTree.NullNode)
                {
                    _owner._cellTree.RemoveProxy(_ownerBvhProxy);
                    _ownerBvhProxy = MyDynamicAABBTree.NullNode;
                }
            }

            internal bool ApplyChanges()
            {
                RemoveRenderObject();
                if (MaterialToObject.KeyCount == 0)
                {
                    ClearStats();
                    return true;
                }

                var parent = _owner._gridRender;
                var model = IsDedicated ? null : MyRenderProxy.PrepareAddRuntimeModel();
                if (model != null)
                {
                    model.Name = _modelName = $"{_modelPrefix}_{_meshGeneration++}";
                    model.ReplacedModel = null;
                    model.Persistent = false;
#if VRAGE_API_1
                    model.Dynamic = true;
#endif
                }

                BoundingBox modelBox;
                using (PoolManager.Get(out List<PackedBvhEntry> bvhEntries))
                {
                    BuildMesh(model?.ModelData, bvhEntries);
                    if (bvhEntries.Count == 0)
                    {
                        model?.Close();
                        ClearStats();
                        return false;
                    }

                    Triangles = model?.ModelData.Indices.Count / 3 ?? 0;
                    Materials = model?.ModelData.Sections.Count ?? 0;
                    BvhEntries = bvhEntries.ToArray();
                }

                var meshBounds = BoundingBox.CreateInvalid();
                using (PoolManager.Get(out List<BoundingBox> boxes))
                {
                    boxes.EnsureCapacity(BvhEntries.Length);
                    var boxesArray = boxes.GetInternalArray();
                    for (var i = 0; i < BvhEntries.Length; i++)
                    {
                        BvhEntries[i].Bounds(out boxesArray[i]);
                        meshBounds.Include(ref boxesArray[i]);
                    }

                    using (var builder = new SahBvhBuilder(8, new EqReadOnlySpan<BoundingBox>(boxesArray, 0, BvhEntries.Length)))
                        Bvh = builder.Build();
                }
                LocalBounds = meshBounds;
                LocalBounds.Translate(Origin);

                if (_ownerBvhProxy != MyDynamicAABBTree.NullNode)
                    _owner._cellTree.MoveProxy(_ownerBvhProxy, ref LocalBounds, Vector3.Zero);
                else
                    _ownerBvhProxy = _owner._cellTree.AddProxy(ref LocalBounds, this, 0);

                if (model != null)
                {
                    model.ModelData.AABB = meshBounds;
                    MyRenderProxy.AddRuntimeModel(model.Name, model);
                    _renderObject = MyRenderProxy.CreateRenderEntity(_modelName, _modelName,
                        MatrixD.CreateTranslation(Origin), MyMeshDrawTechnique.MESH,
                        parent.GetRenderFlags() | RenderFlags.ForceOldPipeline,
                        parent.GetRenderCullingOptions(),
                        parent.GetDiffuseColor(),
                        Vector3.One,
                        dithering: _key.Args.Ghost ? GhostDithering : 0,
                        depthBias: 255
                    );
                    MyRenderProxy.UpdateRenderEntity(_renderObject, null, (Vector3)_key.Args.Color, dithering: _key.Args.Ghost ? GhostDithering : 0);
                }

                return false;
            }

            internal void SetCullObject(uint parent)
            {
                if (_renderObject == MyRenderProxy.RENDER_ID_UNASSIGNED) return;
                MyRenderProxy.SetParentCullObject(_renderObject, parent, Matrix.CreateTranslation(Origin));
            }
        }

        public void DebugDraw()
        {
            if (_renderCells.Count == 0)
                return;
            var cam = MyCameraComponent.ActiveCamera;
            var center = Entity.PositionComp.WorldAABB.Center;
            if (!cam.GetCameraFrustum().Intersects(new BoundingBoxD(center - 1, center + 1)))
                return;

            var localFrustum = new BoundingFrustum(Entity.WorldMatrix * cam.GetViewProjMatrix());

            var renderables = default(DebugCount);
            var materials = default(DebugCount);
            var triangles = default(DebugCount);

            var surfaces = default(DebugCount);
            var lines = default(DebugCount);
            var decals = default(DebugCount);


            foreach (var cell in _renderCells.Values)
            {
                if (cell.Triangles == 0 || cell.Materials == 0) continue;
                var visible = localFrustum.Intersects(cell.LocalBounds);

                renderables.Add(1, visible);
                materials.Add(cell.Materials, visible);
                triangles.Add(cell.Triangles, visible);

                surfaces.Add(cell.Surfaces.Count, visible);
                lines.Add(cell.Lines.Count, visible);
                decals.Add(cell.Decals.Count, visible);
            }

            if (renderables.Total == 0)
                return;

            MyRenderProxy.DebugDrawText3D(
                center,
                $"T: {triangles}\nR: {renderables}\nM: {materials}\nS: {surfaces}\nL: {lines}\nD: {decals}",
                Color.Cyan,
                0.5f);
        }

        private struct DebugCount
        {
            public int Visible;
            public int Total;

            public void Add(int count, bool visible)
            {
                Total += count;
                if (visible)
                    Visible += count;
            }

            public override string ToString() => $"{Visible}/{Total}";
        }


        #region Spatial Query

        private readonly struct PackedBvhEntry
        {
            public readonly ulong Id;
            private readonly Mode _mode;
            private readonly HalfVector3 _pt0;
            private readonly HalfVector3 _pt1;
            private readonly HalfVector3 _pt2;
            private readonly HalfVector3 _pt3;

            private enum Mode : byte
            {
                Triangle,
                Quad,
                BufferedLine
            }

            public PackedBvhEntry(ulong id, HalfVector3 pt0, HalfVector3 pt1, HalfVector3 pt2)
            {
                Id = id;
                _pt0 = pt0;
                _pt1 = pt1;
                _pt2 = pt2;
                _pt3 = default;
                _mode = Mode.Triangle;
            }

            public PackedBvhEntry(ulong id, HalfVector3 pt0, HalfVector3 pt1, HalfVector3 pt2, HalfVector3 pt3)
            {
                Id = id;
                _pt0 = pt0;
                _pt1 = pt1;
                _pt2 = pt2;
                _pt3 = pt3;
                _mode = Mode.Quad;
            }

            public PackedBvhEntry(ulong id, HalfVector3 pt0, HalfVector3 pt1, float radiusAt0, float radiusAt1)
            {
                Id = id;
                _pt0 = pt0;
                _pt1 = pt1;
                _pt2.X = HalfUtils.Pack(radiusAt0);
                _pt2.Y = HalfUtils.Pack(radiusAt1);
                _pt2.Z = default;
                _pt3 = default;
                _mode = Mode.BufferedLine;
            }

            public void Bounds(out BoundingBox box)
            {
                box = BoundingBox.CreateInvalid();
                switch (_mode)
                {
                    case Mode.Triangle:
                        box.Include(_pt0);
                        box.Include(_pt1);
                        box.Include(_pt2);
                        break;
                    case Mode.Quad:
                        box.Include(_pt0);
                        box.Include(_pt1);
                        box.Include(_pt2);
                        box.Include(_pt3);
                        break;
                    case Mode.BufferedLine:
                        box.Include(_pt0);
                        box.Include(_pt1);
                        box.Inflate(Math.Max(HalfUtils.Unpack(_pt2.X), HalfUtils.Unpack(_pt2.Y)));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public float? Intersects(in Ray ray)
            {
                switch (_mode)
                {
                    case Mode.Triangle:
                    {
                        var tri = new Triangle(_pt0, _pt1, _pt2);
                        return tri.Intersects(in ray, out var t) ? (float?)t : null;
                    }
                    case Mode.Quad:
                    {
                        var tri1 = new Triangle(_pt0, _pt1, _pt2);
                        var tri2 = new Triangle(_pt0, _pt2, _pt3);
                        if (!tri1.Intersects(in ray, out var t1)) t1 = float.PositiveInfinity;
                        if (!tri2.Intersects(in ray, out var t2)) t2 = float.PositiveInfinity;
                        var t = Math.Min(t1, t2);
                        return float.IsInfinity(t) ? null : (float?)t;
                    }
                    case Mode.BufferedLine:
                        return MiscMath.CappedConeRayIntersection(
                            ray.Position, ray.Direction,
                            _pt0, _pt1,
                            HalfUtils.Unpack(_pt2.X),
                            HalfUtils.Unpack(_pt2.Y));
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public void DrawHighlight(MyRenderMessageDebugDrawLine3DBatch lines, Color color)
            {
                Vector3 pt0 = _pt0;
                Vector3 pt1 = _pt1;
                switch (_mode)
                {
                    case Mode.Triangle:
                    {
                        Vector3 pt2 = _pt2;
                        lines.AddLine(pt0, color, pt1, color);
                        lines.AddLine(pt1, color, pt2, color);
                        lines.AddLine(pt2, color, pt0, color);
                        break;
                    }
                    case Mode.Quad:
                    {
                        Vector3 pt2 = _pt2;
                        Vector3 pt3 = _pt3;
                        lines.AddLine(pt0, color, pt1, color);
                        lines.AddLine(pt1, color, pt2, color);
                        lines.AddLine(pt2, color, pt3, color);
                        lines.AddLine(pt3, color, pt0, color);
                        break;
                    }
                    case Mode.BufferedLine:
                        lines.AddLine(pt0, color, pt1, color);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private readonly struct PackedBvhEntryProxyTest : PackedBvh.IProxyTest
        {
            private readonly PackedBvhEntry[] _proxies;
            public PackedBvhEntryProxyTest(PackedBvhEntry[] proxies) => _proxies = proxies;
            public float? Intersects(int proxy, in Ray ray) => _proxies[proxy].Intersects(in ray);
        }

        /// <summary>
        /// Queries for mesh objects that overlap the given ray, up to a given max distance.
        /// </summary>
        public void Query(in Ray ray, List<MyLineSegmentOverlapResult<ulong>> results, float maxDistance = float.PositiveInfinity)
        {
            using (PoolManager.Get(out List<MyLineSegmentOverlapResult<RenderCell>> cells))
            {
                var line = ray.AsLine(maxDistance);
                _cellTree.OverlapAllLineSegment(ref line, cells, 0);
                foreach (var overlap in cells)
                {
                    var cell = overlap.Element;
                    if (cell.Bvh == null) continue;
                    var cellRay = new Ray(ray.Position - cell.Origin, ray.Direction);
                    using (var en = cell.Bvh.IntersectRayProxiesUnordered(in cellRay, new PackedBvhEntryProxyTest(cell.BvhEntries)))
                    {
                        while (en.TryMoveNext(maxDistance))
                        {
                            results.Add(new MyLineSegmentOverlapResult<ulong>
                            {
                                Element = cell.BvhEntries[en.Current].Id,
                                Distance = en.CurrentDist
                            });
                        }
                    }
                }
            }
        }

        #endregion
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicMeshComponent : MyObjectBuilder_EntityComponent
    {
    }
}