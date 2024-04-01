using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entity.Block;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
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
        private readonly Dictionary<ulong, ModelData> _models = new Dictionary<ulong, ModelData>();
        private readonly Dictionary<ulong, uint> _renderObjects = new Dictionary<ulong, uint>();

        public struct ModelData
        {
            public string Model;
            public Matrix Matrix;
            public PackedHsvShift ColorMask;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (IsDedicated) return;
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
            if (!IsDedicated)
            {
                _gridRender.BlockRenderablesChanged -= BlockRenderablesChanged;
                foreach (var ro in _renderObjects.Values)
                    MyRenderProxy.RemoveRenderObject(ro);
                _renderObjects.Clear();
            }

            base.OnRemovedFromScene();
        }

        public ulong Create(in ModelData data)
        {
            if (IsDedicated) return NullId;
            var id = _nextId++;
            _models.Add(id, data);
            if (_currentCullObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                EnsureRenderObject(id, in data, _currentCullObject);
            return id;
        }

        public bool DestroyObject(ulong obj)
        {
            if (IsDedicated) return false;
            if (_renderObjects.TryGetValue(obj, out var ro))
            {
                MyRenderProxy.RemoveRenderObject(ro);
                _renderObjects.Remove(obj);
            }

            _models.Remove(obj);
            return true;
        }

        private void ApplyAll()
        {
            if (IsDedicated) return;
            var newCullObject = RequestedCullObject;
            if (_currentCullObject == newCullObject) return;

            if (newCullObject == MyRenderProxy.RENDER_ID_UNASSIGNED)
            {
                // Destroy everything
                foreach (var ro in _renderObjects.Values)
                    MyRenderProxy.RemoveRenderObject(ro);
                _currentCullObject = newCullObject;
                _renderObjects.Clear();
                return;
            }

            foreach (var model in _models)
                EnsureRenderObject(model.Key, model.Value, _currentCullObject);

            _currentCullObject = newCullObject;
        }

        private void EnsureRenderObject(ulong id, in ModelData data, uint cullObject)
        {
            if (!_renderObjects.TryGetValue(id, out var ro))
            {
                // Create render object.
                _renderObjects[id] = ro = MyRenderProxy.CreateRenderEntity(
                    $"dynamic_model_{Entity.EntityId}_{id}",
                    data.Model,
                    MatrixD.Identity,
                    MyMeshDrawTechnique.MESH,
                    RenderFlags.Visible | RenderFlags.ForceOldPipeline,
                    CullingOptions.Default,
                    Color.White,
                    data.ColorMask);
            }

            // Move render object to cull object.
            MyRenderProxy.SetParentCullObject(ro, cullObject, data.Matrix, true);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicModelsComponent : MyObjectBuilder_EntityComponent
    {
    }
}