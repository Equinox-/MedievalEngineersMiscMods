using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
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
using MySession = VRage.Session.MySession;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyComponent(typeof(MyObjectBuilder_EquiDynamicMeshComponent), AllowAutomaticCreation = true)]
    [MyDependency(typeof(MyRenderComponentGrid), Critical = true)]
    public class EquiDynamicMeshComponent : MyEntityComponent
    {
#pragma warning disable CS0649
        [Automatic]
        private readonly MyRenderComponentGrid _gridRender;
#pragma warning restore CS0649

        public const ulong NullId = 0;
        private const float CellSize = 32;
        private bool _scheduled;
        private readonly HashSet<Vector3I> _dirtyCells = new HashSet<Vector3I>(Vector3I.Comparer);
        private readonly Dictionary<Vector3I, RenderCell> _renderCells = new Dictionary<Vector3I, RenderCell>(Vector3I.Comparer);

        private ulong _nextId = 1;
        private readonly Dictionary<ulong, Vector3I> _objectToCell = new Dictionary<ulong, Vector3I>();

        private uint _currentCullObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

        private uint RequestedCullObject => _gridRender.RenderObjectIDs.Length > 0 ? _gridRender.RenderObjectIDs[0] : MyRenderProxy.RENDER_ID_UNASSIGNED;

        private static bool IsDedicated => ((IMyUtilities)MyAPIUtilities.Static).IsDedicated;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!IsDedicated)
                _gridRender.BlockRenderablesChanged += BlockRenderablesChanged;
        }

        private void BlockRenderablesChanged(MyRenderComponentGrid owner, BlockId block, ListReader<uint> renderObjects)
        {
            if (RequestedCullObject != _currentCullObject)
                MarkDirty();
        }

        public override void OnRemovedFromScene()
        {
            if (!IsDedicated)
                _gridRender.BlockRenderablesChanged -= BlockRenderablesChanged;
            base.OnRemovedFromScene();
        }

        private void MarkDirty()
        {
            if (IsDedicated) return;
            if (_scheduled) return;
            _scheduled = true;
            AddScheduledCallback(ApplyChanges);
        }

        private void MarkCellDirty(Vector3I cell)
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
                using (PoolManager.Get<List<Vector3I>>(out var forRemoval))
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

        public ulong CreateLine(EquiMeshHelpers.LineData data)
        {
            if (IsDedicated) return NullId;
            AllocateObjectForCell((data.Pt0 + data.Pt1) / 2, out var cell, out var id);
            cell.Lines.Add(id, data);
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public ulong CreateSurface(EquiMeshHelpers.SurfaceData data)
        {
            if (IsDedicated) return NullId;
            var center = data.Pt0.Position + data.Pt1.Position + data.Pt2.Position;
            if (data.Pt3.HasValue)
                center += data.Pt3.Value.Position;
            AllocateObjectForCell(center / (data.Pt3.HasValue ? 4 : 3), out var cell, out var id);
            cell.Surfaces.Add(id, data);
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public bool DestroyObject(ulong obj)
        {
            if (IsDedicated) return false;
            if (!_objectToCell.TryGetValue(obj, out var cellKey) || !_renderCells.TryGetValue(cellKey, out var cell))
                return false;
            if (cell.Lines.TryGetValue(obj, out var lineData))
            {
                cell.MaterialToObject.Remove(lineData.Material, obj);
                cell.Lines.Remove(obj);
            }

            if (cell.Surfaces.TryGetValue(obj, out var triangleData))
            {
                cell.MaterialToObject.Remove(triangleData.Material, obj);
                cell.Surfaces.Remove(obj);
            } 

            MarkCellDirty(cellKey);
            return true;
        }

        private void AllocateObjectForCell(Vector3 objectCenter, out RenderCell cell, out ulong id)
        {
            var cellKey = PosToCell(objectCenter);
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
            private readonly Vector3I _key;
            internal readonly Dictionary<ulong, EquiMeshHelpers.LineData> Lines = new Dictionary<ulong, EquiMeshHelpers.LineData>();
            internal readonly Dictionary<ulong, EquiMeshHelpers.SurfaceData> Surfaces = new Dictionary<ulong, EquiMeshHelpers.SurfaceData>();
            internal readonly MyHashSetDictionary<string, ulong> MaterialToObject = new MyHashSetDictionary<string, ulong>();
            private string _modelName;
            private uint _meshGeneration;
            private uint _renderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

            public RenderCell(EquiDynamicMeshComponent owner, Vector3I key)
            {
                _owner = owner;
                _key = key;
            }

            private void BuildMesh(MyModelData mesh)
            {
                var dmm = MySession.Static.Components.Get<DerivedModelManager>();
                if (dmm == null) return;
                mesh.AABB = BoundingBox.CreateInvalid();
                foreach (var kv in MaterialToObject)
                {
                    if (!MaterialTable.TryGetById(kv.Key, out var mtl))
                        continue;
                    dmm.PrepareMaterial(mtl);
                    var vertexOffset = mesh.Positions.Count;
                    var indexOffset = mesh.Indices.Count;
                    foreach (var obj in kv.Value)
                    {
                        if (Lines.TryGetValue(obj, out var line))
                        {
                            if (line.CatenaryLength > 0 && line.UseNaturalGravity)
                            {
                                var center = Vector3.Transform((line.Pt0 + line.Pt1) / 2, _owner.Entity.WorldMatrix);
                                var worldGravity = MyGravityProviderSystem.CalculateNaturalGravityInPoint(center);
                                line.Gravity = Vector3.TransformNormal(worldGravity, _owner.Entity.PositionComp.WorldMatrixNormalizedInv);
                            }

                            EquiMeshHelpers.BuildLine(in line, mesh);
                        }

                        if (Surfaces.TryGetValue(obj, out var triangle))
                        {
                            EquiMeshHelpers.BuildSurface(in triangle, mesh);
                        }
                    }

                    if (mesh.Positions.Count == vertexOffset || mesh.Indices.Count == indexOffset)
                        continue;

                    for (var i = vertexOffset; i < mesh.Positions.Count; i++)
                        mesh.AABB.Include(mesh.Positions[i]);

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

            internal bool ApplyChanges()
            {
                RemoveRenderObject();
                if (MaterialToObject.KeyCount == 0)
                {
                    return true;
                }

                var parent = _owner._gridRender;
                var model = MyRenderProxy.PrepareAddRuntimeModel();
                model.Name = _modelName = $"dyn_mesh_{_owner.Entity.Id}_{_key.X}_{_key.Y}_{_key.Z}_{_meshGeneration++}";
                model.Persistent = false;
                BuildMesh(model.ModelData);
                if (model.ModelData.Positions.Count == 0) return false;
                MyRenderProxy.AddRuntimeModel(model.Name, model);
                _renderObject = MyRenderProxy.CreateRenderEntity(_modelName, _modelName,
                    MatrixD.Identity, MyMeshDrawTechnique.MESH,
                    parent.GetRenderFlags() | RenderFlags.ForceOldPipeline,
                    parent.GetRenderCullingOptions(),
                    parent.GetDiffuseColor(),
                    Vector3.One
                );
                return false;
            }

            internal void SetCullObject(uint parent)
            {
                if (_renderObject == MyRenderProxy.RENDER_ID_UNASSIGNED) return;
                MyRenderProxy.SetParentCullObject(_renderObject, parent, Matrix.Identity);
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicMeshComponent : MyObjectBuilder_EntityComponent
    {
    }
}