//#define VIS_INV_ACCESS

using System;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.Components;
using Medieval.Entities.Components;
using Medieval.ObjectBuilders.Definitions.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Definitions.Components;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Inventory
{
    [MyComponent(typeof(MyObjectBuilder_EquiInventoryPhysicsComponent))]
    [MyDefinitionRequired]
//    [MyDependency(typeof(MyPositionComponentBase), Critical = true)]
//    [MyDependency(typeof(MyHierarchyComponentBase), Critical = true)]
    public class EquiInventoryPhysicsComponent : MyPhantomComponent
    {
        private MyPositionComponentBase _positionComponent;
        private readonly MultiComponentReference<MyInventoryBase> _inventories = new MultiComponentReference<MyInventoryBase>();

        public EquiInventoryPhysicsComponent()
        {
            _inventories.ComponentAdded += (inv) =>
            {
                inv.ContentsChanged += InvOnContentsChanged;
                CheckNeedsUpdate();
            };
            _inventories.ComponentRemoved += (inv) =>
            {
                inv.ContentsChanged -= InvOnContentsChanged;
                CheckNeedsUpdate();
            };
        }

        private void InvOnContentsChanged(MyInventoryBase obj)
        {
            CheckNeedsUpdate();
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _inventories.AddToContainer(Container, true, false);
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _updateScheduled = false;
            _positionComponent = Container.Get<MyPositionComponentBase>();
            OnPhantomEntered += PhantomEntered;
            _positionComponent.OnPositionChanged += PosChanged;
            PosChanged(_positionComponent);
            CheckNeedsUpdate();
        }

        public override void OnRemovedFromScene()
        {
            OnPhantomEntered -= PhantomEntered;
            if (_positionComponent != null)
                _positionComponent.OnPositionChanged -= PosChanged;
            _positionComponent = null;
            _updateScheduled = false;
            base.OnRemovedFromScene();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            _inventories.RemoveFromContainer();
            base.OnBeforeRemovedFromContainer();
        }

        private void PhantomEntered(MyPhantomComponent phantom, MyEntity ent)
        {
            if (phantom != this)
                return;
            if (AngleToUp < Definition.FillMargin)
                ConsumeEntity(ent);
            CheckNeedsUpdate();
        }

        private float AngleToUp
        {
            get
            {
                var myUp = Vector3.TransformNormal(Definition.Up, _positionComponent.WorldMatrix);
                var globalUp = -Vector3.Normalize(MyGravityProviderSystem.CalculateTotalGravityInPoint(_positionComponent.WorldMatrix.Translation));
                return (float) Math.Acos(myUp.Dot(globalUp));
            }
        }

        private bool _updateScheduled;

        private void MarkForUpdate()
        {
            if (_updateScheduled)
                return;
            _updateScheduled = true;
            AddScheduledCallback(Update, Definition.DropIntervalMs);
        }

        private bool _isDumping, _isFilling;

        private void PosChanged(MyPositionComponentBase obj)
        {
            var up = AngleToUp;
            _isDumping = up > Definition.EmptyMargin;
            _isFilling = up < Definition.FillMargin;
            CheckNeedsUpdate();
        }

        private void CheckNeedsUpdate()
        {
            if (!VRage.Session.MySession.Static.IsServer)
                return;
            if (_updateScheduled)
                return;
            if (_isDumping)
            {
                foreach (var k in _inventories.Components)
                    if (k.ItemCount > 0)
                    {
                        MarkForUpdate();
                        return;
                    }
            }

            // ReSharper disable once InvertIf
            if (_isFilling && ContainedEntities.Count > 0)
            {
                foreach (var ent in ContainedEntities)
                {
                    var item = (ent as MyFloatingObject)?.Item ?? ent.Components.Get<MyInventoryItemComponent>()?.Item;
                    if (item == null)
                        continue;
                    foreach (var k in _inventories.Components)
                        if (k.CanAddItems(item.DefinitionId, 1))
                        {
                            MarkForUpdate();
                            return;
                        }
                }
            }
        }

        private void Update(long dt)
        {
            _updateScheduled = false;
            if (_isFilling)
            {
                foreach (var ent in ContainedEntities)
                    ConsumeEntity(ent);
            }

            // rate limit floating object dumping
            var maxFloating = (MyAPIGateway.Session?.MaxFloatingObjects ?? 0) * 2 / 3;
            if (maxFloating == 0)
                maxFloating = int.MaxValue;
            if (_isDumping && (MyFloatingObjects.FloatingOreCount + MyFloatingObjects.FloatingItemCount) < maxFloating)
            {
                var itemSpawned = false;
                foreach (var inv in _inventories.Components)
                {
                    var outputPosition = Definition.SpawnOffset * _positionComponent.WorldMatrix;
                    MyVisualInventoryComponent visual = null;
                    MyModelAttachmentComponent attacher = null;
                    MyVisualInventoryComponentDefinition visualDef = null;
                    if (Definition.UsePositionFromVisualInventory || Definition.UseOrientationFromVisualInventory)
                    {
                        visual = inv.Container.Get<MyVisualInventoryComponent>();
                        attacher = inv.Container.Get<MyModelAttachmentComponent>();
                        visualDef = ComponentDefinition<MyVisualInventoryComponentDefinition>(visual);
                    }

                    for (var slot = inv.Items.Count - 1; slot >= 0; slot--)
                    {
                        var item = inv.Items[slot];
                        if (item.Amount <= 0)
                            continue;

                        var slotOutputPosition = outputPosition;
                        if (visualDef != null && attacher != null)
                        {
#if VIS_INV_ACCESS
                            foreach (var mapping in visualDef.Mappings)
                                if (mapping.TrackedInventory == inv.InventoryId && mapping.Slot == slot)
                                {
                                    foreach (var attached in attacher.GetAttachedEntities(mapping.AttachmentPointName))
                                    {
                                        var wm = attached.WorldMatrix;
                                        if (Definition.UsePositionFromVisualInventory)
                                            slotOutputPosition.Translation = wm.Translation;
                                        if (Definition.UseOrientationFromVisualInventory)
                                        {
                                            var tra = slotOutputPosition.Translation;
                                            slotOutputPosition = attached.WorldMatrix;
                                            slotOutputPosition.Translation = tra;
                                        }
                                        break;
                                    }
                                    break;
                                }
#else
                            var attacherDef = ComponentDefinition<MyModelAttachmentComponentDefinition>(attacher);
                            var found = false;
                            var fixedKey = $"_{slot:00}";
                            foreach (var point in attacherDef.AttachmentPoints)
                            {
                                if (point.Key.String.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    point.Key.String.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                var certain = point.Key.String.EndsWith(fixedKey);
                                foreach (var attached in attacher.GetAttachedEntities(point.Key))
                                    if (attached.InScene && attached.Parent != null)
                                    {
                                        var wm = attached.WorldMatrix;
                                        if (Definition.UsePositionFromVisualInventory)
                                            slotOutputPosition.Translation = wm.Translation;

                                        if (Definition.UseOrientationFromVisualInventory)
                                        {
                                            var tra = slotOutputPosition.Translation;
                                            slotOutputPosition = attached.WorldMatrix;
                                            slotOutputPosition.Translation = tra;
                                        }

                                        found = true;
                                        break;
                                    }

                                if (found && certain)
                                    break;
                            }
#endif
                        }

                        var resultPos = FindFreePlace(slotOutputPosition.Translation, 0.1f, .1f);
                        if (!resultPos.HasValue)
                        {
                            MyAPIGateway.Utilities?.ShowNotification("Blocked");
                            continue;
                        }

                        slotOutputPosition.Translation = resultPos.Value;
                        MyFloatingObjects.Spawn(item, slotOutputPosition, inv.Container.Get<MyPhysicsComponentBase>() ??
                                                                          inv.Container.Get<MyHierarchyComponentBase>()?.Parent?.Container
                                                                              .Get<MyPhysicsComponentBase>());
                        inv.Remove(item);
                        itemSpawned = true;
                        break;
                    }

                    if (itemSpawned)
                        break;
                }
            }

            CheckNeedsUpdate();
        }

        private Vector3D? FindFreePlace(Vector3D start, float rad, float step)
        {
            var probe = _positionComponent.WorldMatrix.Up;

            var direction = Definition.SpawnOffset.Translation;
            var len = direction.Length();
            if (len >= 1e-6f)
                probe = Vector3D.TransformNormal(direction / len, _positionComponent.WorldMatrix);

            for (var i = 0; i < 20; i++)
            {
                var test = start + i * step * probe;
                if (MyEntities.FindFreePlace(test, rad, -1, 1, 1f, true).HasValue)
                {
                    // cast ray back inwards (because silly, silly phantoms)
                    IHitInfo hit;
                    if (!MyAPIGateway.Physics.CastRay(test, start, out hit, 9))
                        return start;
                    return hit.Position + Vector3.Normalize(test - start) * rad;
                }
            }

            return null;
        }

        private static TComponentDef ComponentDefinition<TComponentDef>(MyEntityComponent vic) where TComponentDef : MyEntityComponentDefinition
        {
            var def = vic.Entity?.Definition?.Components;
            if (def == null)
            {
                var block = vic.Entity as MyCubeBlock;
                if (block?.BlockDefinition != null)
                    def = MyDefinitionManager.Get<MyContainerDefinition>(block.BlockDefinition.Id)?.Components;
            }

            if (def == null)
                return null;
            foreach (var k in def)
            {
                var definition = k.Definition as TComponentDef;
                if (definition != null)
                    return definition;
            }

            return null;
        }

        public new EquiInventoryPhysicsComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiInventoryPhysicsComponentDefinition) def;
        }

        private void ConsumeEntity(MyEntity ent)
        {
            if (!VRage.Session.MySession.Static.IsServer)
                return;
            if (ent.Closed || ent.MarkedForClose)
                return;
            var item = (ent as MyFloatingObject)?.Item ?? ent.Components.Get<MyInventoryItemComponent>()?.Item;

            var itemId = item?.DefinitionId;
            var amount = item?.Amount ?? 1;

            IMySlimBlock block = null;
            if (item == null)
            {
                var grid = ent as MyCubeGrid;
                if (grid == null)
                    return;
                if (grid.BlocksCount != 1 || grid.Physics == null)
                    return;
                {
                    var tmp = Entity;
                    while (tmp != null)
                    {
                        if (tmp == grid)
                            return;
                        tmp = tmp.Parent;
                    }
                }
                if ((int) grid.Physics.Mass != grid.GetCurrentMass())
                    return;
                block = grid.CubeBlocks.FirstOrDefault();
                if (block == null)
                    return;
                itemId = block.BlockDefinition.Id;
            }

            foreach (var k in _inventories.Components)
            {
                // Check constraint
                if (!k.CanAddItems(itemId.Value, 1))
                    return;

                var count = Math.Min(amount, k.ComputeAmountThatFits(itemId.Value));
                if (count == 0)
                    continue;

                if (!k.AddItems(itemId.Value, count))
                    continue;

                amount -= count;
                if (amount > 0)
                    continue;
                ent.Close();
                return;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiInventoryPhysicsComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiInventoryPhysicsComponentDefinition))]
    public class EquiInventoryPhysicsComponentDefinition : MyPhantomComponentDefinition
    {
        public Vector3 Up { get; private set; }

        public float FillMargin { get; private set; }

        public float EmptyMargin { get; private set; }

        public bool UsePositionFromVisualInventory { get; private set; }

        public bool UseOrientationFromVisualInventory { get; private set; }

        public Matrix SpawnOffset { get; private set; }

        public long DropIntervalMs { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiInventoryPhysicsComponentDefinition) def;

            Up = Vector3.Normalize(ob.Up ?? Vector3.Up);
            FillMargin = ob.FillMargin.HasValue ? MathHelper.ToRadians(ob.FillMargin.Value) : float.NegativeInfinity;
            EmptyMargin = ob.EmptyMargin.HasValue ? MathHelper.ToRadians(ob.EmptyMargin.Value) : float.PositiveInfinity;
            UsePositionFromVisualInventory = ob.UsePositionFromVisualInventory ?? false;
            UseOrientationFromVisualInventory = ob.UseOrientationFromVisualInventory ?? true;

            var m = Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(ob.SpawnRotation?.X ?? 0), MathHelper.ToRadians(ob.SpawnRotation?.Y ?? 0),
                MathHelper.ToRadians(ob.SpawnRotation?.Z ?? 0));
            m.Translation = ob.SpawnOffset ?? ob.DetectorOffset ?? Vector3.Zero;
            SpawnOffset = m;
            DropIntervalMs = ((TimeSpan) (ob.DropInterval ?? new TimeDefinition {Milliseconds = 250})).Milliseconds;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiInventoryPhysicsComponentDefinition : MyObjectBuilder_PhantomComponentDefinition
    {
        /// <summary>
        /// Up direction
        /// </summary>
        public SerializableVector3? Up;

        /// <summary>
        /// Item spawn offset
        /// </summary>
        public SerializableVector3? SpawnOffset;

        /// <summary>
        /// Item spawn orientation
        /// </summary>
        public SerializableVector3? SpawnRotation;

        /// <summary>
        /// Maximum degrees from up for the object to still suck up item
        /// </summary>
        public float? FillMargin;

        /// <summary>
        /// Minimum degrees from up for the object to start dumping its items
        /// </summary>
        public float? EmptyMargin;

        public bool? UsePositionFromVisualInventory;
        public bool? UseOrientationFromVisualInventory;

        public TimeDefinition? DropInterval;
    }
}