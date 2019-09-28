using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Medieval.Entities.Components.Crafting;
using Medieval.Entities.Components.Crafting.Power;
using Medieval.ObjectBuilders.Definitions.Quests.Conditions;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Definitions;
using VRage.Engine;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.Core.Power
{
    [MyComponent(typeof(MyObjectBuilder_EquiVoxelPowerComponent))]
    [MyDefinitionRequired(typeof(EquiVoxelPowerComponentDefinition))]
    public class EquiVoxelPowerComponent : MyEntityComponent, IPowerProvider
    {
        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            MyEntities.OnEntityAdd += EntityAdded;
            MyEntities.OnEntityRemove += EntityRemoved;
            Entity.PositionComp.OnPositionChanged += CheckPosition;

            IsReady = true;
            AddScheduledCallback(RefreshCache, 50L);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            MyEntities.OnEntityAdd -= EntityAdded;
            MyEntities.OnEntityRemove -= EntityRemoved;
            IsReady = false;
        }

        public EquiVoxelPowerComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiVoxelPowerComponentDefinition) def;
        }

        private BoundingSphereD _cachedRegion;

        private void EntityRemoved(MyEntity obj)
        {
            var vox = obj as MyVoxelBase;
            if (vox == null || (vox.RootVoxel != vox && vox.RootVoxel != null)) return;
            var query = new BoundingSphereD(Entity.PositionComp.WorldVolume.Center, Definition.ScanRadius);
            if (vox.PositionComp.WorldVolume.Intersects(query))
                InvalidateCache();
        }

        private void EntityAdded(MyEntity obj)
        {
            var vox = obj as MyVoxelBase;
            if (vox == null || (vox.RootVoxel != vox && vox.RootVoxel != null)) return;
            var query = new BoundingSphereD(Entity.PositionComp.WorldVolume.Center, Definition.ScanRadius);
            if (vox.PositionComp.WorldVolume.Intersects(query))
                InvalidateCache();
        }

        private void CheckPosition(MyPositionComponentBase obj)
        {
            var test = new BoundingSphereD(Entity.PositionComp.WorldVolume.Center, Definition.ScanRadius);
            if (_cachedRegion.Contains(test) == ContainmentType.Contains)
                return;
            InvalidateCache();
        }

        private bool _disturbed;

        public bool Disturbed
        {
            get { return _disturbed; }
            set
            {
                if (_disturbed == value)
                    return;
                _disturbed = value;
                var stateChange = value ? "VoxelPowerDisturbed" : "VoxelPowerUndisturbed";
                if (Definition.DebugMode)
                    MyAPIGateway.Utilities?.ShowNotification("VoxelPowerEvt: " + stateChange);
                Entity?.Components?.Get<MyComponentEventBus>()?.Invoke(stateChange);
            }
        }

        private void InvalidateCache()
        {
            Disturbed = true;
            IsPowered = IsPowered && Definition.PoweredWhenDisturbed;
            RemoveScheduledUpdate(RefreshCache);
            AddScheduledCallback(RefreshCache, (long) Definition.DisturbedTime.TotalMilliseconds);
        }

        private readonly Dictionary<MyVoxelBase, VoxelTracker> _trackedVoxels = new Dictionary<MyVoxelBase, VoxelTracker>();
        private readonly Dictionary<MyVoxelBase, VoxelTracker> _tmpVoxelTrackers = new Dictionary<MyVoxelBase, VoxelTracker>();

        [Update(false)]
        private void RefreshCache(long dt)
        {
            if (_queriesInFlight > 0)
            {
                RemoveScheduledUpdate(RefreshCache);
                AddScheduledCallback(RefreshCache, (long) Definition.DisturbedTime.TotalMilliseconds);
                return;
            }

            if (Entity == null || !Entity.InScene)
            {
                RemoveScheduledUpdate(RefreshCache);
                return;
            }

            _cachedRegion = new BoundingSphereD(Entity.PositionComp.WorldVolume.Center, Definition.ScanRadius + Definition.ScanMargin);

            _tmpVoxelTrackers.Clear();
            foreach (var vox in ((MyVoxelMaps) MyAPIGateway.Session.VoxelMaps).GetAllOverlappingWithSphere(ref _cachedRegion))
            {
                if (vox.RootVoxel != null && vox.RootVoxel != vox)
                    continue;
                VoxelTracker current;
                if (_trackedVoxels.TryGetValue(vox, out current))
                    _trackedVoxels.Remove(vox);
                _tmpVoxelTrackers.Add(vox, current ?? new VoxelTracker(this, vox));
            }

            var voxels = _tmpVoxelTrackers.Count;
            foreach (var old in _trackedVoxels)
                old.Value.Dispose();
            _trackedVoxels.Clear();
            foreach (var kv in _tmpVoxelTrackers)
                _trackedVoxels.Add(kv.Key, kv.Value);
            _tmpVoxelTrackers.Clear();

            _queriesInFlight = 0;
            foreach (var tracker in _trackedVoxels.Values)
                if (tracker.ScheduleQuery())
                    _queriesInFlight++;

            if (Definition.DebugMode)
                MyAPIGateway.Utilities?.ShowNotification($"VoxelPowerDbg: Refreshing...  Q:{_queriesInFlight}, V:{voxels}");
        }

        private int _queriesInFlight;

        private void QueryFinishedAsync()
        {
            AddScheduledCallback(_queryFinishedGameThread, 0L);
        }

        private readonly MyTimedUpdate _queryFinishedGameThread;

        private readonly float[] _totalVoxelData = new float[byte.MaxValue];

        [Update(false)]
        public void QueryFinishedGameThread(long dt)
        {
            _queriesInFlight--;
            if (_queriesInFlight != 0) return;

            for (var i = 0; i < _totalVoxelData.Length; i++)
                _totalVoxelData[i] = 0;
            float multiplier;
            if (Definition.Mode == VoxelPowerCountMode.Volume)
                multiplier = (1L << (3 * Definition.LevelOfDetail)) / (float) byte.MaxValue;
            else
                multiplier = (1L << (2 * Definition.LevelOfDetail));
            // consume data from workers.
            foreach (var worker in _trackedVoxels.Values)
                for (var i = 0; i < Math.Min(_totalVoxelData.Length, worker.Counts.Length); i++)
                    _totalVoxelData[i] += worker.Counts[i] * multiplier;

            // check materials
            var result = Definition.Operator == QuestConditionCompositeOperator.AND;
            foreach (var mtl in Definition.Materials)
            {
                var voxEntry = MyDefinitionManager.Get<MyVoxelMaterialDefinition>(mtl.Material);
                if (voxEntry == null)
                    continue;
                if (Definition.DebugMode)
                    MyAPIGateway.Utilities?.ShowNotification(
                        $"VoxelPowerDbg: Entry {voxEntry.Id.SubtypeName}={_totalVoxelData[voxEntry.Index]} Condition: {(mtl.LessThan ? "<" : ">=")} {mtl.Amount}");
                var eval = mtl.LessThan ? _totalVoxelData[voxEntry.Index] < mtl.Amount : _totalVoxelData[voxEntry.Index] >= mtl.Amount;
                if (Definition.Operator == QuestConditionCompositeOperator.AND)
                {
                    result &= eval;
                    if (!result) break;
                }
                else if (Definition.Operator == QuestConditionCompositeOperator.OR)
                {
                    result |= eval;
                    if (result) break;
                }
            }

            if (Definition.DebugMode)
                MyAPIGateway.Utilities?.ShowNotification("VoxelPowerDbg: Refreshed");
            Disturbed = false;
            IsPowered = result;
        }
        
        private bool _powered;

        public bool IsPowered
        {
            get { return _powered; }
            private set
            {
                if (_powered == value)
                    return;
                var stateChange = value ? "VoxelPowerStart" : "VoxelPowerStop";
                if (Definition.DebugMode)
                    MyAPIGateway.Utilities?.ShowNotification("VoxelPowerEvt: " + stateChange);
                Entity?.Components?.Get<MyComponentEventBus>()?.Invoke(stateChange);
                _powered = value;
                OnPowerChanged?.Invoke(this, value);
            }
        }

        private bool _isReady;

        public EquiVoxelPowerComponent()
        {
            _queryFinishedGameThread = QueryFinishedGameThread;
        }

        public bool IsReady
        {
            get { return _isReady; }
            private set
            {
                if (_isReady == value)
                    return;
                _isReady = value;
            }
        }

        private class VoxelTracker
        {
            private readonly MyVoxelBase _vox;
            private readonly EquiVoxelPowerComponent _component;
            private readonly MyStorageData _storage = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
            internal long[] Counts = new long[256];
            private long[] _countsWorking = new long[256];

            public VoxelTracker(EquiVoxelPowerComponent component, MyVoxelBase vox)
            {
                _component = component;
                _vox = vox;
                _vox.RangeChanged += OnRangeChanged;
            }

            private void OnRangeChanged(MyVoxelBase voxelBase, Vector3I minvoxelchanged, Vector3I maxvoxelchanged, MyStorageDataTypeFlags changeddata)
            {
                Vector3D minWorld, maxWorld;
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(_vox.PositionLeftBottomCorner, ref minvoxelchanged, out minWorld);
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(_vox.PositionLeftBottomCorner, ref maxvoxelchanged, out maxWorld);
                var box = new BoundingBoxD(minWorld, maxWorld);
                if (_component._cachedRegion.Intersects(box))
                    _component.InvalidateCache();
            }

            public void Dispose()
            {
                _vox.RangeChanged -= OnRangeChanged;
            }

            private bool _currentlyExecuting;

            private static readonly Action<VoxelTracker> _execQueryStatic = (x) => x.ExecuteQuery();
            private static readonly Action<VoxelTracker> _completedQueryStatic = (x) => { };

            // TODO remove hack
            private static readonly MyParallelTask _parallelTask = new MyParallelTask();

            public bool ScheduleQuery()
            {
                if (_currentlyExecuting)
                    return false;
                _currentlyExecuting = true;
                _parallelTask.Start(this, _execQueryStatic, _completedQueryStatic);
                return true;
            }

            private void ExecuteQuery()
            {
                try
                {
                    var shape = _component._cachedRegion;
                    var worldMin = shape.Center - shape.Radius;
                    var worldMax = shape.Center + shape.Radius;
                    Vector3I voxMin, voxMax;
                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(_vox.PositionLeftBottomCorner, ref worldMin, out voxMin);
                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(_vox.PositionLeftBottomCorner, ref worldMax, out voxMax);

                    {
                        var tmp = Vector3I.Max(voxMin, voxMax);
                        voxMin = Vector3I.Min(voxMin, voxMax);
                        voxMax = tmp;
                    }

                    {
                        voxMin = voxMin >> _component.Definition.LevelOfDetail;
                        voxMax = (voxMax >> _component.Definition.LevelOfDetail) + 1;
                    }

                    _storage.Resize(voxMin, voxMax);
                    // ReSharper disable once RedundantCast
                    ((IMyStorage) _vox.Storage).ReadRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, _component.Definition.LevelOfDetail, in voxMin,
                        in voxMax);

                    for (var i = 0; i < _countsWorking.Length; i++)
                        _countsWorking[i] = 0;
                    for (var itr = new Vector3I_RangeIterator(ref voxMin, ref voxMax); itr.IsValid(); itr.MoveNext())
                    {
                        var local = itr.Current;
                        var localRoot = local << _component.Definition.LevelOfDetail;
                        Vector3D box;
                        MyVoxelCoordSystems.VoxelCoordToWorldPosition(_vox.PositionLeftBottomCorner, ref localRoot, out box);
                        if (!shape.Intersects(new BoundingBoxD(box, box + (1 << _component.Definition.LevelOfDetail))))
                            continue;
                        var stor = local - voxMin;
                        var idx = _storage.ComputeLinear(ref stor);
                        var content = _storage.Content(idx);
                        if (content == 0)
                            continue;
                        if (_component.Definition.Mode == VoxelPowerCountMode.Surface)
                        {
                            if (content == byte.MaxValue)
                                continue;
                            content = 1;
                        }

                        _countsWorking[_storage.Material(idx)] += content;
                    }

                    {
                        var tmp = _countsWorking;
                        _countsWorking = Counts;
                        Counts = tmp;
                    }
                }
                finally
                {
                    _currentlyExecuting = false;
                    _component.QueryFinishedAsync();
                }
            }
        }

        public bool TryConsumePower(TimeSpan amount)
        {
            return IsPowered;
        }

        public void ReturnPower(TimeSpan amount)
        {
        }

        public TimeSpan ComputePotentialPower(TimeSpan startTime, TimeSpan endTime)
        {
            return IsPowered ? (endTime - startTime) : TimeSpan.Zero;
        }

        public TimeSpan ConsumePotentialPower(TimeSpan consumedTime)
        {
            return IsPowered ? consumedTime : TimeSpan.Zero;
        }

        public float PowerEfficiency => 1f;
        public string PowerProviderName => Definition?.Name ?? "Voxel";
        public IEnumerable<MyInventoryBase> AdditionalInventories => Enumerable.Empty<MyInventoryBase>();
        public event PowerStateChangedDelegate OnPowerChanged;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiVoxelPowerComponent : MyObjectBuilder_EntityComponent
    {
    }
}