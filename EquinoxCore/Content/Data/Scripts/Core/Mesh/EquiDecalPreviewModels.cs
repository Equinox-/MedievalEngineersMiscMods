using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.ModelGenerator;
using VRage.Import;
using VRage.Session;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Mesh
{
    public static class EquiDecalPreviewModels
    {
        private static readonly Dictionary<Key, string> PreviewModels = new Dictionary<Key, string>();

        private readonly struct Key : IEquatable<Key>
        {
            private readonly string _material;
            private readonly HalfVector2 _topLeftUv;
            private readonly HalfVector2 _bottomRightUv;

            public Key(string material, HalfVector2 topLeftUv, HalfVector2 bottomRightUv)
            {
                _material = material;
                _topLeftUv = topLeftUv;
                _bottomRightUv = bottomRightUv;
            }

            public bool Equals(Key other) => _material == other._material && _topLeftUv.Equals(other._topLeftUv) && _bottomRightUv.Equals(other._bottomRightUv);

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = (_material != null ? _material.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ _topLeftUv.GetHashCode();
                hashCode = (hashCode * 397) ^ _bottomRightUv.GetHashCode();
                return hashCode;
            }
        }

        public static string GetPreviewModel(string material, HalfVector2 topLeftUv, HalfVector2 bottomRightUv)
        {
            var key = new Key(material, topLeftUv, bottomRightUv);
            if (PreviewModels.TryGetValue(key, out var model))
                return model;
            if (MaterialTable.TryGetById(material, out var mtl))
                mtl.EnsurePrepared();
            var msg = MyRenderProxy.PrepareAddRuntimeModel();
            var data = new EquiMeshHelpers.DecalData
            {
                Material = material,
                TopLeftUv = topLeftUv,
                BottomRightUv = bottomRightUv,
                Position = Vector3.Zero,
                Normal = VF_Packer.PackNormal(Vector3.Backward),
                Up = new HalfVector3(Vector3.Up),
                Left = new HalfVector3(Vector3.Left)
            };
            model = $"decal_preview_{material.GetHashCode()}_{topLeftUv.PackedValue}_{bottomRightUv.PackedValue}";
            msg.Persistent = false;
            msg.ReplacedModel = null;
#if VRAGE_VERSION_0
            msg.Dynamic = false;
#endif
            EquiMeshHelpers.BuildDecal(in data, msg.ModelData);
            msg.ModelData.Sections.Add(new MyRuntimeSectionInfo
            {
                MaterialName = material,
                IndexStart = 0,
                TriCount = 2,
            });
            msg.ModelData.AABB = new BoundingBox(Vector3.MinusOne, Vector3.One);
            MyRenderProxy.AddRuntimeModel(model, msg);
            PreviewModels.Add(key, model);
            return model;
        }
    }
}