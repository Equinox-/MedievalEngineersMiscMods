using System;
using VRage.Game;
using VRage.Logging;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Util
{
    public interface IDefinitionReferOrEmbed<out TDef> where TDef : IEmbeddableDefinition
    {
        TDef Get(MyDefinitionBase owner);
    }

    public interface IDefinitionReference<out TDef> : IDefinitionReferOrEmbed<TDef> where TDef : IEmbeddableDefinition
    {
        string SubtypeName { get; }
    }

    public interface IEmbeddableDefinition
    {
        /// <summary>
        /// Definition this was embedded in, or null if it was not embedded.
        /// </summary>
        MyDefinitionBase EmbeddedIn { get; set; }
    }

    public static class ReferOrEmbedDefinition
    {
        // ReSharper disable once ClassNeverInstantiated.Local
        private class DefinitionLogHelper : MyDefinitionBase
        {
            public static ref NamedLogger ExposedLog => ref Log;
        }

        public static TDef Embedded<TDef, TOb>(this TOb ob, MyDefinitionBase owner)
            where TDef : MyDefinitionBase, IEmbeddableDefinition
            where TOb : MyObjectBuilder_DefinitionBase, IDefinitionReferOrEmbed<TDef>
        {
            var embeddedIn = (owner as IEmbeddableDefinition)?.EmbeddedIn ?? owner;
            MyObjectBuilderType type = ob.GetType();
            ob.Id = new SerializableDefinitionId(type, string.IsNullOrEmpty(ob.SubtypeName)
                ? $"{type.ShortName}_{ob.GetHashCode():X8}_in_{embeddedIn.Id.SubtypeName}"
                : $"{type.ShortName}_{ob.SubtypeName}_in_{embeddedIn.Id.SubtypeName}");

            var instance = MyDefinitionFactory.Get().CreateInstance<TDef>(ob.GetType());
            if (instance == null)
                throw new Exception($"Failed to created embedded definition {typeof(TDef)} from object builder {ob.GetType()}.");
            instance.EmbeddedIn = embeddedIn;
            var prevCtx = DefinitionLogHelper.ExposedLog.Context;
            try
            {
                instance.Init(ob, owner.Package);
                (instance as ILazyInitDefinition)?.LazyInit();
            }
            finally
            {
                DefinitionLogHelper.ExposedLog.Context = prevCtx;
            }

            return instance;
        }

        public static TDef Refer<TDef>(this IDefinitionReference<TDef> reference, MyDefinitionBase owner) where TDef : MyDefinitionBase, IEmbeddableDefinition
        {
            var res = MyDefinitionManager.Get<TDef>(reference.SubtypeName);
            if (res == null)
                DefinitionLogHelper.ExposedLog.Warning($"Failed to find {typeof(TDef).Name} {reference.SubtypeName} for {owner.Id}");
            return res;
        }
    }
}