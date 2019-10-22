using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzDailyQuestComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public TimeDefinition TimeBetweenQuests;
        public int? MaxQuests;

        [XmlElement("Quest")]
        public List<string> Quests;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzDailyQuestComponentDefinition))]
    public class MrzDailyQuestComponentDefinition: MyEntityComponentDefinition
    {
        public List<MyStringHash> Quests = new List<MyStringHash>();
        public long TimeBetweenQuests;
        public int MaxQuests;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = (MyObjectBuilder_MrzDailyQuestComponentDefinition) builder;

            MaxQuests = ob.MaxQuests ?? 3;
            TimeBetweenQuests = (long)((TimeSpan) ob.TimeBetweenQuests).TotalMilliseconds;

            if (ob.Quests == null || ob.Quests.Count == 0)
                throw new MyDefinitionException("Daily quest components needs to have quests defined");

            foreach (var quest in ob.Quests)
                Quests.Add(MyStringHash.GetOrCompute(quest));
        }

        public MyStringHash? GetRandomQuest()
        {
            if (Quests == null || Quests.Count == 0)
                return null;

            return Quests[MyRandom.Instance.Next(Quests.Count)];
        }
    }
}
