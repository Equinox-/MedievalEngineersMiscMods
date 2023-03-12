using VRage.Game;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Game.Definitions;
using System.Xml.Serialization;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Game.Entity;
using VRageMath;
using VRage.Components;
using Sandbox.ModAPI;
using VRage.Game.Entity.UseObject;
using VRage.Network;
using VRage.Utils;
using Medieval.Entities.UseObject;
using VRage.Scene;
using System.Collections.Generic;
using Medieval.Entities.Components;
using System;
using VRage.Components.Physics;
using VRage.Engine;
using VRage.Library.Collections;
using VRage.Serialization;

namespace Pax.Misc
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_GridSync : MyObjectBuilder_EntityComponent
    {
        public bool Active = false;
    }
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_PAX_GridSyncDefinition : MyObjectBuilder_EntityComponentDefinition
    {
    }
    [MyDefinitionType(typeof(MyObjectBuilder_PAX_GridSyncDefinition))]
    public class MyPAX_GridSyncDefinition : MyEntityComponentDefinition
    {
        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_PAX_GridSyncDefinition)builder;
        }
    }

    [ReplicatedComponent]
    [MyComponent(typeof(MyObjectBuilder_PAX_GridSync))]
    public class MyPAX_GridSync : MyEntityComponent, IMyEventOwner, IMyEventProxy, IMyGenericUseObjectInterface, IMyComponentEventProvider
    {
        private static List<long> ActiveSyncers = new List<long>();
        private static int DataSend = 0;
        private static DateTime LastTimeSend = DateTime.Now;
        private static long ActiveDrawId = 0;

        private MyPAX_GridSyncDefinition m_definition = null;
        private MyComponentEventBus m_eventBus = null;

        private bool SyncActive = false;
        private int SyncTimeout = 0;
        private int SyncTimer = 0;
        private int SleepTimer = 0;

        private bool EventActiveSend = false;
        private bool EventInactiveSend = false;

        private Vector3D LastPos = Vector3D.Zero;

        private bool Active = false;

        private string Message = null;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            m_definition = definition as MyPAX_GridSyncDefinition;
        }

        public override bool IsSerialized
        {
            get { return true; }
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);

            MyObjectBuilder_PAX_GridSync myObjectBuilder = builder as MyObjectBuilder_PAX_GridSync;

            Active = myObjectBuilder.Active;
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var builder = base.Serialize(copy);

            MyObjectBuilder_PAX_GridSync myObjectBuilder = builder as MyObjectBuilder_PAX_GridSync;

            myObjectBuilder.Active = Active;

            return builder;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (Entity == null) return;

            if (Entity.Components.Contains<MyInfinitePersistenceViewComponent>())
            {
                MyInfinitePersistenceViewComponent comp = Entity.Components.Get<MyInfinitePersistenceViewComponent>();
                Entity.Components.Remove(comp);
            }

            m_eventBus = null;
            Container.TryGet<MyComponentEventBus>(out m_eventBus);

            EventActiveSend = false;
            EventInactiveSend = true;

            if (m_eventBus != null) m_eventBus.Invoke("SyncDeactivate");

            if (Active) AddScheduledCallback(new MyTimedUpdate(this.DelayedAdded), (long)1000);
        }

        [Update(false)]
        private void DelayedAdded(long time)
        {
            if (!SyncActive && Active && Entity != null)
            {
                Use("detector_inventory", UseActionEnum.None, null);
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            SyncActive = false;
            RemoveFixedUpdate(Syncing);
            RemoveFixedUpdate(DrawingMessage);
        }

        public struct EntitySyncData
        {
            public const int Size = 8 + /* packed quaternion */ 7 + 3 * 3 * 4;
            public long EntityId;

            [Serialize(MyPrimitiveFlags.Normalized)]
            public Quaternion Rotation;

            public Vector3 RelativePos;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
        }

        [FixedUpdate(false)]
        private void Syncing()
        {
            SyncTimer++;
            if (SyncTimer < 4) return;
            SyncTimer = 0;

            //server sync tick
            SyncTimeout -= 4;
            var parentPhysics = Entity?.Parent?.Physics;
            if (MyAPIGateway.Multiplayer != null && parentPhysics != null && SyncTimeout >= 0)
            {
                if (SleepTimer > 150)
                {
                    SleepTimer++;
                    if (SleepTimer > 210) SleepTimer = 149;
                    return;
                }

                SleepTimer++;

                if ((Entity.WorldMatrix.Translation - LastPos).LengthSquared() > 0.24f)
                {
                    SyncTimeout = 54000;
                    LastPos = Entity.WorldMatrix.Translation;
                }

                using (PoolManager.Get(out List<MyPhysicsComponentBase> movingEntities))
                {
                    var group = Entity.Parent.Physics.GetGroup();
                    if (group?.ParentEntities != null)
                    {
                        foreach (var parent in group.ParentEntities)
                            if (!parent.IsStatic)
                                movingEntities.Add(parent);
                    } else if (!parentPhysics.IsStatic)
                        movingEntities.Add(parentPhysics);

                    var dataOffset = Entity.WorldMatrix.Translation;
                    const int sendBatchSize = (1000 / EntitySyncData.Size) + 1;
                    var dataToSend = new List<EntitySyncData>(sendBatchSize);

                    foreach (var physics in movingEntities)
                    {
                        var entity = physics.Entity;
                        if (entity == null) continue;
                        var matrix = entity.WorldMatrix;
                        var data = new EntitySyncData
                        {
                            EntityId = entity.EntityId,
                            RelativePos = (Vector3)(matrix.Translation - dataOffset),
                            LinearVelocity = physics.LinearVelocity,
                            AngularVelocity = physics.AngularVelocity,
                        };
#if VRAGE_VERSION_0_7_3
                        Quaternion.CreateFromRotationMatrix(in matrix, out data.Rotation);
#else
                        data.Rotation = Quaternion.CreateFromRotationMatrix(matrix);
#endif
                        dataToSend.Add(data);
                        if (dataToSend.Count >= sendBatchSize)
                        {
                            MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.SyncEvent, dataOffset, dataToSend);
                            dataToSend = new List<EntitySyncData>(sendBatchSize);
                        }
                        if (physics.IsActive)
                            SleepTimer = 0;
                    }
                    
                    if (dataToSend.Count > 0)
                    {
                        MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.SyncEvent, dataOffset, dataToSend);
                    }

                    if (movingEntities.Count > 0)
                    {
                        if (!EventActiveSend)
                        {
                            EventActiveSend = true;
                            EventInactiveSend = false;
                            m_eventBus?.Invoke("SyncActivate");
                        }
                        return;
                    }
                }
            }

            if (!EventInactiveSend)
            {
                EventActiveSend = false;
                EventInactiveSend = true;
                m_eventBus?.Invoke("SyncDeactivate");
            }

            if (MyAPIGateway.Multiplayer != null) MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.DeactivateEvent);

            RemoveFixedUpdate(Syncing);
            SyncActive = false;
            Active = false;

            if (Entity.Components.Contains<MyInfinitePersistenceViewComponent>())
            {
                MyInfinitePersistenceViewComponent comp = Entity.Components.Get<MyInfinitePersistenceViewComponent>();
                Entity.Components.Remove(comp);
            }
        }

        [Event, Reliable, BroadcastExcept]
        public void SyncEvent(Vector3D relativeTo, List<EntitySyncData> syncData)
        {
            DataSend += (8 * 3) + syncData.Count * EntitySyncData.Size;

            //client sync event
            SyncActive = true;
            if (Entity?.Parent?.Physics != null && syncData.Count > 0)
            {
                if (!ActiveSyncers.Contains(Entity.EntityId)) ActiveSyncers.Add(Entity.EntityId);

                using (PoolManager.Get(out Dictionary<long, MyEntity> groupEntities))
                {
                    groupEntities[Entity.Parent.EntityId] = Entity.Parent;
                    var group = Entity.Parent.Physics.GetGroup();
                    if (group?.Entities != null)
                        foreach (var comp in group.ParentEntities)
                        {
                            var ent = comp.Entity;
                            if (ent != null)
                                groupEntities[ent.EntityId] = ent;
                        }

                    foreach (var data in syncData)
                    {
                        if (!groupEntities.TryGetValue(data.EntityId, out var entity))
                            continue;

                        entity.WorldMatrix = MatrixD.CreateFromTransformScale(data.Rotation, relativeTo + data.RelativePos, Vector3D.One);
                        entity.Physics.LinearVelocity = data.LinearVelocity;
                        entity.Physics.AngularVelocity = data.AngularVelocity;
                    }
                }
            }

            if (!EventActiveSend)
            {
                EventActiveSend = true;
                EventInactiveSend = false;
                if (m_eventBus != null) m_eventBus.Invoke("SyncActivate");
            }

            DisplayGlobalSyncerStatus();
        }

        private void DisplayGlobalSyncerStatus()
        {
            float seconds = (float)(DateTime.Now - LastTimeSend).TotalSeconds;
            if (seconds > 1f)
            {
                if (seconds < 3f)
                {
                    //display data on screen
                    Message = ActiveSyncers.Count.ToString() + " Syncers active\n" + (DataSend / 1000f).ToString("0") + " kB/sec Data transfer";
                    if (ActiveDrawId != Entity.EntityId)
                    {
                        AddFixedUpdate(DrawingMessage);
                        ActiveDrawId = Entity.EntityId;
                    }
                }

                //clear
                DataSend = 0;
                ActiveSyncers.Clear();
                LastTimeSend = DateTime.Now;
            }
        }

        [FixedUpdate(false)]
        private void DrawingMessage()
        {
            if (Entity != null)
            {
                if (ActiveDrawId == Entity.EntityId && !string.IsNullOrEmpty(Message))
                {
                    if ((float)(DateTime.Now - LastTimeSend).TotalSeconds > 5f)
                    {
                        //syncing has stopped
                        Message = null;
                        ActiveDrawId = 0;
                        RemoveFixedUpdate(DrawingMessage);
                    }
                    var size = Sandbox.Graphics.MyGuiManager.GetFullscreenRectangle().Size;
                    VRageRender.MyRenderProxy.DebugDrawText2D(new Vector2(size.X - 5, 5), Message, Color.LightGray, size.Y * 0.0004f, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP, false, VRageRender.Messages.SpriteBatchMode.Default);
                }
                else
                {
                    Message = null;
                    RemoveFixedUpdate(DrawingMessage);
                }
            }
        }

        [Event, Reliable, BroadcastExcept]
        public void DeactivateEvent()
        {
            if (!EventInactiveSend)
            {
                EventActiveSend = false;
                EventInactiveSend = true;
                if (m_eventBus != null) m_eventBus.Invoke("SyncDeactivate");
            }
        }

        public void TurnOnClientSide()
        {
            if (!SyncActive)
            {
                if (MyAPIGateway.Multiplayer != null) MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.TurnOn);
            }
        }

        [Event, Reliable, Server]
        public void TurnOn()
        {
            if (!SyncActive)
            {
                SyncActive = true;
                SyncTimeout = 54000; //15 minutes until timeout
                LastPos = Entity.WorldMatrix.Translation;
                AddFixedUpdate(Syncing);
                Active = true;

#if PNL || VRAGE_VERSION_0_7_3
                if (!Entity.Components.Contains<MyInfinitePersistenceViewComponent>())
                {
                    MyInfinitePersistenceViewComponent comp = new MyInfinitePersistenceViewComponent();
                    Entity.Components.Add(comp);
                    comp.RemoveOnSleep = false;
                }
#endif
            }
        }

        public UseActionEnum SupportedActions => UseActionEnum.Manipulate;
        public UseActionEnum PrimaryAction => UseActionEnum.Manipulate;
        public UseActionEnum SecondaryAction => UseActionEnum.UseFinished;
        public bool ContinuousUsage => false;

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            MyActionDescription desc = new MyActionDescription();
            if (dummyName == "detector_inventory")
            {
                if (SyncActive)
                {
                    desc.Text = MyStringId.GetOrCompute("Stop syncing");
                }
                else
                {
                    desc.Text = MyStringId.GetOrCompute("Start syncing");
                }

                if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer)
                {
                    SyncTimer++;
                    if (SyncTimer >= 4)
                    {
                        SyncTimer = 0;
                        SyncActive = false;
                    }
                }
            }

            return desc;
        }

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            //server only
            if (!(MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer))
            {
                if (actionEnum != UseActionEnum.UseFinished)
                {
                    if (dummyName == "detector_inventory")
                    {
                        if (SyncActive)
                        {
                            SyncActive = false;
                            Active = false;
                            RemoveFixedUpdate(Syncing);

                            if (Entity.Components.Contains<MyInfinitePersistenceViewComponent>())
                            {
                                MyInfinitePersistenceViewComponent comp = Entity.Components.Get<MyInfinitePersistenceViewComponent>();
                                Entity.Components.Remove(comp);
                            }

                            if (!EventInactiveSend)
                            {
                                EventActiveSend = false;
                                EventInactiveSend = true;
                                if (m_eventBus != null) m_eventBus.Invoke("SyncDeactivate");
                            }

                            if (MyAPIGateway.Multiplayer != null) MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.DeactivateEvent);
                        }
                        else
                        {
                            SyncActive = true;
                            SyncTimeout = 54000; //15 minutes until timeout
                            LastPos = Entity.WorldMatrix.Translation;
                            AddFixedUpdate(Syncing);
                            Active = true;

#if PNL || VRAGE_VERSION_0_7_3
                            if (!Entity.Components.Contains<MyInfinitePersistenceViewComponent>())
                            {
                                MyInfinitePersistenceViewComponent comp = new MyInfinitePersistenceViewComponent();
                                Entity.Components.Add(comp);
                                comp.RemoveOnSleep = false;
                            }
#endif
                        }
                    }
                }
            }
        }

        public bool HasEvent(string eventName)
        {
            switch (eventName)
            {
                default: return false;
                case "SyncActivate":
                case "SyncDeactivate":
                    return true;
            }
        }
    }
}

//end of the line