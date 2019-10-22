using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiModifierChangeColorDefinition))]
    public class EquiModifierChangeColorDefinition : EquiModifierBaseDefinition
    {
        // Memorized table of models this modifier can be applied to.
        private readonly ConcurrentDictionary<string, bool> _memorizedApplicableTo = new ConcurrentDictionary<string, bool>();

        private HashSet<string> _materialDependencies;

        public Vector3? ColorMask { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiModifierChangeColorDefinition) def;
            ColorMask = ob.ColorMask;
            if (ob.MaterialDependencies != null && ob.MaterialDependencies.Count > 0)
            {
                _materialDependencies = new HashSet<string>();
                foreach (var s in ob.MaterialDependencies)
                    _materialDependencies.Add(string.Intern(s));
            }
        }

        public override bool CanApply(in ModifierContext ctx)
        {
            return base.CanApply(in ctx) && (_materialDependencies == null || _memorizedApplicableTo.GetOrAdd(ctx.OriginalModel, (modelName) =>
            {
                foreach (var mtl in MySession.Static.Components.Get<DerivedModelManager>()?.GetMaterialsForModel(modelName) ?? InterningBag<string>.Empty)
                    if (_materialDependencies.Contains(mtl))
                        return true;
                return false;
            }));
        }

        public override void Apply(in ModifierContext ctx, IModifierData data, ref ModifierOutput output)
        {
            if (ColorMask.HasValue)
                output.ColorMask = ColorMask.Value;
            else if (data is ModifierDataColor colorMod)
                output.ColorMask = colorMod.Raw;
        }

        public override IModifierData CreateData(in ModifierContext ctx)
        {
            return null;
        }

        public override IModifierData CreateData(string data)
        {
            return ColorMask.HasValue ? null : new ModifierDataColor(data);
        }

        public override bool ShouldEvict(EquiModifierBaseDefinition other)
        {
            return other is EquiModifierChangeColorDefinition || base.ShouldEvict(other);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiModifierChangeColorDefinition : MyObjectBuilder_EquiModifierBaseDefinition
    {
        public SerializableVector3? ColorMask;

        [XmlElement("MaterialDependency")]
        public List<string> MaterialDependencies;
    }
}