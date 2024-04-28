using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Struct;
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
        private readonly OffloadedDictionary<ulong, RenderData> _models = new OffloadedDictionary<ulong, RenderData>();

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

            public uint RenderObject;
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
            base.OnRemovedFromScene();
            if (IsDedicated)
                return;
            _gridRender.BlockRenderablesChanged -= BlockRenderablesChanged;
            DestroyRenderObjects();
        }

        public ulong Create(in ModelData data)
        {
            if (IsDedicated) return NullId;
            var id = _nextId++;
            ref var model = ref _models.Add(id);
            model.Model = data;
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
            if (IsDedicated) return;
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
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicModelsComponent : MyObjectBuilder_EntityComponent
    {
    }
}