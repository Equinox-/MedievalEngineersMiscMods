using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Struct;
using Sandbox.ModAPI;
using VRage.Network;
using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        public readonly struct FeatureHandle
        {
            public readonly EquiDecorativeMeshComponent Owner;
            public readonly FeatureKey Key;
            public readonly float Distance;
            private readonly PagedFreeList<RenderData>.Handle _handle;

            public bool IsValid => _handle.IsValid;

            internal FeatureHandle(
                EquiDecorativeMeshComponent owner,
                in FeatureKey key,
                float distance = float.PositiveInfinity)
            {
                Owner = owner;
                Distance = distance;
                Key = key;
                owner._features.TryGetValue(Key, out _handle);
            }

            private ref RenderData Data => ref _handle.Value;
            private ref FeatureArgs Args => ref Data.Args;

            #region Public API

            public EquiDecorativeToolBaseDefinition Definition => Data.Def;

            public EquiDecorativeToolBaseDefinition.MaterialDef Material =>
                Definition.RawMaterials.TryGetValue(Data.Args.MaterialId, out var mtl) ? mtl :
                Definition.RawSortedMaterials.Count > 0 ? Definition.RawSortedMaterials[0] : null;

            public PackedHsvShift Color => Data.Args.Shared.Color;

            public bool IsDecal => Key.Type == FeatureType.Decal;
            public BlockAndAnchor DecalPosition => Key.A;
            public float DecalHeight => Args.DecalHeight;
            public bool DecalMirrored => (Args.DecalFlags & DecalFlags.Mirrored) != 0;

            public bool IsLine => Key.Type == FeatureType.Line;
            public BlockAndAnchor LinePt0 => Key.A;
            public BlockAndAnchor LinePt1 => Key.B;
            public float LineCatenaryFactory => Args.CatenaryFactor;
            public float LineWidthA => Args.WidthA >= 0 ? Args.WidthA : ((EquiDecorativeLineToolDefinition)Definition).DefaultWidth;
            public float LineWidthB => Args.WidthB >= 0 ? Args.WidthB : LineWidthA;

            public bool IsSurface => Key.Type == FeatureType.Surface;
            public BlockAndAnchor SurfacePt0 => Key.A;
            public BlockAndAnchor SurfacePt1 => Key.B;
            public BlockAndAnchor SurfacePt2 => Key.C;
            public BlockAndAnchor? SurfacePt3 => Key.D.Equals(BlockAndAnchor.Null) ? (BlockAndAnchor?)null : Key.D;
            public UvProjectionMode SurfaceUvProjection => Args.UvProjection;
            public UvBiasMode SurfaceUvBias => Args.UvBias;
            public float SurfaceUvScale => Args.UvScale;

            public bool IsModel => Key.Type == FeatureType.Model;
            public BlockAndAnchor ModelPosition => Key.A;
            public float ModelScale => Args.ModelScale;

            #endregion
        }

        /// <summary>
        /// Gets a handle for the feature with the given key.
        /// </summary>
        /// <returns>true if the feature was found, otherwise false</returns>
        public bool TryGetFeature(in FeatureKey key, out FeatureHandle handle)
        {
            handle = new FeatureHandle(this, in key);
            return handle.IsValid;
        }

        /// <summary>
        /// Called by a client to request the removal of a feature.
        /// Standard NetworkTrust applies.
        /// </summary>
        public void RequestRemoveFeature(in FeatureKey key)
        {
            var mp = MyMultiplayerModApi.Static;
            if (mp.IsServer)
            {
                RemoveFeature(in key);
                return;
            }

            mp.RaiseEvent(this, ctx => ctx.RequestRemoveFeature_Sync, (RpcFeatureKey)key);
        }

        [Event, Reliable, Server]
        private void RequestRemoveFeature_Sync(RpcFeatureKey key)
        {
            var fk = (FeatureKey)key;
            var box = BoundingBoxD.CreateInvalid();
            AccumulatePt(in fk.A);
            AccumulatePt(in fk.B);
            AccumulatePt(in fk.C);
            AccumulatePt(in fk.D);
            if (!box.IsValid || !NetworkTrust.IsTrusted(this, box))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            RemoveFeature(in fk);
            return;

            void AccumulatePt(in BlockAndAnchor anchor)
            {
                if (!anchor.Equals(BlockAndAnchor.Null) && anchor.TryGetGridLocalAnchor(_gridData, out var pos))
                {
                    box.Include(Vector3D.Transform(pos, Entity.PositionComp.WorldMatrix));
                }
            }
        }
    }
}