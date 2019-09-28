using System.Collections.Generic;
using VRage.Components;
using VRage.Game.Components;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Extensions
{
    public static class MrzComponentExtensions
    {
        /// <summary>
        /// Gets a component of type specified on the same entity.
        /// </summary>
        /// <typeparam name="TComp"></typeparam>
        /// <param name="comp"></param>
        /// <returns></returns>
        public static TComp Get<TComp>(this MyEntityComponent comp) where TComp: MyEntityComponent
        {
            return comp.Entity?.Get<TComp>() ?? null;
        }

        /// <summary>
        /// Gets a component of type and subtype specified on the same entity.
        /// </summary>
        /// <typeparam name="TComp"></typeparam>
        /// <param name="comp"></param>
        /// <param name="subtype"></param>
        /// <returns></returns>
        public static TComp Get<TComp>(this MyEntityComponent comp, MyStringHash subtype) where TComp: MyMultiComponent
        {
            return comp.Entity?.Get<TComp>(subtype) ?? null;
        }

        /// <summary>
        /// Gets all components of type specified on the same entity.
        /// </summary>
        /// <typeparam name="TComp"></typeparam>
        /// <param name="comp"></param>
        /// <returns></returns>
        public static IEnumerable<TComp> GetAll<TComp>(this MyEntityComponent comp) where TComp : MyEntityComponent
        {
            return comp.Entity?.Components?.GetComponents<TComp>();
        }
    }
}
