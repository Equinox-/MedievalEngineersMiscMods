using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using VRage.Collections;
using VRage.Components;
using VRage.Definitions;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Models;
using VRage.Library.Collections;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiModifierChangeMaterialDefinition))]
    public class EquiModifierChangeMaterialDefinition : EquiModifierBaseDefinition
    {
        // Change materials per model (including LODs)
        private readonly ConcurrentDictionary<string, InterningBag<string>> _memorizedMaterialEdits = new ConcurrentDictionary<string, InterningBag<string>>();
        private readonly Dictionary<string, List<MaterialEdit>> _edits = new Dictionary<string, List<MaterialEdit>>();

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiModifierChangeMaterialDefinition) def;
            if (ob.Replacements == null) return;
            foreach (var mod in ob.Replacements)
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

        public override bool CanApply(in ModifierContext ctx)
        {
            if (!base.CanApply(in ctx))
                return false;
            var model = ctx.OriginalModel;
            if (model == null)
                return false;
            return GetMaterialEdits(model).Count > 0;
        }

        private InterningBag<string> GetMaterialEdits(string model)
        {
            return _memorizedMaterialEdits.GetOrAdd(model, (modelName) =>
            {
                using (PoolManager.Get(out HashSet<string> valid))
                {
                    foreach (var mtl in MySession.Static.Components.Get<DerivedModelManager>()?.GetMaterialsForModel(modelName) ?? InterningBag<string>.Empty)
                        if (_edits.ContainsKey(mtl))
                            valid.Add(mtl);
                    return InterningBag<string>.Of(valid);
                }
            });
        }

        public override void Apply(in ModifierContext ctx, IModifierData data, ref ModifierOutput output)
        {
            var model = ctx.OriginalModel;
            if (model == null)
                return;
            var materials = GetMaterialEdits(model);
            if (materials.Count == 0)
                return;
            if (output.MaterialEditsBuilder == null)
                output.MaterialEditsBuilder = MaterialEditsBuilder.Allocate();
            foreach (var mtl in materials)
                output.MaterialEditsBuilder.Add(mtl, _edits[mtl]);
        }

        public override IModifierData CreateData(in ModifierContext ctx)
        {
            return null;
        }

        public override IModifierData CreateData(string data)
        {
            return null;
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