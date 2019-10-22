using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRage.Collections;
using VRage.Collections.Concurrent;
using VRage.Components;
using VRage.Definitions;
using VRage.Engine;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Collections;
using VRage.Logging;

namespace Equinox76561198048419394.Core.ModelCreator
{
    public static class DefinitionObLoader
    {
        private static MyDefinitionLoader loader => MyDefinitionManager.Loader;
        private static NamedLogger m_logger = new NamedLogger(MyLog.Default);

        private static readonly FieldInfo m_currentObsetField =
            typeof(MyDefinitionLoader).GetField("m_currentObset", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo m_modObjectBuildersField =
            typeof(MyDefinitionLoader).GetField("m_modObjectBuilders", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo m_handlerAttributeField =
            typeof(MyDefinitionHandler).GetField("Attribute", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo m_mergeBuildersMethod =
            typeof(MyDefinitionLoader).GetMethod("MergeBuilders", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo m_resolveInheritanceMethod =
            typeof(MyDefinitionLoader).GetMethod("ResolveInheritance", BindingFlags.Instance | BindingFlags.NonPublic);


        private static List<MyObjectBuilder_DefinitionBase> m_currentObset => (List<MyObjectBuilder_DefinitionBase>) m_currentObsetField.GetValue(loader);

        private static MyConcurrentDictionary<Type, MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>>>
            m_modObjectBuilders =>
            (MyConcurrentDictionary<Type, MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>>>)
            m_modObjectBuildersField.GetValue(loader);

        private static void MergeBuilders(
            MyDefinitionHandler handler,
            MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>> modBuilders,
            List<MyObjectBuilder_DefinitionBase> merged)
        {
            m_mergeBuildersMethod.Invoke(loader, new object[] {handler, modBuilders, merged});
        }

        private static void ResolveInheritance(
            MyDefinitionHandler handler,
            List<MyObjectBuilder_DefinitionBase> currentObset)
        {
            m_resolveInheritanceMethod.Invoke(loader, new object[] {handler, currentObset});
        }

        public static readonly DefinitionObSet Loaded = new DefinitionObSet();
        
        public static void LoadObjectBuilders(MyDefinitionSet set)
        {
            var definitionFactory = MyDefinitionFactory.Get();
            var definitionHandlers = definitionFactory.DefinitionHandlers;
            var currentObset = m_currentObset;
            for (var index1 = 0; index1 < definitionHandlers.Count; ++index1)
            {
                var handler = definitionHandlers[index1];
                var objectBuilderType = ((MyDefinitionTypeAttribute) m_handlerAttributeField.GetValue(handler)).ObjectBuilderType;
                if (handler.HasBeforeLoad)
                    handler.BeforeLoad(set);
                MyConcurrentSortedDictionary<IApplicationPackage, MyConcurrentList<MyObjectBuilder_DefinitionBase>> modBuilders;
                if (m_modObjectBuilders.TryGetValue(objectBuilderType, out modBuilders))
                {
                    MergeBuilders(handler, modBuilders, m_currentObset);
                    ResolveInheritance(handler, m_currentObset);
                    for (var index2 = 0; index2 < m_currentObset.Count; ++index2)
                    {
                        var builder = currentObset[index2];
                        if (!builder.Abstract)
                        {
                            Loaded.AddOrReplaceDefinition(builder);
                        }
                    }

                    m_currentObset.Clear();
                }
            }
        }
    }
}