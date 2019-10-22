using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.State
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiEventStateDefinitionBase))]
    public abstract class EquiEventStateDefinitionBase : MyEntityComponentDefinition
    {
        public bool Defer { get; private set; }
        
        protected class Trigger
        {
            public readonly string Event;
            public readonly MyStringHash From;
            public readonly MyStringHash To;

            public Trigger(MyObjectBuilder_EquiEventStateDefinitionBase.Trigger ob)
            {
                Event = ob.Event;
                From = MyStringHash.GetOrCompute(ob.From);
                To = MyStringHash.GetOrCompute(ob.To);
            }

            public override string ToString()
            {
                return $"{Event}={From}->{To}";
            }
        }

        public IReadOnlyCollection<string> Events => _events;
        
        private readonly HashSet<string> _events = new HashSet<string>();
        
        public bool HasEvent(string evt)
        {
            return _events.Contains(evt);
        }
        
        private readonly List<Trigger> _triggers = new List<Trigger>();

        protected IReadOnlyCollection<Trigger> Triggers => _triggers;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiEventStateDefinitionBase) def;
            Defer = ob.Defer;
            _events.Clear();
            foreach (var t in ob.Triggers)
            {
                var trig = new Trigger(t);
                if (string.IsNullOrEmpty(trig.Event))
                    MyDefinitionErrors.Add(Package, $"Trigger {t} in {Id} has no event", LogSeverity.Critical);
                if (trig.From == MyStringHash.NullOrEmpty && trig.To == MyStringHash.NullOrEmpty)
                    MyDefinitionErrors.Add(Package, $"Trigger {t} in {Id} has no from and no to state", LogSeverity.Critical);
                _triggers.Add(trig);
                _events.Add(trig.Event);
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public abstract class MyObjectBuilder_EquiEventStateDefinitionBase : MyObjectBuilder_EntityComponentDefinition
    {
        public bool Defer;
        
        [XmlElement("Trigger")]
        public Trigger[] Triggers;

        public class Trigger
        {
            [XmlAttribute]
            public string Event;

            [XmlAttribute]
            public string From;

            [XmlAttribute]
            public string To;
        }
    }
}