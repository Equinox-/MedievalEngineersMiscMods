using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Sandbox.Definitions;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.Camera;
using VRage.Components.Entity.CubeGrid;
using VRage.Core;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Import;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeDecalToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeDecalTool : EquiDecorativeToolBase
    {
        internal const float MinDecalHeight = .125f / 4;
        internal const float MaxDecalHeight = 5f;

        private EquiDecorativeDecalToolDefinition _definition;

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiDecorativeDecalToolDefinition)definition;
        }

        private EquiDecorativeDecalToolDefinition.DecalDef DecalDef =>
            _definition.SortedDecals[DecorativeToolSettings.DecalIndex % _definition.SortedDecals.Count];

        protected override bool ValidateTarget() => HasPermission(MyPermissionsConstants.Build);

        protected override bool Start(MyHandItemActionEnum action) => HasPermission(MyPermissionsConstants.Build);

        protected override int RequiredPoints => 1;

        private Vector3 ComputeDecalUp(MyGridDataComponent grid, Vector3 normal)
        {
            var gridPos = grid.Container.Get<MyPositionComponentBase>();
            var gridInv = gridPos.WorldMatrixNormalizedInv;
            var camWorld = MyCameraComponent.ActiveCamera.GetWorldMatrix();

            var rot = Quaternion.CreateFromAxisAngle(normal, MathHelper.ToRadians(DecorativeToolSettings.DecalRotationDeg));

            Vector3 RotateAndAlign(Vector3 localUp)
            {
                var rotated = Vector3.Transform(localUp, rot);
                var left = Vector3.Cross(normal, rotated);
                return Vector3.Cross(left, normal);
            }

            bool TryHint(Vector3D hint, out Vector3 up)
            {
                var local = (Vector3)Vector3D.TransformNormal(hint, ref gridInv);
                local -= local.Dot(normal) * normal;
                local = Vector3.DominantAxisProjection(local);
                up = default;
                if (local.Normalize() < 1e-5f)
                    return false;
                up = RotateAndAlign(local);
                return up.Normalize() > 1e-5f;
            }

            if (TryHint(camWorld.Up, out var result) || TryHint(camWorld.Left, out result))
                return result;
            result = RotateAndAlign(new Vector3(normal.Y + normal.Z, -normal.X + normal.Z, -normal.Y - normal.X));
            result.Normalize();
            return result;
        }

        protected override void HitWithEnoughPoints(ListReader<DecorAnchor> points)
        {
            if (points.Count < 1) return;
            var remove = ActiveAction == MyHandItemActionEnum.Secondary;
            var decalDef = DecalDef;
            if (!remove)
            {
                var area = DecorativeToolSettings.DecalHeight * DecorativeToolSettings.DecalHeight * decalDef.AspectRatio;
                var durabilityCost = (int)Math.Ceiling(decalDef.DurabilityBase + decalDef.DurabilityPerSquareMeter * area);
                if (!TryRemoveDurability(durabilityCost))
                    return;
            }

            var normal = points[0].GridLocalNormal;
            if (MyMultiplayerModApi.Static.IsServer)
            {
                var gridDecor = points[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                if (remove)
                    gridDecor.RemoveDecal(points[0].Anchor);
                else
                    gridDecor.AddDecal(decalDef, new EquiDecorativeMeshComponent.DecalArgs<EquiDecorativeMeshComponent.BlockAndAnchor>()
                    {
                        Position = points[0].Anchor,
                        Normal = normal,
                        Up = ComputeDecalUp(points[0].Grid, normal),
                        Height = DecorativeToolSettings.DecalHeight,
                        Color = PackedHsvShift
                    });
                return;
            }

            if (remove)
            {
                MyMultiplayer.RaiseStaticEvent(x => PerformOp, points[0].Grid.Entity.Id, points[0].RpcAnchor, default(DecalRpcArgs), true);
                return;
            }

            var up = ComputeDecalUp(points[0].Grid, normal);
            MyMultiplayer.RaiseStaticEvent(x => PerformOp,
                points[0].Grid.Entity.Id, points[0].RpcAnchor, new DecalRpcArgs
                {
                    DecalId = decalDef.Id,
                    PackedNormal = VF_Packer.PackNormal(normal),
                    PackedUp = VF_Packer.PackNormal(up),
                    Height = DecorativeToolSettings.DecalHeight,
                    Color = PackedHsvShift,
                }, false);
        }

        [RpcSerializable]
        private struct DecalRpcArgs
        {
            public MyStringHash DecalId;
            public uint PackedNormal;
            public uint PackedUp;
            public float Height;
            public PackedHsvShift Color;
        }

        [Event, Reliable, Server]
        private static void PerformOp(
            EntityId grid,
            EquiDecorativeMeshComponent.RpcBlockAndAnchor rpcPt0,
            DecalRpcArgs decal,
            bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeDecalTool behavior)
                || !behavior._definition.Decals.TryGetValue(decal.DecalId, out var decalDef)
                || !behavior.Scene.TryGetEntity(grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            EquiDecorativeMeshComponent.BlockAndAnchor pt0 = rpcPt0;
            if (!gridEntity.Components.TryGet(out MyGridDataComponent gridData)
                || !pt0.TryGetGridLocalAnchor(gridData, out var local0))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!NetworkTrust.IsTrusted(gridData, Vector3D.Transform(local0, gridEntity.PositionComp.WorldMatrix)))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!remove)
            {
                var area = decal.Height * decal.Height * decalDef.AspectRatio;
                var durabilityCost = (int)Math.Ceiling(decalDef.DurabilityBase + decalDef.DurabilityPerSquareMeter * area);
                if (!behavior.TryRemoveDurability(durabilityCost))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            var gridDecor = gridEntity.Components.GetOrAdd<EquiDecorativeMeshComponent>();
            if (remove)
                gridDecor.RemoveDecal(pt0);
            else
                gridDecor.AddDecal(
                    decalDef,
                    new EquiDecorativeMeshComponent.DecalArgs<EquiDecorativeMeshComponent.BlockAndAnchor>
                    {
                        Position = pt0,
                        Normal = VF_Packer.UnpackNormal(decal.PackedNormal),
                        Up = VF_Packer.UnpackNormal(decal.PackedUp),
                        Height = decal.Height,
                        Color = decal.Color,
                    });
        }

        private string _currentRenderObjectModel = null;
        private uint _currentRenderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

        private void DestroyRenderObject()
        {
            if (_currentRenderObject == MyRenderProxy.RENDER_ID_UNASSIGNED) return;
            MyRenderProxy.RemoveRenderObject(_currentRenderObject);
            _currentRenderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;
        }

        public override void Deactivate()
        {
            base.Deactivate();
            DestroyRenderObject();
        }

        protected override void RenderHelper()
        {
            SetTarget();
            var def = DecalDef;
            MatrixD worldTransform;
            Vector3 localPos;
            Vector3 localNormal;
            Vector3 localUp;
            if (TryGetAnchor(out var nextAnchor))
            {
                worldTransform = nextAnchor.Grid.Entity.WorldMatrix;
                localPos = nextAnchor.GridLocalPosition;
                localNormal = nextAnchor.GridLocalNormal;
                localUp = ComputeDecalUp(nextAnchor.Grid, localNormal);
                if (nextAnchor.Source == AnchorSource.Existing)
                    nextAnchor.Draw();
            }
            else
            {
                var caster = Holder.Get<MyCharacterDetectorComponent>();
                worldTransform = Holder.WorldMatrix;
                MatrixD.Invert(ref worldTransform, out var worldInv);
                if (caster != null)
                {
                    localPos = (Vector3)Vector3D.Transform(caster.StartPosition + caster.Direction * 2, ref worldInv);
                    localNormal = (Vector3)Vector3D.TransformNormal(-caster.Direction, ref worldInv);
                    localNormal.Normalize();
                }
                else
                {
                    localPos = Vector3.Zero;
                    localNormal = Vector3.Backward;
                }

                var rot = Quaternion.CreateFromAxisAngle(localNormal, MathHelper.ToRadians(DecorativeToolSettings.DecalRotationDeg));
                var rotated = Vector3.Transform(Vector3.Up, rot);
                var left = Vector3.Cross(localNormal, rotated);
                localUp = Vector3.Cross(left, localNormal);
                localUp.Normalize();
            }

            var prepared = EquiDecorativeMeshComponent.CreateDecalData(
                def,
                new EquiDecorativeMeshComponent.DecalArgs<Vector3>
                {
                    Position = localPos,
                    Normal = localNormal,
                    Up = localUp,
                    Height = DecorativeToolSettings.DecalHeight,
                    Color = PackedHsvShift,
                });

            var renderMatrix = MatrixD.Identity;
            renderMatrix.Translation = prepared.Position;
            renderMatrix.Backward = VF_Packer.UnpackNormal(prepared.Normal);
            renderMatrix.Up = prepared.Up.ToVector3();
            renderMatrix.Left = prepared.Left.ToVector3();
            renderMatrix *= worldTransform;


            var previewModel = EquiDecalPreviewModels.GetPreviewModel(def.Material, def.TopLeftUv, def.BottomRightUv);
            if (_currentRenderObject == MyRenderProxy.RENDER_ID_UNASSIGNED || _currentRenderObjectModel != previewModel)
            {
                if (_currentRenderObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                    MyRenderProxy.RemoveRenderObject(_currentRenderObject);
                _currentRenderObject = MyRenderProxy.CreateRenderEntity(
                    $"decal_preview_{Holder.EntityId}",
                    previewModel,
                    MatrixD.Identity, MyMeshDrawTechnique.MESH,
                    RenderFlags.Visible | RenderFlags.ForceOldPipeline,
                    CullingOptions.Default,
                    Color.White,
                    Vector3.One,
                    depthBias: 255);
                _currentRenderObjectModel = previewModel;
            }
            else
                MyRenderProxy.UpdateRenderObject(_currentRenderObject, renderMatrix);

            MyRenderProxy.UpdateRenderEntity(_currentRenderObject, null, PackedHsvShift);
        }

        protected override void RenderShape(MyGridDataComponent grid, ListReader<Vector3> positions)
        {
            // Not used...
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeDecalToolDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    [MyDependency(typeof(MyInventoryItemDefinition))]
    [MyDependency(typeof(MyItemTagDefinition))]
    public class EquiDecorativeDecalToolDefinition : EquiDecorativeToolBaseDefinition
    {
        public ListReader<DecalDef> SortedDecals { get; private set; }
        public DictionaryReader<MyStringHash, DecalDef> Decals { get; private set; }

        public class DecalDef : IMyObject, IDecorativeMaterial
        {
            public readonly EquiDecorativeDecalToolDefinition Owner;
            public readonly MyStringHash Id;
            public string Name { get; }
            public readonly HalfVector2 TopLeftUv;
            public readonly HalfVector2 BottomRightUv;
            public readonly float AspectRatio;
            public readonly float DurabilityBase;
            public readonly float DurabilityPerSquareMeter;
            public readonly string Material;

            // Used for icon rendering.
            public string[] UiIcons { get; }

            internal DecalDef(EquiDecorativeDecalToolDefinition owner, MyStringHash id, MyObjectBuilder_EquiDecorativeDecalToolDefinition.DecalDef ob)
            {
                Owner = owner;
                Id = id;
                Name = ob.Name;
                if (string.IsNullOrEmpty(ob.Name))
                    Name = EquiDecorativeMaterialController.NameFromId(id);
                if (ob.UiIcons != null && ob.UiIcons.Length > 0)
                    UiIcons = ob.UiIcons;
                else if (ob.Material.Icons != null && ob.Material.Icons.Count > 0)
                    UiIcons = ob.Material.Icons.ToArray();
                else
                {
                    Log.Warning($"Decal {owner.Id}/{Name} has no UI icon.  Add <UiIcon> tag to the decal.");
                    UiIcons = null;
                }

                Material = ob.Material.Build().MaterialName;
                var topLeftUv = ob.TopLeftUv ?? Vector2.Zero;
                TopLeftUv = new HalfVector2(topLeftUv);
                var bottomRightUv = ob.BottomRightUv ?? Vector2.One;
                BottomRightUv = new HalfVector2(bottomRightUv);
                AspectRatio = Math.Abs(ob.AspectRatio ?? (bottomRightUv.X - topLeftUv.X) / (bottomRightUv.Y - topLeftUv.Y));
                DurabilityBase = ob.DurabilityBase ?? 1;
                DurabilityPerSquareMeter = ob.DurabilityPerSquareMeter ?? 0;
            }

            void IMyObject.Deserialize(MyObjectBuilder_Base builder) => throw new NotImplementedException();

            MyObjectBuilder_Base IMyObject.Serialize() => throw new NotImplementedException();

            IMyObjectIdentifier IMyObject.Id => throw new NotImplementedException();
            MyDefinitionId IMyObject.DefinitionId => new MyDefinitionId(typeof(MyObjectBuilder_EquiDecorativeDecalToolDefinition), Id);
            bool IMyObject.NeedsSerialize => throw new NotImplementedException();
        }

        private static readonly List<MaterialSpec.Parameter> ItemDecalParameters = new List<MaterialSpec.Parameter>
        {
            new MaterialSpec.Parameter { Name = "Technique", Value = "DECAL" },
            new MaterialSpec.Parameter { Name = "NormalGlossTexture", Value = "ReleaseMissingNormalGloss" }
        };

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeDecalToolDefinition)builder;
            if (ob.Decals == null) return;
            var dict = new Dictionary<MyStringHash, DecalDef>();
            // Item decals
            foreach (var itemDecal in ob.ItemDecals)
            {
                void Create(MyInventoryItemDefinition item)
                {
                    if (item?.Icons == null || item.Icons.Length == 0) return;
                    var decal = new MyObjectBuilder_EquiDecorativeDecalToolDefinition.DecalDef
                    {
                        Id = item.Id.SubtypeName,
                        Name = item.DisplayNameText,
                        Material = new MaterialSpec
                        {
                            Parameters = itemDecal.Material?.Parameters ?? ItemDecalParameters,
                            IconResolution = itemDecal.Material?.IconResolution,
                            Icons = new List<string>(item.Icons)
                        },
                        DurabilityBase = itemDecal.DurabilityBase,
                        DurabilityPerSquareMeter = itemDecal.DurabilityPerSquareMeter,
                    };
                    if (itemDecal.Material?.Icons != null)
                        decal.Material.Icons.AddRange(itemDecal.Material.Icons);
                    var id = MyStringHash.GetOrCompute(decal.Id);
                    dict[id] = new DecalDef(this, id, decal);
                }

                void CreateMany(IEnumerable<MyInventoryItemDefinition> items, bool includeHidden = false)
                {
                    foreach (var item in items)
                        if ((includeHidden || item.Public) && item.Enabled)
                            Create(item);
                }

                if (itemDecal.All)
                    CreateMany(MyDefinitionManager.GetOfType<MyInventoryItemDefinition>());
                if (itemDecal.AllWithoutSchematics)
                    CreateMany(MyDefinitionManager.GetOfType<MyInventoryItemDefinition>().Where(x => !(x is MySchematicItemDefinition)));

                if (itemDecal.ItemSubtypes != null)
                    foreach (var subtype in itemDecal.ItemSubtypes)
                        Create(MyDefinitionManager.Get<MyInventoryItemDefinition>(subtype));
                if (itemDecal.Tags != null)
                    foreach (var tag in itemDecal.Tags)
                        if (MyDefinitionManager.TryGet<MyItemTagDefinition>(MyStringHash.GetOrCompute(tag), out var tagDef))
                            CreateMany(tagDef.Items);
                if (itemDecal.TagsNonPublic != null)
                    foreach (var tag in itemDecal.TagsNonPublic)
                        if (MyDefinitionManager.TryGet<MyItemTagDefinition>(MyStringHash.GetOrCompute(tag), out var tagDef))
                            CreateMany(tagDef.Items, true);
            }

            // Custom decals
            foreach (var decal in ob.Decals)
            {
                if (string.IsNullOrEmpty(decal.Id) || decal.Material == null)
                    continue;
                var id = MyStringHash.GetOrCompute(decal.Id);
                dict[id] = new DecalDef(this, id, decal);
            }

            Decals = dict;
            SortedDecals = dict.Values.OrderBy(x => x.Name).ToList();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeDecalToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        public class DecalDef
        {
            [XmlAttribute("Id")]
            public string Id;

            [XmlAttribute("Name")]
            public string Name;

            [XmlElement]
            public MaterialSpec Material;

            [XmlElement("UiIcon")]
            public string[] UiIcons;

            [XmlElement]
            public SerializableVector2? TopLeftUv;

            [XmlElement]
            public SerializableVector2? BottomRightUv;

            [XmlElement]
            public float? AspectRatio;

            [XmlElement]
            public float? DurabilityBase;

            [XmlElement]
            public float? DurabilityPerSquareMeter;
        }

        [XmlElement("Decal")]
        public DecalDef[] Decals;

        [XmlElement("ItemDecals")]
        public ItemDecalsDef[] ItemDecals;

        public class ItemDecalsDef
        {
            [XmlAttribute("All")]
            public bool All;

            [XmlAttribute("AllWithoutSchematics")]
            public bool AllWithoutSchematics;

            [XmlElement("Item")]
            public List<string> ItemSubtypes;

            [XmlElement("Tag")]
            public List<string> Tags;

            [XmlElement("TagNonPublic")]
            public List<string> TagsNonPublic;

            [XmlElement]
            public MaterialSpec Material;

            [XmlElement]
            public float? DurabilityBase;

            [XmlElement]
            public float? DurabilityPerSquareMeter;
        }
    }
}