using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;

namespace Equinox76561198048419394.Core.Util
{
    /// <summary>
    /// Adds a lazily-run initialize method after the entire definition set is loaded.
    /// Must be used with <see cref="LazyDefinitionHandler"/>.
    /// </summary>
    public interface ILazyInitDefinition
    {
        void LazyInit();
    }

    public class LazyDefinitionHandler : MyDefinitionHandler
    {
        public override void AfterLoad(MyDefinitionSet set, List<MyDefinitionBase> definitions)
        {
            base.AfterLoad(set, definitions);
            foreach (var def in definitions)
                ((ILazyInitDefinition)def).LazyInit();
        }
    }
}