using System.Xml.Serialization;
using Medieval.Entities.Components.Planet;
using Medieval.GameSystems;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using ObjectBuilders.GUI;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using VRage.Game.Entity;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.BetterTax.UI
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_EquiPayTaxesInteractionContext))]
    public class EquiPayTaxesInteractionContext : MyContextMenuContext
    {
        private BetterTaxesContext _taxes;

        public override void Init(object[] contextParams)
        {
            var taxEntity = (MyEntity) contextParams[0];
            var payer = (MyEntity) contextParams[1];
            var payerInventory = payer.GetInventory();
            if (payerInventory == null) return;
            var player = MyPlayers.Static.GetControllingPlayer(payer);
            if (player?.Identity == null) return;
            var planet = MyGamePruningStructureSandbox.GetClosestPlanet(taxEntity.GetPosition());
            if (planet == null
                || !planet.Components.TryGet(out MyPlanetAreasComponent areas)
                || !planet.Components.TryGet(out MyPlanetAreaOwnershipComponent ownership)) return;

            _taxes = new BetterTaxesContext(
                taxEntity,
                ownership,
                areas.GetArea(taxEntity.GetPosition()),
                player.Identity,
                m_dataSources,
                payerInventory);
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
    public class MyObjectBuilder_EquiPayTaxesInteractionContext : MyObjectBuilder_ClaimBlockInteractionContext
    {
    }
}