using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sandbox.Definitions;
using VRage.Collections.Concurrent;
using VRage.Definitions;
using VRage.Engine;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Collections;

namespace Equinox76561198048419394.Core.Cli.Def
{
    public static class DefinitionObLoader
    {
        private static MyDefinitionLoader Loader => MyDefinitionManager.Loader;

        private static readonly FieldInfo CurrentObsetField =
            typeof(MyDefinitionLoader).GetField("m_currentObset", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ModObjectBuildersField =
            typeof(MyDefinitionLoader).GetField("m_modObjectBuilders", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo HandlerAttributeField =
            typeof(MyDefinitionHandler).GetField("Attribute", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo MergeBuildersMethod =
            typeof(MyDefinitionLoader).GetMethod("MergeBuilders", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo ResolveInheritanceMethod =
            typeof(MyDefinitionLoader).GetMethod("ResolveInheritance", BindingFlags.Instance | BindingFlags.NonPublic);


        private static List<MyObjectBuilder_DefinitionBase> CurrentObset => (List<MyObjectBuilder_DefinitionBase>)CurrentObsetField.GetValue(Loader);

        private static MyConcurrentDictionary<Type, MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>>>
            ModObjectBuilders =>
            (MyConcurrentDictionary<Type, MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>>>)
            ModObjectBuildersField.GetValue(Loader);

        private static void MergeBuilders(
            MyDefinitionHandler handler,
            MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>> modBuilders,
            List<MyObjectBuilder_DefinitionBase> merged)
        {
            MergeBuildersMethod.Invoke(Loader, new object[] { handler, modBuilders, merged });
        }

        private static void ResolveInheritance(
            MyDefinitionHandler handler,
            List<MyObjectBuilder_DefinitionBase> currentObset)
        {
            ResolveInheritanceMethod.Invoke(Loader, new object[] { handler, currentObset });
        }

        public static DefinitionObSet Load(
            IReadOnlyCollection<string> additionalContentPaths = null,
            IEnumerable<Type> obFilter = null)
        {
            if (additionalContentPaths == null)
                additionalContentPaths = new List<string>();
            MyFileSystem.SetAdditionalContentPaths(additionalContentPaths);
            MyDefinitionManagerSandbox.Static.LoadData(additionalContentPaths
                .Select(contentPath =>
                    new MyModContext(Path.GetFileNameWithoutExtension(contentPath), Path.GetFileNameWithoutExtension(contentPath), contentPath))
                .ToList());
            Console.WriteLine("Processing definitions...");
            return LoadObjectBuilders(MyDefinitionManagerSandbox.Static.DefinitionSet, obFilter);
        }

        public static DefinitionObSet LoadObjectBuilders(MyDefinitionSet set, IEnumerable<Type> obFilter = null)
        {
            var dest = new DefinitionObSet();
            var definitionFactory = MyDefinitionFactory.Get();
            var definitionHandlers = definitionFactory.DefinitionHandlers;
            var currentObset = CurrentObset;

            var filterSet = obFilter != null ? new HashSet<Type>(obFilter) : null;

            for (var index1 = 0; index1 < definitionHandlers.Count; ++index1)
            {
                var handler = definitionHandlers[index1];
                var objectBuilderType = ((MyDefinitionTypeAttribute)HandlerAttributeField.GetValue(handler)).ObjectBuilderType;
                if (!TestFilter(filterSet, objectBuilderType))
                    continue;
                if (handler.HasBeforeLoad)
                    handler.BeforeLoad(set);
                MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>> modBuilders;
                if (ModObjectBuilders.TryGetValue(objectBuilderType, out modBuilders))
                {
                    MergeBuilders(handler, modBuilders, CurrentObset);
                    ResolveInheritance(handler, CurrentObset);
                    for (var index2 = 0; index2 < CurrentObset.Count; ++index2)
                    {
                        var builder = currentObset[index2];
                        if (!builder.Abstract)
                        {
                            dest.AddOrReplaceDefinition(builder);
                        }
                    }

                    CurrentObset.Clear();
                }
            }

            return dest;
        }

        private static bool TestFilter(HashSet<Type> filter, Type query)
        {
            if (filter == null || filter.Count == 0)
                return true;
            while (query != null)
            {
                if (filter.Contains(query))
                    return true;
                query = query.BaseType;
            }

            return false;
        }
    }
}