using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.Camera;
using VRage.Components.Entity.CubeGrid;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Import;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Serialization;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeDecalToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeDecalTool : EquiDecorativeToolBase<EquiDecorativeDecalToolDefinition, EquiDecorativeDecalToolDefinition.DecalDef>
    {
        internal const float MinDecalHeight = .125f / 4;
        internal const float MaxDecalHeight = 5f;
        protected override int RequiredPoints => 1;

        private static Vector3 ComputeDecalUp(MyGridDataComponent grid, Vector3 normal)
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

        protected override void EyeDropperFeature(in EquiDecorativeMeshComponent.FeatureHandle feature)
        {
            base.EyeDropperFeature(in feature);
            if (!feature.IsDecal) return;
            DecorativeToolSettings.DecalHeight = feature.DecalHeight;
            DecorativeToolSettings.DecalMirrored = feature.DecalMirrored;
        }

        protected override void HitWithEnoughPoints(ListReader<BlockAnchorInteraction> points)
        {
            if (points.Count < 1) return;
            var remove = ActiveAction == MyHandItemActionEnum.Secondary;
            var decalDef = MaterialDef;
            if (!remove)
            {
                var area = DecorativeToolSettings.DecalHeight * DecorativeToolSettings.DecalHeight * decalDef.AspectRatio;
                var durabilityCost = (int)Math.Ceiling(decalDef.DurabilityBase + decalDef.DurabilityPerSquareMeter * area);
                if (!TryRemovePreReqs(durabilityCost, decalDef))
                    return;
            }

            var normal = points[0].GridLocalNormal;
            if (MyMultiplayerModApi.Static.IsServer)
            {
                var gridDecor = points[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                if (remove)
                    gridDecor.RemoveDecal(points[0].Anchor);
                else
                    gridDecor.AddDecal(decalDef, new EquiDecorativeMeshComponent.DecalArgs<BlockAndAnchor>()
                    {
                        Position = points[0].Anchor,
                        Normal = normal,
                        Up = ComputeDecalUp(points[0].Grid, normal),
                        Height = DecorativeToolSettings.DecalHeight,
                        Flags = DecorativeToolSettings.DecalFlags,
                        Shared =
                        {
                            Color = PackedHsvShift
                        }
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
                    Flags = DecorativeToolSettings.DecalFlags,
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

            [Serialize]
            private uint _flags;

            [NoSerialize]
            public EquiDecorativeMeshComponent.DecalFlags Flags
            {
                get => (EquiDecorativeMeshComponent.DecalFlags)_flags;
                set => _flags = (uint)value;
            }
        }

        [Event, Reliable, Server]
        private static void PerformOp(
            EntityId grid,
            RpcBlockAndAnchor rpcPt0,
            DecalRpcArgs decal,
            bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeDecalTool behavior)
                || !behavior.Def.Materials.TryGetValue(decal.DecalId, out var decalDef)
                || !behavior.Scene.TryGetEntity(grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            BlockAndAnchor pt0 = rpcPt0;
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
                if (!behavior.TryRemovePreReqs(durabilityCost, decalDef))
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
                    new EquiDecorativeMeshComponent.DecalArgs<BlockAndAnchor>
                    {
                        Position = pt0,
                        Normal = VF_Packer.UnpackNormal(decal.PackedNormal),
                        Up = VF_Packer.UnpackNormal(decal.PackedUp),
                        Height = decal.Height,
                        Flags = decal.Flags,
                        Shared =
                        {
                            Color = decal.Color
                        }
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

        protected override void RenderHelper(bool hasNextAnchor, in BlockAnchorInteraction nextAnchor)
        {
            SetTarget();
            var def = MaterialDef;
            MatrixD worldTransform;
            Vector3 localPos;
            Vector3 localNormal;
            Vector3 localUp;
            if (hasNextAnchor)
            {
                worldTransform = nextAnchor.Grid.Entity.WorldMatrix;
                localPos = nextAnchor.GridLocalPosition;
                localNormal = nextAnchor.GridLocalNormal;
                localUp = ComputeDecalUp(nextAnchor.Grid, localNormal);
                if (nextAnchor.Source == BlockAnchorInteraction.SourceType.Existing)
                    nextAnchor.Draw();
            }
            else
            {
                var detectionLine = DetectionLine;
                worldTransform = Holder.WorldMatrix;
                MatrixD.Invert(ref worldTransform, out var worldInv);
                localPos = (Vector3)Vector3D.Transform(detectionLine.To, ref worldInv);
                localNormal = (Vector3)Vector3D.TransformNormal(-detectionLine.Direction, ref worldInv);

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
                    Flags = DecorativeToolSettings.DecalFlags,
                    Shared =
                    {
                        Color = PackedHsvShift
                    }
                });

            var renderMatrix = MatrixD.Identity;
            renderMatrix.Translation = prepared.Position;
            renderMatrix.Backward = VF_Packer.UnpackNormal(prepared.Normal);
            renderMatrix.Up = prepared.Up.ToVector3();
            renderMatrix.Left = prepared.Left.ToVector3();
            renderMatrix *= worldTransform;


            var previewModel = EquiDecalPreviewModels.GetPreviewModel(def.Material, prepared.TopLeftUv, prepared.BottomRightUv);
            if (_currentRenderObject == MyRenderProxy.RENDER_ID_UNASSIGNED || _currentRenderObjectModel != previewModel)
            {
                if (_currentRenderObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                    MyRenderProxy.RemoveRenderObject(_currentRenderObject);
                _currentRenderObject = MyRenderProxy.CreateRenderEntity(
                    $"decal_preview_{Holder.EntityId}",
                    previewModel,
                    renderMatrix,
                    MyMeshDrawTechnique.MESH,
                    RenderFlags.Visible | RenderFlags.ForceOldPipeline,
                    CullingOptions.Default,
                    Color.White,
                    prepared.ColorMask,
                    depthBias: 255);
                _currentRenderObjectModel = previewModel;
            }
            else
                MyRenderProxy.UpdateRenderObject(_currentRenderObject, renderMatrix);

            MyRenderProxy.UpdateRenderEntity(_currentRenderObject, null, PackedHsvShift);
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeDecalToolDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiDecorativeDecalToolDefinition : EquiDecorativeToolBaseDefinition,
        IEquiDecorativeToolBaseDefinition<EquiDecorativeDecalToolDefinition.DecalDef>
    {
        private readonly MaterialHolder<DecalDef> _holder;

        public DictionaryReader<MyStringHash, DecalDef> Materials
        {
            get
            {
                LazyInitIfNeeded();
                return _holder.Materials;
            }
        }

        public ListReader<DecalDef> SortedMaterials
        {
            get
            {
                LazyInitIfNeeded();
                return _holder.SortedMaterials;
            }
        }

        public override DictionaryReader<MyStringHash, MaterialDef> RawMaterials
        {
            get
            {
                LazyInitIfNeeded();
                return _holder.RawMaterials;
            }
        }

        public override ListReader<MaterialDef> RawSortedMaterials
        {
            get
            {
                LazyInitIfNeeded();
                return _holder.SortedRawMaterials;
            }
        }

        public class DecalDef : MaterialDef
        {
            public readonly HalfVector2 TopLeftUv;
            public readonly HalfVector2 BottomRightUv;

            public readonly HalfVector2 TopLeftUvMirrored;
            public readonly HalfVector2 BottomRightUvMirrored;

            public readonly float AspectRatio;
            public readonly float DurabilityPerSquareMeter;
            public readonly string Material;

            public readonly bool UiIconUsesUv;

            internal DecalDef(EquiDecorativeDecalToolDefinition owner, MyObjectBuilder_EquiDecorativeDecalToolDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeDecalToolDefinition.DecalDef ob) : base(owner, ownerOb, ob,
                ob.Material?.Icons)
            {
                var mtl = ob.Material?.Build() ?? throw new ArgumentException($"No material specified for {ob.Name}");
                Material = mtl.MaterialName;
                UiIconUsesUv = ob.IconUsesUv ?? (!(ob.UiIcons?.Length > 0) && ob.Material.Icons?.Count > 0);
                var topLeftUv = ob.TopLeftUv ?? Vector2.Zero;
                var bottomRightUv = ob.BottomRightUv ?? Vector2.One;
                TopLeftUv = new HalfVector2(topLeftUv);
                BottomRightUv = new HalfVector2(bottomRightUv);

                TopLeftUvMirrored = new HalfVector2(bottomRightUv.X, topLeftUv.Y);
                BottomRightUvMirrored = new HalfVector2(topLeftUv.X, bottomRightUv.Y);

                AspectRatio = Math.Abs(ob.AspectRatio ?? (bottomRightUv.X - topLeftUv.X) / (bottomRightUv.Y - topLeftUv.Y));
                DurabilityPerSquareMeter = ob.DurabilityPerSquareMeter ?? 0;
            }
        }

        private static readonly List<MaterialSpec.Parameter> ItemDecalParameters = new List<MaterialSpec.Parameter>
        {
            new MaterialSpec.Parameter { Name = "Technique", Value = "DECAL" },
            new MaterialSpec.Parameter { Name = "NormalGlossTexture", Value = "ReleaseMissingNormalGloss" }
        };

        public EquiDecorativeDecalToolDefinition()
        {
            _holder = new MaterialHolder<DecalDef>(this);
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeDecalToolDefinition)builder;
            if (ob.ItemDecals?.Length > 0)
            {
                // Use lazy initialization since dependencies are needed.
                _lazyInit = ob;
            }
            else
            {
                // Eagerly initialize since there aren't any external references.
                LazyInit(ob);
            }
        }

        private volatile MyObjectBuilder_EquiDecorativeDecalToolDefinition _lazyInit;

        private void LazyInitIfNeeded()
        {
            if (_lazyInit == null) return;
            lock (this)
            {
                var ob = _lazyInit;
                if (ob == null) return;
                LazyInit(ob);
                _lazyInit = null;
            }
        }

        private void LazyInit(MyObjectBuilder_EquiDecorativeDecalToolDefinition ob)
        {
            // Item decals
            if (ob.ItemDecals != null)
                foreach (var itemDecal in ob.ItemDecals)
                {
                    // FIXME // BAD access to inventory items during hand item behavior init 
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
                    continue;

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
                        _holder.Add(new DecalDef(this, ob, decal));
                    }

                    void CreateMany(IEnumerable<MyInventoryItemDefinition> items, bool includeHidden = false)
                    {
                        foreach (var item in items)
                            if ((includeHidden || item.Public) && item.Enabled)
                                Create(item);
                    }
                }

            // Custom decals
            if (ob.Decals != null)
                foreach (var decal in ob.Decals)
                {
                    if (string.IsNullOrEmpty(decal.Id) || decal.Material == null)
                        continue;
                    _holder.Add(new DecalDef(this, ob, decal));
                }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeDecalToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        public class DecalDef : MaterialDef
        {
            /// <summary>
            /// Texture coordinates for the top left of the decal. Defaults to (0, 0).
            /// </summary>
            [XmlElement]
            public SerializableVector2? TopLeftUv;

            /// <summary>
            /// Texture coordinates for the bottom left of the decal. Defaults to (1, 1).
            /// </summary>
            [XmlElement]
            public SerializableVector2? BottomRightUv;

            /// <summary>
            /// Preferred aspect ratio of the decal. Defaults to 1.
            /// </summary>
            [XmlElement]
            public float? AspectRatio;

            /// <summary>
            /// PBR material to use for the decal.
            /// </summary>
            [XmlElement]
            public MaterialSpec Material;

            /// <summary>
            /// Durability cost per square meter of decal.
            /// </summary>
            [XmlElement]
            public float? DurabilityPerSquareMeter;

            [XmlIgnore]
            public bool? IconUsesUv;

            /// <summary>
            /// Should the UI Icons use the UVs specified above.
            /// </summary>
            [XmlAttribute(nameof(IconUsesUv))]
            public bool IconUsesUvXml
            {
                set => IconUsesUv = value;
                get => IconUsesUv ?? false;
            }
        }

        [XmlElement("Decal")]
        public DecalDef[] Decals;

        [XmlElement("ItemDecals")]
        public ItemDecalsDef[] ItemDecals;

        public class ItemDecalsDef
        {
            /// <summary>
            /// Include all items.
            /// </summary>
            [XmlAttribute("All")]
            public bool All;

            /// <summary>
            /// Include all items that aren't schematic items.
            /// </summary>
            [XmlAttribute("AllWithoutSchematics")]
            public bool AllWithoutSchematics;

            /// <summary>
            /// Include items with specific subtypes.
            /// </summary>
            [XmlElement("Item")]
            public List<string> ItemSubtypes;

            /// <summary>
            /// Include public items with any of the provided tags.
            /// </summary>
            [XmlElement("Tag")]
            public List<string> Tags;

            /// <summary>
            /// Include non-public items with any of the provided tags.
            /// </summary>
            [XmlElement("TagNonPublic")]
            public List<string> TagsNonPublic;

            /// <summary>
            /// Material to overlay icons on top of.
            /// </summary>
            [XmlElement]
            public MaterialSpec Material;

            /// <summary>
            /// Durability cost for each placement.
            /// </summary>
            [XmlElement]
            public float? DurabilityBase;

            /// <summary>
            /// Durability cost per square meter of decal.
            /// </summary>
            [XmlElement]
            public float? DurabilityPerSquareMeter;
        }
    }
}