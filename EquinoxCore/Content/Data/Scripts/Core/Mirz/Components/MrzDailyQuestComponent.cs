using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Mirz.Definitions;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Medieval.Entities.Components.Quests;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzDailyQuestComponent : MyObjectBuilder_EntityComponent
    {
        public long LastTrigger;

        [XmlArrayItem("Quest")]
        public List<string> ActiveQuests;
    }

    [MyComponent(typeof(MyObjectBuilder_MrzDailyQuestComponent))]
    [MyDependency(typeof(MyQuestEntityComponent))]
    public class MrzDailyQuestComponent : MyEntityComponent
    {
        #region Private

        private MrzDailyQuestComponentDefinition _definition;
        private MyQuestEntityComponent _questComp;

        private HashSet<MyStringHash> _activeQuests = new HashSet<MyStringHash>(MyStringHash.Comparer);

        private long _lastTrigger;

        #endregion

        #region Lifecycle

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);

            _definition = definition as MrzDailyQuestComponentDefinition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            _questComp = this.Get<MyQuestEntityComponent>();
            
            CheckActive();
            TryStartQuest(0);

            MyQuestEntityComponent.OnQuestCompleted += OnQuestCompleted;
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            _questComp = null;
        }

        #endregion

        #region (De)Serialization

        public override bool IsSerialized => true;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = base.Serialize(copy) as MyObjectBuilder_MrzDailyQuestComponent;

            ob.LastTrigger = _lastTrigger;
            ob.ActiveQuests = new List<string>(_activeQuests.Count);

            foreach (var quest in _activeQuests)
                ob.ActiveQuests.Add(quest.String);

            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);

            var ob = builder as MyObjectBuilder_MrzDailyQuestComponent;

            _lastTrigger = ob.LastTrigger;
            
            if (ob.ActiveQuests == null || ob.ActiveQuests.Count == 0)
                return;

            foreach (var quest in ob.ActiveQuests)
                _activeQuests.Add(MyStringHash.GetOrCompute(quest));
        }

        #endregion

        #region Helpers

        [Update(false)]
        private void TryStartQuest(long deltaFrames)
        {
            var diff = _lastTrigger + _definition.TimeBetweenQuests - MySession.Static.ElapsedGameTime.TotalMilliseconds;
            if (_lastTrigger > 0 && diff > 0)
            {
                AddScheduledCallback(TryStartQuest, (long)Math.Ceiling(diff));
                return;
            }

            if (!HasFreeSlots())
                return;

            var questId = _definition.GetRandomQuest();
            if (questId.HasValue)
                _questComp.StartQuest(questId.Value);
        }

        /// <summary>
        /// Checks whether all localy tracked quests are actually active.
        /// </summary>
        private void CheckActive()
        {
            if (_questComp == null || _activeQuests.Count == 0)
                return;

            var toRemove = new List<MyStringHash>();
            foreach (var quest in _activeQuests)
            {
                if (!_questComp.IsQuestActive(quest))
                    toRemove.Add(quest);
            }

            foreach (var quest in toRemove)
                _activeQuests.Remove(quest);
        }

        private bool HasFreeSlots()
        {
            return _activeQuests.Count < _definition.MaxQuests;
        }

        #endregion

        #region Bindings

        private void OnQuestCompleted(MyQuestEntityComponent questComponent, MyStringHash questSubtypeId, CompletionCause completionCause)
        {
            // we need to check if this message is from our quest component ... static events are dumb
            if (_questComp != questComponent)
                return;

            _activeQuests.Remove(questSubtypeId);
        }

        #endregion
    }
}
