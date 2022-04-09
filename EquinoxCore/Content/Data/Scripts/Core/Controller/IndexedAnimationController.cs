using System.Collections.Generic;
using VRage.Collections;
using VRage.Game.Definitions.Animation;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Controller
{
    public sealed class IndexedAnimationController
    {
        private readonly MyAnimationControllerDefinition _def;
        private readonly MyHashSetDictionary<MyStringId, MyStringId> _stateToIncomingActions = new MyHashSetDictionary<MyStringId, MyStringId>(
            MyStringId.Comparer, MyStringId.Comparer);
        private readonly MyHashSetDictionary<MyStringId, MyStringId> _stateToOutgoingActions = new MyHashSetDictionary<MyStringId, MyStringId>(
            MyStringId.Comparer, MyStringId.Comparer);

        private readonly MyHashSetDictionary<MyStringId, MyStringId> _actionToFrom = new MyHashSetDictionary<MyStringId, MyStringId>(
            MyStringId.Comparer, MyStringId.Comparer);
        private readonly MyHashSetDictionary<MyStringId, MyStringId> _actionToTo = new MyHashSetDictionary<MyStringId, MyStringId>(
            MyStringId.Comparer, MyStringId.Comparer);

        public IndexedAnimationController(MyAnimationControllerDefinition def)
        {
            _def = def;
            if (def.StateMachines == null) return;
            foreach (var sm in def.StateMachines)
            {
                if (sm.Transitions == null) continue;
                foreach (var action in sm.Transitions)
                {
                    var name = MyStringId.GetOrCompute(action.Name);
                    var from = MyStringId.GetOrCompute(action.From);
                    var to = MyStringId.GetOrCompute(action.To);
                    _stateToOutgoingActions.Add(from, name);
                    _stateToIncomingActions.Add(to, name);
                    _actionToFrom.Add(name, from);
                    _actionToTo.Add(name, to);
                }
            }
        }

        public bool HasDirectTransition(MyStringId lastTransitionUsed, MyStringId desiredTransition)
        {
            if (!_actionToTo.TryGet(lastTransitionUsed, out var possibleStates))
                return false;
            foreach (var possibleState in possibleStates)
                if (_stateToOutgoingActions.TryGet(possibleState, out var possibleOutgoing) && possibleOutgoing.Contains(desiredTransition))
                    return true;
            return false;
        }
    }
}