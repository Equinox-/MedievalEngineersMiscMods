using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util;
using VRage.Components;
using VRage.Definitions;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiModifierChangeModelDefinition))]
    public class EquiModifierChangeModelDefinition : EquiModifierBaseDefinition
    {
        private readonly Dictionary<string, Data> _modelReplacements = new Dictionary<string, Data>();

        private struct Data
        {
            public readonly MyDiscreteSampler<string> Collection;
            public readonly string Model;

            public Data(MyDiscreteSampler<string> collection)
            {
                Collection = collection;
                Model = null;
            }

            public Data(string model)
            {
                Collection = null;
                Model = model;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiModifierChangeModelDefinition) def;
            if (ob.Replacements == null) return;
            foreach (var replacement in ob.Replacements)
            {
                if (!string.IsNullOrEmpty(replacement.To))
                {
                    _modelReplacements[replacement.From] = new Data(replacement.To);
                    continue;
                }

                _modelReplacements[replacement.From] = new Data(new MyDiscreteSampler<string>(replacement.Weighted.Select(x => x.Model),
                    replacement.Weighted.Select(x => x.Weight > 0 ? x.Weight : 1)));
            }
        }

        private Data? OperationFor(in ModifierContext ctx)
        {
            var model = ctx.OriginalModel;
            if (string.IsNullOrEmpty(model))
                return null;
            if (_modelReplacements.TryGetValue(model, out var data))
                return data;
            return null;
        }

        public override bool CanApply(in ModifierContext ctx)
        {
            return base.CanApply(in ctx) && OperationFor(in ctx).HasValue;
        }

        public override void Apply(in ModifierContext ctx, IModifierData data, ref ModifierOutput output)
        {
            var op = OperationFor(in ctx);
            if (!op.HasValue)
                return;
            if (op.Value.Collection == null)
            {
                output.Model = op.Value.Model;
                return;
            }

            var longValue = (data as ModifierDataLong)?.Raw ?? 0;
            var strHash = (int) (longValue);
            var seed = (int) (longValue >> 32);
            foreach (var str in op.Value.Collection)
                if (str.GetHashCode() == strHash)
                {
                    output.Model = str;
                    return;
                }

            using (MyRandom.Instance.PushSeed(seed))
                output.Model = op.Value.Collection.Sample(MyRandom.Instance);
        }

        public override IModifierData CreateData(in ModifierContext ctx)
        {
            var op = OperationFor(in ctx);
            if (op?.Collection == null)
                return null;
            var seed = MyRandom.Instance.Next();
            int modelHash;
            using (MyRandom.Instance.PushSeed(seed))
                modelHash = op.Value.Collection.Sample(MyRandom.Instance).GetHashCode();
            return new ModifierDataLong(((long) seed << 32) | modelHash);
        }

        public override IModifierData CreateData(string data)
        {
            return new ModifierDataLong(data);
        }

        public override bool ShouldEvict(EquiModifierBaseDefinition other)
        {
            return other is EquiModifierChangeModelDefinition || base.ShouldEvict(other);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiModifierChangeModelDefinition : MyObjectBuilder_EquiModifierBaseDefinition
    {
        [XmlElement("Change")]
        public List<Replacement> Replacements;

        public struct Replacement
        {
            [XmlAttribute]
            public string From;

            [XmlAttribute("To")]
            public string To;

            [XmlElement("To")]
            public WeightedModel[] Weighted;
        }

        public struct WeightedModel
        {
            [XmlAttribute]
            public string Model;

            [XmlAttribute]
            public float Weight;
        }
    }
}