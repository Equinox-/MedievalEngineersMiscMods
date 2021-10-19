using System;
using System.Collections.Concurrent;
using Havok;
using VRage.Game;

namespace Equinox76561198048419394.Core.Util
{
    public static class ContainerDefinitionExtension
    {
        private struct Key : IEquatable<Key>
        {
            public readonly MyContainerDefinition Definition;
            public readonly Type Type;

            public Key(MyContainerDefinition definition, Type type)
            {
                Definition = definition;
                Type = type;
            }

            public bool Equals(Key other)
            {
                return Definition == other.Definition && Type == other.Type;
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Definition != null ? Definition.GetHashCode() : 0) * 397) ^ (Type != null ? Type.GetHashCode() : 0);
                }
            }
        }

        private static readonly ConcurrentDictionary<Key, MyEntityComponentDefinition> ComponentDef =
            new ConcurrentDictionary<Key, MyEntityComponentDefinition>();

        public static T Get<T>(this MyContainerDefinition container) where T : MyEntityComponentDefinition
        {
            if (container.Components == null)
                return null;
            if (container.Components.Count < 4)
            {
                foreach (var c in container.Components)
                    if (c.Definition is T tr)
                        return tr;
                return null;
            }

            return (T) ComponentDef.GetOrAdd(new Key(container, typeof(T)), (key) =>
            {
                foreach (var c in container.Components)
                    if (key.Type.IsInstanceOfType(c.Definition))
                        return c.Definition;
                return null;
            });
        }
    }
}