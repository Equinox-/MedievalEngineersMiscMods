using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Equinox76561198048419394.Core.Util.Struct;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entity.Block;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;
using VRageRender;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyComponent(typeof(MyObjectBuilder_EquiDynamicModelsComponent), AllowAutomaticCreation = true)]
    [MyDependency(typeof(MyRenderComponentGrid), Critical = true)]
    public class EquiDynamicModelsComponent : MyEntityComponent
    {
#pragma warning disable CS0649
        [Automatic]
        private readonly MyRenderComponentGrid _gridRender;
#pragma warning restore CS0649
        private uint RequestedCullObject => _gridRender.RenderObjectIDs.Length > 0 ? _gridRender.RenderObjectIDs[0] : MyRenderProxy.RENDER_ID_UNASSIGNED;
        private static bool IsDedicated => ((IMyUtilities)MyAPIUtilities.Static).IsDedicated;

        private const ulong NullId = 0;
        private ulong _nextId = 1;
        private uint _currentCullObject = MyRenderProxy.RENDER_ID_UNASSIGNED;
        private readonly OffloadedDictionary<ulong, RenderData> _models = new OffloadedDictionary<ulong, RenderData>();
        private readonly MyDynamicAABBTree _tree = new MyDynamicAABBTree();

        public struct ModelData
        {
            public string Model;
            public Matrix Matrix;
            public PackedHsvShift ColorMask;
            public bool Ghost;
        }

        private struct RenderData
        {
            public ModelData Model;

            public Matrix MatrixInv;

            public uint RenderObject;

            public int TreeProxy;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!IsDedicated)
                _gridRender.BlockRenderablesChanged += BlockRenderablesChanged;
            ApplyAll();
        }

        private void BlockRenderablesChanged(MyRenderComponentGrid owner, BlockId block, ListReader<uint> renderObjects)
        {
            if (RequestedCullObject != _currentCullObject)
                ApplyAll();
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (!IsDedicated)
                _gridRender.BlockRenderablesChanged -= BlockRenderablesChanged;
            DestroyRenderObjects();
        }

        public ulong Create(in ModelData data)
        {
            var id = _nextId++;
            ref var model = ref _models.Add(id);
            model.Model = data;
            model.MatrixInv = Matrix.Invert(data.Matrix);

            var box = MyModels.GetModelOnlyModelInfo(data.Model)?.BoundingBox;
            if (box != null)
            {
                BoundingBox.Transform(box.Value, in data.Matrix, out var globalBox);
                model.TreeProxy = _tree.AddProxy(ref globalBox, new CullProxy(id, new OrientedBoundingBox(box.Value, data.Matrix)), 0);
            }
            else
                model.TreeProxy = MyDynamicAABBTree.NullNode;

            model.RenderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;
            if (_currentCullObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                EnsureRenderObject(id, ref model, _currentCullObject);
            return id;
        }

        public bool DestroyObject(ulong obj)
        {
            if (IsDedicated) return false;
            if (_models.TryGetValue(obj, out var ro))
            {
                ref var model = ref ro.Value;
                if (model.TreeProxy != MyDynamicAABBTree.NullNode)
                {
                    _tree.RemoveProxy(model.TreeProxy);
                    model.TreeProxy = MyDynamicAABBTree.NullNode;
                }

                if (model.RenderObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                    MyRenderProxy.RemoveRenderObject(ref model.RenderObject);
            }

            _models.Remove(obj);
            return true;
        }

        private void DestroyRenderObjects()
        {
            foreach (var handle in _models.Values)
            {
                ref var model = ref handle.Value;
                if (model.RenderObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                    MyRenderProxy.RemoveRenderObject(ref model.RenderObject);
            }
        }

        private void ApplyAll()
        {
            var newCullObject = RequestedCullObject;
            if (_currentCullObject == newCullObject) return;

            if (newCullObject == MyRenderProxy.RENDER_ID_UNASSIGNED)
            {
                DestroyRenderObjects();
                _currentCullObject = newCullObject;
                return;
            }

            foreach (var model in _models)
                EnsureRenderObject(model.Key, ref model.Value, newCullObject);

            _currentCullObject = newCullObject;
        }

        private void EnsureRenderObject(ulong id, ref RenderData data, uint cullObject)
        {
            if (IsDedicated) return;

            if (data.RenderObject == MyRenderProxy.RENDER_ID_UNASSIGNED)
            {
                // Create render object.
                data.RenderObject = MyRenderProxy.CreateRenderEntity(
                    $"dynamic_model_{Entity.EntityId}_{id}",
                    data.Model.Model,
                    MatrixD.Identity,
                    MyMeshDrawTechnique.MESH,
                    RenderFlags.Visible | RenderFlags.ForceOldPipeline,
                    CullingOptions.Default,
                    Color.White,
                    data.Model.ColorMask,
                    dithering: data.Model.Ghost ? EquiDynamicMeshComponent.GhostDithering : 0);
            }

            // Move render object to cull object.
            MyRenderProxy.SetParentCullObject(data.RenderObject, cullObject, data.Model.Matrix, true);
        }

        private class CullProxy
        {
            public readonly ulong Id;
            public OrientedBoundingBox Box;

            public CullProxy(ulong id, in OrientedBoundingBox obb)
            {
                Id = id;
                Box = obb;
            }
        }

        public void Query(in Ray ray, List<MyLineSegmentOverlapResult<ulong>> results, float maxDistance = float.PositiveInfinity)
        {
            using (PoolManager.Get(out List<MyLineSegmentOverlapResult<CullProxy>> proxies))
            {
                var line = ray.AsLine(maxDistance);
                _tree.OverlapAllLineSegment(ref line, proxies, 0);
                foreach (var proxy in proxies)
                {
                    var dist = proxy.Element.Box.Intersects(ref line);
                    if (!dist.HasValue || dist > maxDistance) continue;
                    if (!_models.TryGetValue(proxy.Element.Id, out var handle)) continue;
                    ref var model = ref handle.Value;

                    var bvh = MySession.Static.Components.Get<DerivedModelManager>()?.GetMaterialBvh(model.Model.Model);
                    if (bvh != null)
                    {
                        Ray localRay = default;
                        Vector3.Transform(ref line.From, ref model.MatrixInv, out localRay.Position);
                        Vector3.TransformNormal(ref line.Direction, ref model.MatrixInv, out localRay.Direction);
                        if (!bvh.RayCast(in localRay, out _, out _, out var localDist))
                            continue;
                        var localHitPos = localRay.Position + localRay.Direction * localDist;
                        Vector3.Transform(ref localHitPos, ref model.Model.Matrix, out var hitPos);
                        dist = Vector3.Distance(ray.Position, hitPos);
                    }
                    if (dist <= maxDistance)
                        results.Add(new MyLineSegmentOverlapResult<ulong> { Element = proxy.Element.Id, Distance = dist.Value });
                }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicModelsComponent : MyObjectBuilder_EntityComponent
    {
    }
}