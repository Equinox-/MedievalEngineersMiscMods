using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Util;
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
            if (IsDedicated) return;
            _gridRender.BlockRenderablesChanged += BlockRenderablesChanged;
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
            if (IsDedicated) return;
            if (_scheduled) return;
            _scheduled = true;
            AddScheduledCallback(ApplyChanges);
        }

        private void MarkCellDirty(CellKey cell)
        {
            if (IsDedicated) return;
            MarkDirty();
            _dirtyCells.Add(cell);
        }

        [Update(false)]
        public void ApplyChanges(long dt)
        {
            if (IsDedicated) return;
            _scheduled = false;
            var newCullObject = RequestedCullObject;
            // Destroy everything
            if (newCullObject == MyRenderProxy.RENDER_ID_UNASSIGNED)
            {
                foreach (var cell in _renderCells.Values)
                    cell.RemoveRenderObject();
                _currentCullObject = newCullObject;
                _dirtyCells.Clear();
                return;
            }

            // Create everything from scratch
            if (_currentCullObject == MyRenderProxy.RENDER_ID_UNASSIGNED)
            {
                using (PoolManager.Get<List<CellKey>>(out var forRemoval))
                {
                    foreach (var cell in _renderCells)
                    {
                        if (cell.Value.ApplyChanges())
                            forRemoval.Add(cell.Key);
                        cell.Value.SetCullObject(newCullObject);
                    }

                    foreach (var cell in forRemoval)
                        _renderCells.Remove(cell);
                }

                _currentCullObject = newCullObject;
                _dirtyCells.Clear();
                return;
            }

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
            if (IsDedicated) return NullId;
            AllocateObjectForCell(data.Position, new CellArgs(data.ColorMask, data.Ghost), out var cell, out var id);
            cell.Decals.Add(id, in data);
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public ulong CreateLine(in EquiMeshHelpers.LineData data)
        {
            if (IsDedicated) return NullId;
            AllocateObjectForCell((data.Pt0 + data.Pt1) / 2, new CellArgs(data.ColorMask, data.Ghost), out var cell, out var id);
            cell.Lines.Add(id, in data);
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public ulong CreateSurface(in EquiMeshHelpers.SurfaceData data)
        {
            if (IsDedicated) return NullId;
            var center = data.Pt0.Position + data.Pt1.Position + data.Pt2.Position;
            if (data.Pt3.HasValue)
                center += data.Pt3.Value.Position;
            AllocateObjectForCell(center / (data.Pt3.HasValue ? 4 : 3), new CellArgs(data.ColorMask, data.Ghost), out var cell, out var id);
            cell.Surfaces.Add(id, in data);
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public bool DestroyObject(ulong obj)
        {
            if (IsDedicated) return false;
            if (!_objectToCell.TryGetValue(obj, out var cellKey) || !_renderCells.TryGetValue(cellKey, out var cell))
                return false;
            if (cell.Decals.TryGetValue(obj, out var decalData))
            {
                cell.MaterialToObject.Remove(decalData.Value.Material, obj);
                cell.Decals.Remove(obj);
            }

            if (cell.Lines.TryGetValue(obj, out var lineData))
            {
                cell.MaterialToObject.Remove(lineData.Value.Material, obj);
                cell.Lines.Remove(obj);
            }

            if (cell.Surfaces.TryGetValue(obj, out var triangleData))
            {
                cell.MaterialToObject.Remove(triangleData.Value.Material, obj);
                cell.Surfaces.Remove(obj);
            }

            MarkCellDirty(cellKey);
            return true;
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

        private sealed class RenderCell
        {
            private readonly EquiDynamicMeshComponent _owner;
            private readonly CellKey _key;
            internal readonly OffloadedDictionary<ulong, EquiMeshHelpers.DecalData> Decals = new OffloadedDictionary<ulong, EquiMeshHelpers.DecalData>();
            internal readonly OffloadedDictionary<ulong, EquiMeshHelpers.LineData> Lines = new OffloadedDictionary<ulong, EquiMeshHelpers.LineData>();
            internal readonly OffloadedDictionary<ulong, EquiMeshHelpers.SurfaceData> Surfaces = new OffloadedDictionary<ulong, EquiMeshHelpers.SurfaceData>();
            internal readonly MyHashSetDictionary<string, ulong> MaterialToObject = new MyHashSetDictionary<string, ulong>();
            private readonly string _modelPrefix;
            private string _modelName;
            private uint _meshGeneration;
            private uint _renderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

            public BoundingBox LocalBounds;
            public int Triangles;
            public int Materials;

            public RenderCell(EquiDynamicMeshComponent owner, CellKey key)
            {
                _owner = owner;
                _key = key;
                _modelPrefix = $"dyn_mesh_{_owner.Entity.Id}_{_key.Position}_{_key.Args.Color}_{_key.Args.Ghost}";
            }

            private Vector3 Origin => (_key.Position + 1) * CellSize;

            private void BuildMesh(MyModelData mesh)
            {
                var offset = -Origin;
                mesh.AABB = BoundingBox.CreateInvalid();


                var localGravity = Vector3.TransformNormal(
                    MyGravityProviderSystem.CalculateNaturalGravityInPoint(Vector3.Transform(
                        _owner.Entity.PositionComp.LocalAABB.Center,
                        _owner.Entity.WorldMatrix)),
                    _owner.Entity.PositionComp.WorldMatrixNormalizedInv);

                foreach (var kv in MaterialToObject)
                {
                    if (MaterialTable.TryGetById(kv.Key, out var mtl))
                        mtl.EnsurePrepared();
                    var vertexOffset = mesh.Positions.Count;
                    var indexOffset = mesh.Indices.Count;
                    foreach (var obj in kv.Value)
                    {
                        if (Lines.TryGetValue(obj, out var line))
                            EquiMeshHelpers.BuildLine(in line.Value, mesh, localGravity, offset);

                        if (Surfaces.TryGetValue(obj, out var surface))
                            EquiMeshHelpers.BuildSurface(in surface.Value, mesh, offset);

                        if (Decals.TryGetValue(obj, out var decal))
                            EquiMeshHelpers.BuildDecal(in decal.Value, mesh, offset);
                    }

                    if (mesh.Positions.Count == vertexOffset || mesh.Indices.Count == indexOffset)
                        continue;
                    mesh.Sections.Add(new MyRuntimeSectionInfo
                    {
                        IndexStart = indexOffset,
                        TriCount = (mesh.Indices.Count - indexOffset) / 3,
                        MaterialName = mtl.MaterialName,
                    });
                }

                for (var i = 0; i < mesh.Positions.Count; i++)
                    mesh.AABB.Include(mesh.Positions[i]);

                LocalBounds = mesh.AABB;
                LocalBounds = LocalBounds.Translate(Origin);
                Triangles = mesh.Indices.Count / 3;
                Materials = mesh.Sections.Count;
            }

            internal void RemoveRenderObject()
            {
                if (_renderObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                    MyRenderProxy.RemoveRenderObject(ref _renderObject);
            }

            internal bool ApplyChanges()
            {
                RemoveRenderObject();
                if (MaterialToObject.KeyCount == 0)
                {
                    Materials = 0;
                    Triangles = 0;
                    return true;
                }

                var parent = _owner._gridRender;
                var model = MyRenderProxy.PrepareAddRuntimeModel();
                model.Name = _modelName = $"{_modelPrefix}_{_meshGeneration++}";
                model.ReplacedModel = null;
                model.Persistent = false;
#if VRAGE_API_1
                model.Dynamic = true;
#endif
                BuildMesh(model.ModelData);
                if (model.ModelData.Positions.Count == 0)
                {
                    model.Close();
// #if VRAGE_API_1
//                     MyRenderProxy.MessagePool.Return(model);
// #endif
                    return false;
                }

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
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicMeshComponent : MyObjectBuilder_EntityComponent
    {
    }
}