using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.Util.EqMath;
using Sandbox.Game.World;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRage.Logging;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;
using MySession = VRage.Session.MySession;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public sealed class MaterialDescriptor
    {
        public readonly MyMaterialDescriptor Descriptor;
        public string MaterialName => Descriptor.MaterialName;
        private Action _prepare;

        public MaterialDescriptor(MyMaterialDescriptor descriptor, Action prepare)
        {
            Descriptor = descriptor;
            _prepare = prepare;
        }

        public void EnsurePrepared()
        {
            if (_prepare == null)
                return;
            lock (this)
            {
                _prepare?.Invoke();
                _prepare = null;
            }
        }
    }

    public sealed class MaterialTable
    {
        private static readonly FastResourceLock MaterialsLock = new FastResourceLock();
        private static readonly Dictionary<string, MaterialDescriptor> Materials = new Dictionary<string, MaterialDescriptor>();

        public static MaterialDescriptor From(MaterialSpec spec)
        {
            var materialId = spec.Name ?? GenerateMaterialId(spec);
            using (MaterialsLock.AcquireExclusiveUsing())
            {
                if (Materials.TryGetValue(materialId, out var value))
                    return value;
                Materials.Add(materialId, value = ToMwmMaterial(materialId, spec));
                return value;
            }
        }

        private static string GenerateMaterialId(MaterialSpec spec)
        {
            var hasher = new Hashing.HashBuilder();
            var iconMode = spec.Icons != null && spec.Icons.Count > 0;
            using (PoolManager.Get<List<MaterialSpec.Parameter>>(out var sorted))
            {
                sorted.AddRange(spec.Parameters);
                sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                foreach (var param in sorted)
                {
                    hasher.Add(param.Name);
                    hasher.Add(param.Value);
                }

                if (iconMode)
                {
                    hasher.Add("@icons@");
                    foreach (var icon in spec.Icons)
                        hasher.Add(icon);
                }
            }

            return hasher.Build().ToCompactString();
        }

        public static bool TryGetById(string id, out MaterialDescriptor descriptor)
        {
            using (MaterialsLock.AcquireSharedUsing())
            {
                return Materials.TryGetValue(id, out descriptor);
            }
        }

        private static bool TryRemoveProp(Dictionary<string, string> props, string key, out string value)
        {
            if (!props.TryGetValue(key, out value))
                return false;
            props.Remove(key);
            return true;
        }

        private static MaterialDescriptor ToMwmMaterial(string id, MaterialSpec spec)
        {
            const string techniqueKey = "Technique";
            const string glassCwKey = "GlassMaterialCW";
            const string glassCcwKey = "GlassMaterialCCW";
            const string glassSmoothKey = "GlassSmooth";
            
            var desc = new MyMaterialDescriptor(id);
            var props = spec.Parameters.ToDictionary(x => x.Name, x => x.Value);
            if (TryRemoveProp(props, techniqueKey, out var techniqueStr))
                desc.Technique = string.IsNullOrEmpty(techniqueStr) ? MyMeshDrawTechnique.MESH.ToString() : techniqueStr;
            if (TryRemoveProp(props, glassCwKey, out var glassCw))
                desc.GlassCW = glassCw;
            if (TryRemoveProp(props, glassCcwKey, out var glassCcw))
                desc.GlassCCW = glassCcw;
            if (TryRemoveProp(props, glassSmoothKey, out var glassSmooth))
                desc.GlassSmoothNormals = Convert.ToBoolean(glassSmooth);

            foreach (var kv in props)
            {
                if (kv.Key.Contains("Texture"))
                    desc.Textures[kv.Key] = kv.Value;
                else
                    desc.UserData[kv.Key] = kv.Value;
            }

            var iconPrepare = GetIconPrepareActions(id, desc.Textures, spec.Icons, spec.IconResolution ?? 256);
            return new MaterialDescriptor(desc, () => {
                iconPrepare?.Invoke();
                MySession.Static.Components.Get<DerivedModelManager>()?.PrepareMaterial(desc);
            });
        }

        private static Action GetIconPrepareActions(string id, Dictionary<string, string> textures, List<string> icons, int res)
        {
            if (icons == null || icons.Count <= 0)
                return null;
            const string colorMetalTexture = "ColorMetalTexture";
            const string alphaMaskTexture = "AlphamaskTexture";

            string PrepareTexture(string key)
            {
                if (textures.ContainsKey(key)) return null;
                var name = $"{id}_icons_{key}";
                textures.Add(key, name);
                return name;
            }

            var cm = PrepareTexture(colorMetalTexture);
            var alpha = PrepareTexture(alphaMaskTexture);
            if (cm == null && alpha == null)
                return null;
            return () =>
            {
                var tempObject = MyRenderProxy.CreateRenderEntity($"temp_for_icons_{id}",
                    @"Models\Debug\Sphere.mwm",
                    MatrixD.Identity, MyMeshDrawTechnique.MESH,
                    0,
                    CullingOptions.Default,
                    Color.White,
                    Vector3.Zero);

                void GenerateTexture(string name, MyTextureType type)
                {
                    if (name == null)
                        return;
                    MyRenderProxy.CreateGeneratedTexture(name, res, res, type == MyTextureType.Alphamask 
                        ? MyGeneratedTextureType.Alphamask 
                        : MyGeneratedTextureType.RGBA);
                    foreach (var icon in icons)
                    {
                        var rectangle = new RectangleF(0, 0, res, res);
                        Rectangle? sourceRectangle = null;
                        MyRenderProxy.DrawSprite(icon, ref rectangle, true, ref sourceRectangle, Color.White, 0, Vector2.UnitX, 
                            ref Vector2.Zero,
                            SpriteEffects.None, 0f, true, name, type == MyTextureType.Alphamask 
                                ? SpriteBatchMode.SingleChannelMax
                                : SpriteBatchMode.Default);
                    }

                    MyRenderProxy.RenderOffscreenTextureToMaterial(tempObject, "material", name, null, type);
                }

                GenerateTexture(cm, MyTextureType.ColorMetal);
                GenerateTexture(alpha, MyTextureType.Alphamask);
                MyRenderProxy.RemoveRenderObject(tempObject);
            };
        }
    }
}