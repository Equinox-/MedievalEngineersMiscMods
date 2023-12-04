using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using VRage;
using VRage.Entity.Block;
using VRage.Game.ObjectBuilders.ComponentSystem;

namespace CartographyBaker;

public static class SaveFileAccessor
{
    public static ParallelQuery<EntityAccessor> Entities(string save)
    {
        var entitiesPath = Path.Combine(save, "Entities");
        var entityFiles = Directory.GetFiles(entitiesPath, "*.entity", SearchOption.AllDirectories);
        return entityFiles.AsParallel()
            .Select(entity =>
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(entity);
                    return new EntityAccessor(doc["MySerializedEntity"]!["Entity"]);
                }
                catch (Exception err)
                {
                    Console.WriteLine($"Failed to load entity {entity}: {err}");
                    return default;
                }
            })
            .Where(entity => entity.Entity != null);
    }

    private static readonly ConcurrentDictionary<(Type, string), XmlSerializer> DeserializerCache = new();

    public static T DeserializeAs<T>(this XmlNode node)
    {
        var deserializer = DeserializerCache.GetOrAdd((typeof(T), node.Name),
            key => new XmlSerializer(key.Item1, new XmlRootAttribute(key.Item2)));
        return (T)deserializer.Deserialize(new XmlNodeReader(node));
    }

    public readonly struct EntityAccessor
    {
        public readonly XmlNode Entity;

        public MyPositionAndOrientation Position => Entity["PositionAndOrientation"].DeserializeAs<MyPositionAndOrientation>();

        public string Subtype => Entity.Attributes?["Subtype"]?.Value;

        public long Id => long.Parse(Entity["EntityId"]!.InnerText);
        
        public XmlNode Component(string type) =>
            Entity["ComponentContainer"]?
                .OfType<XmlNode>()
                .FirstOrDefault(node => node.Attributes!["xsi:type"].Value == type);

        public T Component<T>() where T : MyObjectBuilder_EntityComponent => Component(typeof(T).Name)?.DeserializeAs<T>();

        public IEnumerable<(BlockId Id, EntityAccessor Entity)> BlockEntities
        {
            get
            {
                return Component("MyObjectBuilder_GridHierarchyComponent")?["BlockToEntityMap"]?.ChildNodes
                           .OfType<XmlNode>()
                           .Select(node => (new BlockId(ulong.Parse(node["BlockId"]!.InnerText)), new EntityAccessor(node["Entity"])))
                       ?? Enumerable.Empty<(BlockId, EntityAccessor)>();
            }
        }

        public EntityAccessor(XmlNode entity) => Entity = entity;
    }
}