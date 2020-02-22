using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using ObjectBuilders.Definitions.GUI;
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

        public Vector3? ColorMaskHsv { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiModifierChangeColorDefinition) def;
            ColorMaskHsv = ob.ColorMaskHsv;
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
            if (ColorMaskHsv.HasValue)
                output.ColorMaskHsv = ColorMaskHsv.Value;
            else if (data is ModifierDataColor colorMod)
                output.ColorMaskHsv = colorMod.Raw;
        }

        public override IModifierData CreateData(in ModifierContext ctx)
        {
            return null;
        }

        public override IModifierData CreateData(string data)
        {
            return ColorMaskHsv.HasValue ? null : new ModifierDataColor(data);
        }

        public override bool ShouldEvict(EquiModifierBaseDefinition other)
        {
            return other is EquiModifierChangeColorDefinition || base.ShouldEvict(other);
        }

        public override string ToString()
        {
            return $"{Id}[{nameof(ColorMaskHsv)}={ColorMaskHsv}]";
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiModifierChangeColorDefinition : MyObjectBuilder_EquiModifierBaseDefinition
    {
        public ColorDefinitionHSV? ColorMaskHsv;

        public SerializableVector3? ColorMask
        {
            // Do not use, only here for back compat
            get => (Vector3?) ColorMaskHsv;
            set => ColorMaskHsv = value.HasValue ? (ColorDefinitionHSV?) CastCorrect(value.Value) : null;
        }
        
        // Vanilla one is all kinds of screwed up, so use the fixed one
        private static ColorDefinitionHSV CastCorrect(Vector3 vector)
        {
            return new ColorDefinitionHSV()
            {
                H = (int) MathHelper.Clamp(vector.X * 360f, 0f, 360f),
                S = (int) MathHelper.Clamp(vector.Y * 100f, -100f, 100f),
                V = (int) MathHelper.Clamp(vector.Z * 100f, -100f, 100f)
            };
        }

        [XmlElement("MaterialDependency")]
        public List<string> MaterialDependencies;
    }
}