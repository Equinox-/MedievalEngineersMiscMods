using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiModifierChangeMaterialDefinition))]
    public class EquiModifierChangeMaterialDefinition : EquiModifierBaseDefinition
    {
        private readonly Dictionary<string, List<MaterialEdit>> _edits = new Dictionary<string, List<MaterialEdit>>();
        private readonly Dictionary<string, string> _swaps = new Dictionary<string, string>();

        // Change materials per model (including LODs)
        private readonly ConcurrentDictionary<string, ChangeMaterialsEdit> _memorizedMaterialEdits = new ConcurrentDictionary<string, ChangeMaterialsEdit>();
        private readonly ConcurrentDictionary<string, ChangeMaterialsEdit> _memorizedMaterialEditsRefEquals =
            new ConcurrentDictionary<string, ChangeMaterialsEdit>(new ReferenceEqualityComparer<string>());
        private int _gcMaterialEditsCounter;

        private readonly Func<string, ChangeMaterialsEdit> _materialEditsForInternedModel;
        private Hashing.Hash128 _runtimeHash;

        public EquiModifierChangeMaterialDefinition()
        {
            Func<string, ChangeMaterialsEdit> materialEditsForModel = modelName =>
            {
                var valid = new HashSet<string>();
                foreach (var mtl in MySession.Static.Components.Get<DerivedModelManager>()?.GetMaterialsForModel(modelName) ??
                                    InterningBag<MaterialInModel>.Empty)
                    if ((mtl.CanEditInternals && _edits.ContainsKey(mtl.Name)) || _swaps.ContainsKey(mtl.Name))
                        valid.Add(mtl.Name);
                return valid.Count > 0 ? new ChangeMaterialsEdit(this, valid) : null;
            };
            _materialEditsForInternedModel = modelName =>
            {
                Interlocked.Increment(ref _gcMaterialEditsCounter);
                return _memorizedMaterialEdits.GetOrAdd(modelName, materialEditsForModel);
            };
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var hasher = Hashing.Builder();
            hasher.Add((uint)(TypeId)Id.TypeId);
            hasher.Add((int)Id.SubtypeId);
            _runtimeHash = hasher.Build();

            var ob = (MyObjectBuilder_EquiModifierChangeMaterialDefinition)def;
            if (ob.Replacements == null) return;
            foreach (var mod in ob.Replacements)
            {
                if (mod.NewName != null)
                {
                    if (!string.IsNullOrEmpty(mod.Name))
                        _swaps[mod.Name] = mod.NewName;
                    if (mod.Names != null)
                        foreach (var name in mod.Names)
                            _swaps[name] = mod.NewName;
                }

                if (mod.Parameters != null)
                {
                    if (!string.IsNullOrEmpty(mod.Name))
                    {
                        if (!_edits.TryGetValue(mod.Name, out var list))
                            _edits[mod.Name] = list = new List<MaterialEdit>();
                        mod.GetChanges(list);
                    }

                    if (mod.Names != null)
                    {
                        foreach (var name in mod.Names)
                        {
                            if (!_edits.TryGetValue(name, out var list))
                                _edits[name] = list = new List<MaterialEdit>();
                            mod.GetChanges(list);
                        }
                    }
                }
            }
        }

        public override bool CanApply(in ModifierContext ctx)
        {
            if (!base.CanApply(in ctx))
                return false;
            var model = ctx.OriginalModel;
            if (model == null)
                return false;
            return GetMaterialEdits(model) != null;
        }

        private ChangeMaterialsEdit GetMaterialEdits(string model)
        {
            // If the interned table grows too much, clear it (but not the underlying cache).
            if (_gcMaterialEditsCounter > 1_000 && _memorizedMaterialEditsRefEquals.Count > 10 * _memorizedMaterialEdits.Count)
            {
                _memorizedMaterialEditsRefEquals.Clear();
                Interlocked.Exchange(ref _gcMaterialEditsCounter, 0);
            }
            return _memorizedMaterialEditsRefEquals.GetOrAdd(model, _materialEditsForInternedModel);
        }

        public override void Apply(in ModifierContext ctx, IModifierData data, ref ModifierOutput output)
        {
            var model = ctx.OriginalModel;
            if (model == null)
                return;
            var edits = GetMaterialEdits(model);
            if (edits == null)
                return;
            output.AddModelEdit(edits);
        }

        public override bool MaybeHasData => false;
        public override IModifierData CreateDefaultData(in ModifierContext ctx) => null;
        public override IModifierData CreateData(string data) => null;

        private sealed class ChangeMaterialsEdit : IModifierModelEdit
        {
            private readonly EquiModifierChangeMaterialDefinition _definition;
            private readonly HashSet<string> _materials;

            public ChangeMaterialsEdit(EquiModifierChangeMaterialDefinition def, HashSet<string> materials)
            {
                _definition = def;
                _materials = materials;
            }

            public Hashing.Hash128 RuntimeHash => _definition._runtimeHash;

            public void Apply(MaterialEditsBuilder target)
            {
                foreach (var mtl in _materials)
                {
                    if (_definition._swaps.TryGetValue(mtl, out var swap))
                        target.SwapMaterial(mtl, swap);
                    if (_definition._edits.TryGetValue(mtl, out var edits))
                        target.Add(mtl, edits);
                }
            }

            public void ReturnToPool()
            {
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiModifierChangeMaterialDefinition : MyObjectBuilder_EquiModifierBaseDefinition
    {
        [XmlElement("Change")]
        public List<MaterialModifier> Replacements;

        public struct MaterialModifier
        {
            [XmlAttribute]
            public string Name;

            [XmlElement("Names")]
            public string[] Names;

            [XmlElement("Parameter")]
            public MaterialParameter[] Parameters;

            [XmlElement("NewName")]
            public string NewName;

            public void GetChanges(List<MaterialEdit> list)
            {
                foreach (var param in Parameters)
                {
                    if (param.Name.Contains("Texture"))
                    {
                        list.AddOrReplace(new MaterialEdit(MaterialEdit.ModeEnum.Texture, param.Name, param.Value));
                        continue;
                    }

                    string internalKey;
                    if (param.Name.Equals("Technique"))
                        internalKey = MaterialEdit.TechniqueKey;
                    else
                    {
                        list.AddOrReplace(new MaterialEdit(MaterialEdit.ModeEnum.UserData, param.Name, param.Value));
                        continue;
                    }

                    list.AddOrReplace(new MaterialEdit(MaterialEdit.ModeEnum.FieldKey, internalKey, param.Value));
                }
            }
        }

        public struct MaterialParameter
        {
            [XmlAttribute]
            public string Name;

            [XmlText]
            public string Value;
        }
    }
}