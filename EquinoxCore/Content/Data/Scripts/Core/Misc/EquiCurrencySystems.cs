using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.Market;
using Sandbox.Game.GameSystems.Chat;
using VRage.Components;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Serialization;
using VRage.Session;

namespace Equinox76561198048419394.Core.Misc
{
    [MySessionComponent(typeof(MyObjectBuilder_EquiCurrencySystems), AlwaysOn = true, AllowAutomaticCreation = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    [MyForwardDependency(typeof(EquiDynamicIconRegistration))]
    public partial class EquiCurrencySystems : MySessionComponent
    {
        [Automatic]
        private readonly MyChatSystem _chat = null;

        private List<EquiCurrencySystemDefinition> _systems;

        private readonly Dictionary<MyDefinitionId, EquiCurrencySystemDefinition> _defaultSystemForItem =
            new Dictionary<MyDefinitionId, EquiCurrencySystemDefinition>();

        protected override void OnLoad()
        {
            base.OnLoad();
            _systems = MyDefinitionManager.GetOfType<EquiCurrencySystemDefinition>()
                .OrderByDescending(x => x.Priority)
                .ToList();
            if (_systems.Count == 0)
                _systems.Add(CreateFallback());

            foreach (var system in _systems)
            foreach (var item in system.CurrencyItems.Values)
                if (!_defaultSystemForItem.ContainsKey(item.Item.Id))
                    _defaultSystemForItem[item.Item.Id] = system;

            foreach (var item in _defaultSystemForItem)
                if (!MyDefinitionManager.Contains<EquiDynamicIconDefinition>(item.Key))
                    MyDefinitionManager.Static.DefinitionSet.AddDefinition(item.Value.GenerateDynamicIconDefinition(item.Key));

            _defaultFromDefinitions = _systems[0];

            _chat?.RegisterChatCommand("/currency", HandleCommand, "Interact with currency systems as an admin");
        }

        private static EquiCurrencySystemDefinition CreateFallback()
        {
            var ob = new MyObjectBuilder_EquiCurrencySystemDefinition
            {
                Id = new MyDefinitionId(typeof(MyObjectBuilder_EquiCurrencySystemDefinition)),
                Items = new List<MyObjectBuilder_EquiCurrencySystemDefinition.CurrencyItem>
                {
                    new MyObjectBuilder_EquiCurrencySystemDefinition.CurrencyItem
                    {
                        Type = "InventoryItem",
                        Subtype = "IngotGold",
                        Value = 1000,
                    },
                }
            };
            var def = new EquiCurrencySystemDefinition();
            def.Init(ob, MyModContext.BaseGame);
            MyDefinitionManager.Static.DefinitionSet.AddDefinition(def);
            return def;
        }

        protected override bool IsSerialized => _defaultOverride != null;

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_EquiCurrencySystems)base.Serialize();
            ob.Default = _defaultOverride;
            return ob;
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var ob = (MyObjectBuilder_EquiCurrencySystems)objectBuilder;
            _defaultOverride = ob.Default;
            _default = null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiCurrencySystems : MyObjectBuilder_SessionComponent
    {
        [XmlElement]
        public SerializableDefinitionId? Default;
    }
}