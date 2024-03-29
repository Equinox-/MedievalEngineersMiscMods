using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Util;
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
        private readonly HashSet<CellKey> _dirtyCells = new HashSet<CellKey>();
        private readonly Dictionary<CellKey, RenderCell> _renderCells = new Dictionary<CellKey, RenderCell>();

        private ulong _nextId = 1;
        private readonly Dictionary<ulong, CellKey> _objectToCell = new Dictionary<ulong, CellKey>();

        private uint _currentCullObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

        private uint RequestedCullObject => _gridRender.RenderObjectIDs.Length > 0 ? _gridRender.RenderObjectIDs[0] : MyRenderProxy.RENDER_ID_UNASSIGNED;

        private static bool IsDedicated => ((IMyUtilities)MyAPIUtilities.Static).IsDedicated;

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly Vector3I Position;
            public readonly PackedHsvShift Color;

            public CellKey(Vector3I position, PackedHsvShift color)
            {
                Position = position;
                Color = color;
            }

            public bool Equals(CellKey other) => Position.Equals(other.Position) && Color.Equals(other.Color);

            public override bool Equals(object obj) => obj is CellKey other && Equals(other);

            public override int GetHashCode() => (Position.GetHashCode() * 397) ^ Color.GetHashCode();
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

        public ulong CreateDecal(EquiMeshHelpers.DecalData data)
        {
            if (IsDedicated) return NullId;
            AllocateObjectForCell(data.Position, data.ColorMask, out var cell, out var id);
            cell.Decals.Add(id, data);
            cell.MaterialToObject.Add(data.Material, id);
            return id;
        }

        public ulong CreateLine(EquiMeshHelpers.LineData data)
        {
            if (IsDedicated) return NullId;
            AllocateObjectForCell((data.Pt0 + data.Pt1) / 2, data.ColorMask, out var cell, out var id);
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
            AllocateObjectForCell(center / (data.Pt3.HasValue ? 4 : 3), data.ColorMask, out var cell, out var id);
            cell.Surfaces.Add(id, data);
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
                cell.MaterialToObject.Remove(decalData.Material, obj);
                cell.Decals.Remove(obj);
            }

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

        private void AllocateObjectForCell(Vector3 objectCenter, PackedHsvShift colorMask, out RenderCell cell, out ulong id)
        {
            var cellPos = PosToCell(objectCenter);
            var cellKey = new CellKey(cellPos, colorMask);
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
            internal readonly Dictionary<ulong, EquiMeshHelpers.DecalData> Decals = new Dictionary<ulong, EquiMeshHelpers.DecalData>();
            internal readonly Dictionary<ulong, EquiMeshHelpers.LineData> Lines = new Dictionary<ulong, EquiMeshHelpers.LineData>();
            internal readonly Dictionary<ulong, EquiMeshHelpers.SurfaceData> Surfaces = new Dictionary<ulong, EquiMeshHelpers.SurfaceData>();
            internal readonly MyHashSetDictionary<string, ulong> MaterialToObject = new MyHashSetDictionary<string, ulong>();
            private readonly string _modelPrefix;
            private string _modelName;
            private uint _meshGeneration;
            private uint _renderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

            public RenderCell(EquiDynamicMeshComponent owner, CellKey key)
            {
                _owner = owner;
                _key = key;
                _modelPrefix = $"dyn_mesh_{_owner.Entity.Id}_{_key.Position}_{_key.Color}";
            }

            private Vector3 Origin => (_key.Position + 1) * CellSize;

            private void BuildMesh(MyModelData mesh)
            {
                var origin = Origin;
                mesh.AABB = BoundingBox.CreateInvalid();
                foreach (var kv in MaterialToObject)
                {
                    if (MaterialTable.TryGetById(kv.Key, out var mtl))
                        mtl.EnsurePrepared();
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

                            line.Pt0 -= origin;
                            line.Pt1 -= origin;

                            EquiMeshHelpers.BuildLine(in line, mesh);
                        }

                        if (Surfaces.TryGetValue(obj, out var surface))
                        {
                            surface.Pt0.Position -= origin;
                            surface.Pt1.Position -= origin;
                            surface.Pt2.Position -= origin;
                            if (surface.Pt3.HasValue)
                            {
                                var pt3 = surface.Pt3.Value;
                                pt3.Position -= origin;
                                surface.Pt3 = pt3;
                            }
                            EquiMeshHelpers.BuildSurface(in surface, mesh);
                        }

                        if (Decals.TryGetValue(obj, out var decal))
                        {
                            decal.Position -= origin;
                            EquiMeshHelpers.BuildDecal(in decal, mesh);
                        }
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
                    depthBias: 255
                );
                MyRenderProxy.UpdateRenderEntity(_renderObject, null, (Vector3) _key.Color);
                return false;
            }

            internal void SetCullObject(uint parent)
            {
                if (_renderObject == MyRenderProxy.RENDER_ID_UNASSIGNED) return;
                MyRenderProxy.SetParentCullObject(_renderObject, parent, Matrix.CreateTranslation(Origin));
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicMeshComponent : MyObjectBuilder_EntityComponent
    {
    }
}