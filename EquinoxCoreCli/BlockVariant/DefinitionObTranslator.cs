using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Equinox76561198048419394.Core.Cli.Def;
using Medieval.ObjectBuilders.Definitions.Block;
using Medieval.ObjectBuilders.Definitions.BlockGeneration;
using VRage;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Library.Collections;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Block;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Cli.BlockVariant
{
    public class DefinitionObTranslator
    {
        private readonly DefinitionObSet _sourceSet;
        private readonly DefinitionObSet _destinationSet;
        private readonly List<MethodInfo> _translators;
        private readonly List<MethodInfo> _conditionalTranslators;
        private readonly string _subtypeSuffix;
        private readonly Dictionary<MyDefinitionId, MyDefinitionId> _translationTable;
        private readonly Func<string, string> _assetTranslator;
        private readonly Func<string, string> _displayNameTranslator;

        public DefinitionObTranslator(DefinitionObSet sourceSet, DefinitionObSet destinationSet,
            Func<string, string> assetTranslator,
            Func<string, string> displayNameTranslator,
            string subtypeSuffix,
            Dictionary<MyDefinitionId, MyDefinitionId> translationTable)
        {
            _subtypeSuffix = subtypeSuffix;
            _sourceSet = sourceSet;
            _destinationSet = destinationSet;
            _translators = typeof(DefinitionObTranslator).GetMethods((BindingFlags) (-1)).Where(x => x.Name == nameof(TranslateInternal))
                .ToList();
            _conditionalTranslators = typeof(DefinitionObTranslator).GetMethods((BindingFlags) (-1)).Where(x => x.Name == nameof(ConditionalTranslateInternal))
                .ToList();
            _translationTable = translationTable;
            _assetTranslator = assetTranslator;
            _displayNameTranslator = displayNameTranslator;
        }

        public T Translate<T>(T input, bool forceCopy = false) where T : MyObjectBuilder_DefinitionBase
        {
            var translatedId = new MyDefinitionId(input.Id.TypeId, input.Id.SubtypeIdAttribute + _subtypeSuffix);
            var translated = _destinationSet.GetDefinition<T>(translatedId);
            if (translated != null && translated.GetType() == input.GetType())
                return translated;

            var args = new object[] {input, input};
            foreach (var translator in _translators)
            {
                if (translator.GetParameters()[0].ParameterType.IsInstanceOfType(input))
                {
                    translator.Invoke(this, args);
                }
            }

            var output = (T) args[1];
            if (output == input && forceCopy)
                output = (T) MyObjectBuilderSerializer.Clone(input);
            if (output != input)
            {
                foreach (var translator in _conditionalTranslators)
                {
                    if (translator.GetParameters()[0].ParameterType.IsInstanceOfType(input))
                    {
                        translator.Invoke(this, new object[] {output});
                    }
                }

                output.Id = translatedId;
                _destinationSet.AddOrReplaceDefinition(output);
                Console.WriteLine("Translated " + input.GetType().Name + " " + input.Id + " to " + output.Id);
            }

            return output;
        }

        private void TranslateInternal(MyObjectBuilder_VisualDefinitionBase input, ref MyObjectBuilder_VisualDefinitionBase output)
        {
            TranslateDependencies(input, ref output, _assetTranslator, (x) => x.Icons, (x, y) => x.Icons = y.ToArray());
        }

        private void ConditionalTranslateInternal(MyObjectBuilder_VisualDefinitionBase ob)
        {
            if (ob.DisplayName != null && ob.DisplayName.StartsWith("DisplayName_"))
                ob.DisplayName = _displayNameTranslator(MyTexts.Get(MyStringId.GetOrCompute(ob.DisplayName)).ToString());
            else
                ob.DisplayName = _displayNameTranslator(ob.DisplayName);
        }

        private void TranslateInternal(MyObjectBuilder_PhysicalModelDefinition input, ref MyObjectBuilder_PhysicalModelDefinition output)
        {
            TranslateDependency(input, ref output, _assetTranslator, (x) => x.Model, (x, y) => x.Model = y);
            TranslateDependencies(input, ref output, _assetTranslator, (x) => x.AdditionalModels, (x, y) => x.AdditionalModels = y.ToArray());
        }

        private void TranslateInternal(MyObjectBuilder_BlockDefinition input, ref MyObjectBuilder_BlockDefinition output)
        {
            TranslateDependency(input, ref output, _assetTranslator, (x) => x.PreviewModel, (x, y) => x.PreviewModel = y);
        }

        private void TranslateInternal(MyObjectBuilder_BuildableBlockDefinition input, ref MyObjectBuilder_BuildableBlockDefinition output)
        {
            TranslateDependencies(input, ref output, TranslateDependency, (x) => x.Components, (x, y) => x.Components = y.ToArray());
            TranslateDependencies(input, ref output, TranslateDependency, (x) => x.BuildProgressModels, (x, y) => x.BuildProgressModels = y.ToArray());
        }

        private void TranslateInternal(MyObjectBuilder_AdditionalBlocksDefinition input, ref MyObjectBuilder_AdditionalBlocksDefinition output)
        {
            TranslateDependencies(input, ref output, TranslateDependency, (x) => x.GenerateBlockItems, (x, y) => x.GenerateBlockItems = y.ToList());
        }

        private void TranslateInternal(MyObjectBuilder_BlockVariantsDefinition input, ref MyObjectBuilder_BlockVariantsDefinition output)
        {
            TranslateDependencies(input, ref output, (x) =>
            {
                var def = _sourceSet.GetDefinition<MyObjectBuilder_BlockDefinition>(x);
                return def == null ? x : Translate(def).Id;
            }, (x) => x.Blocks, (x, y) => x.Blocks = y.ToList());
        }

        private MyObjectBuilder_GenerateBlockItem TranslateDependency(MyObjectBuilder_GenerateBlockItem x)
        {
            var original = _sourceSet.GetDefinition<MyObjectBuilder_GeneratedBlockDefinition>(x.Id);
            if (original == null)
                return x;
            x.Id = Translate(original).Id;

            if (x.ReplaceItems != null)
            {
                var tmp = new List<ReplaceItem>();
                var changed = false;
                foreach (var k in x.ReplaceItems)
                {
                    var o = _sourceSet.GetDefinition<MyObjectBuilder_GeneratedBlockDefinition>(k.To);
                    var t = o != null ? Translate(o) : null;
                    if (t != o)
                    {
                        changed = true;
                        var copy = k;
                        copy.To = t.Id;
                        tmp.Add(copy);
                    }
                    else
                    {
                        tmp.Add(k);
                    }
                }

                if (changed)
                    x.ReplaceItems = tmp;
            }

            return x;
        }

        private MyObjectBuilder_BuildProgressModel TranslateDependency(MyObjectBuilder_BuildProgressModel dep)
        {
            var translatedAsset = _assetTranslator(dep.File);
            if (translatedAsset == dep.File) return dep;
            return new MyObjectBuilder_BuildProgressModel
                {File = translatedAsset, UpperBound = dep.UpperBound, BuildPercentUpperBound = dep.BuildPercentUpperBound};
        }

        private void TranslateInternal(MyObjectBuilder_ContainerDefinition input, ref MyObjectBuilder_ContainerDefinition output)
        {
            TranslateDependencies(input, ref output, TranslateDependency, (x) => x.Components, (x, y) =>
            {
                x.Components.Clear();
                x.Components.AddRange(y);
            });
        }

        private MyObjectBuilder_ContainerDefinition.ComponentEntry TranslateDependency(MyObjectBuilder_ContainerDefinition.ComponentEntry original)
        {
            var id = new MyDefinitionId(MyObjectBuilderType.Parse(original.Type), original.Subtype);
            var resultId = id;
            if (_translationTable.TryGetValue(resultId, out var replacedId))
            {
                resultId = replacedId;
            }
            else
            {
                var componentDef = _sourceSet.GetDefinitions(id);
                if (componentDef.Count == 1)
                {
                    var resultDef = Translate(componentDef[0]);
                    if (resultDef != componentDef[0])
                        resultId = resultDef.Id;
                }
            }

            if (resultId != id)
            {
                return new MyObjectBuilder_ContainerDefinition.ComponentEntry
                {
                    Type = resultId.TypeId.ShortName,
                    Subtype = resultId.SubtypeName,
                    AlwaysCreate = original.AlwaysCreate
                };
            }

            return original;
        }

        private MyObjectBuilder_CubeBlockDefinition.CbObCubeBlockComponent TranslateDependency(
            MyObjectBuilder_CubeBlockDefinition.CbObCubeBlockComponent original)
        {
            var originalComponent = (MyDefinitionId) original.Definition;
            var originalReturned = original.ReturnedItem.HasValue ? (MyDefinitionId) original.ReturnedItem.Value : (MyDefinitionId?) null;
            var replacedComponent = _translationTable.GetValueOrDefault(originalComponent, originalComponent);
            var replacedReturned = originalReturned.HasValue
                ? _translationTable.GetValueOrDefault(originalReturned.Value, originalReturned.Value)
                : (MyDefinitionId?) null;

            if (replacedReturned != originalReturned || replacedComponent != originalComponent)
            {
                return new MyObjectBuilder_CubeBlockDefinition.CbObCubeBlockComponent
                {
                    Definition = replacedComponent,
                    Count = original.Count,
                    ReturnedItem = replacedReturned
                };
            }

            return original;
        }

        private void TranslateDependency<T, TDependent>(T input, ref T output,
            Func<TDependent, TDependent> translator,
            Func<T, TDependent> getDependency,
            Action<T, TDependent> setDependency
        )
            where T : MyObjectBuilder_DefinitionBase
        {
            var comp = EqualityComparer<TDependent>.Default;
            using (PoolManager.Get(out List<TDependent> lst))
            {
                var original = getDependency(output);
                var translated = translator(original);

                if (!comp.Equals(original, translated))
                {
                    if (output == input)
                        output = (T) MyObjectBuilderSerializer.Clone(input);
                    setDependency(output, translated);
                }
            }
        }

        private void TranslateDependencies<T, TDependent>(T input,
            ref T output,
            Func<TDependent, TDependent> translator,
            Func<T, IEnumerable<TDependent>> getDependencies,
            Action<T, IEnumerable<TDependent>> setDependencies)
            where T : MyObjectBuilder_DefinitionBase
        {
            var comp = EqualityComparer<TDependent>.Default;
            using (PoolManager.Get(out List<TDependent> lst))
            {
                var inequal = false;
                var deps = getDependencies(output);
                if (deps != null)
                    foreach (var dep in deps)
                    {
                        var result = translator(dep);
                        if (!comp.Equals(dep, result))
                            inequal = true;
                        lst.Add(result);
                    }

                if (inequal)
                {
                    if (output == input)
                        output = (T) MyObjectBuilderSerializer.Clone(input);
                    setDependencies(output, lst);
                }
            }
        }
    }
}