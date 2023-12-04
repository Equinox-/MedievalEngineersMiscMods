using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Collections;
using VRage.Game;

namespace Equinox76561198048419394.Core.Cli.Def
{
    public class DefinitionObSet
    {
        private readonly Dictionary<MyDefinitionId, List<MyObjectBuilder_DefinitionBase>> _objectBuilders =
            new Dictionary<MyDefinitionId, List<MyObjectBuilder_DefinitionBase>>();

        public IEnumerable<MyObjectBuilder_DefinitionBase> AllDefinitions => _objectBuilders.Values.SelectMany(x => x);
        
        public ListReader<MyObjectBuilder_DefinitionBase> GetDefinitions(MyDefinitionId id)
        {
            var lst = _objectBuilders.GetValueOrDefault(id, null);
            return lst ?? ListReader<MyObjectBuilder_DefinitionBase>.Empty;
        }

        public T GetDefinition<T>(MyDefinitionId id) where T : MyObjectBuilder_DefinitionBase
        {
            return _objectBuilders.GetValueOrDefault(id)?.OfType<T>()?.FirstOrDefault();
        }

        public MyObjectBuilder_DefinitionBase GetDefinition(TypedDefinitionReference def)
        {
            var list = _objectBuilders.GetValueOrDefault(def.Id);
            if (list == null)
                return null;
            foreach (var k in list)
                if (def.Type.IsInstanceOfType(k))
                    return k;
            return null;
        }

        public void AddOrReplaceDefinition(MyObjectBuilder_DefinitionBase builder)
        {
            if (!_objectBuilders.TryGetValue(builder.Id, out var list))
                _objectBuilders.Add(builder.Id, list = new List<MyObjectBuilder_DefinitionBase>());
            for (var i = 0; i < list.Count; i++)
                if (list[i].GetType() == builder.GetType())
                {
                    list[i] = builder;
                    return;
                }

            list.Add(builder);
        }

        public struct TypedDefinitionReference
        {
            public readonly MyDefinitionId Id;
            public readonly Type Type;

            public TypedDefinitionReference(MyDefinitionId id, Type type)
            {
                Id = id;
                Type = type;
            }
        }
    }
}