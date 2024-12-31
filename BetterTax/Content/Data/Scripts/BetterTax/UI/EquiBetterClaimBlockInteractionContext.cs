using System.Xml.Serialization;
using Medieval.Entities.Components;
using Medieval.Entities.Components.Planet;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.Contexts;
using ObjectBuilders.GUI;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.BetterTax.UI
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_EquiBetterClaimBlockInteractionContext))]
    public class EquiBetterClaimBlockInteractionContext : MyClaimBlockInteractionContext
    {
        private BetterTaxesContext _taxes;

        public override void Init(object[] contextParams)
        {
            base.Init(contextParams);
            _taxes = new BetterTaxesContext(
                (contextParams[0] as MyEntityComponent)?.Entity,
                (MyPlanetAreaOwnershipComponent)contextParams[1],
                AreaId,
                MyPlayers.Static.GetControllingPlayer(MySession.Static.PlayerEntity)?.Identity,
                m_dataSources,
                MySession.Static?.PlayerEntity?.GetInventory());
        }

        public void PayBetterTaxes() => _taxes.PayBetterTaxes();

        public override void Close()
        {
            base.Close();
            _taxes.Close();
            _taxes = null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterClaimBlockInteractionContext : MyObjectBuilder_ClaimBlockInteractionContext
    {
    }
}