using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Cartography.Data.Cartographic;
using Equinox76561198048419394.Cartography.Data.Framework;
using Equinox76561198048419394.Cartography.Derived;
using Equinox76561198048419394.Cartography.Derived.Contours;
using Equinox76561198048419394.Cartography.MapLayers;
using Medieval.GameSystems;
using Medieval.GUI.Ingame.Map;
using ObjectBuilders.GUI.Map;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Input;
using VRage.Game.ModAPI;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Cartography
{
    [MySessionComponent(AlwaysOn = true)]
    public class EquiCartography : MySessionComponent
    {
        #region Map Overlay Data

        private readonly EquiContourCalculator _contours = new EquiContourCalculator();
        private readonly EquiElevationCalculator _elevation = new EquiElevationCalculator();
        private readonly EquiContourOverlay _contourOverlay = new EquiContourOverlay();

        private readonly MyInputContext _overlaysContext = new MyInputContext("MapOverlays");

        /// <summary>
        /// Gets computed contour data asynchronously.
        /// This will return null until the contour data is computed.
        /// </summary>
        public ContourData GetOrComputeContoursAsync(in ContourArgs args) => _contours.GetOrComputeAsync(in args);

        /// <summary>
        /// Gets computed elevation raster asynchronously.
        /// This will return null until the elevation data is computed.
        /// </summary>
        public ElevationData GetOrComputeElevationAsync(in ElevationArgs args) => _elevation.GetOrComputeAsync(in args);

        private List<EquiContourOptions> _contourOverlayOptions;
        private int _contourOverlayIndex;

        private void ToggleContours()
        {
            if (_contourOverlayOptions == null)
            {
                _contourOverlayOptions = MyDefinitionManager.GetOfType<EquiContourOptions>().Where(x => x.IsOverlay).ToList();
                _contourOverlayOptions.Sort((a, b) => string.Compare(a.Id.SubtypeName, b.Id.SubtypeName, StringComparison.Ordinal));
            }

            _contourOverlayIndex = (_contourOverlayIndex + 1) % (_contourOverlayOptions.Count + 1);
        }

        #endregion

        #region UI Hooks

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            _overlaysContext.RegisterAction(MyStringHash.GetOrCompute("ShowContours"), ToggleContours);
            _overlaysContext.Push();
            MyChatSystem.Static.RegisterChatCommand("/cartography-test", CartographyTest, "Test");
        }

        protected override void OnUnload()
        {
            _overlaysContext.Pop();
            base.OnUnload();
        }

        #endregion

        private bool _hookedMapControls;

        [FixedUpdate]
        public void Render()
        {
            if (_contourOverlayIndex > 0)
                _contourOverlay.RenderOverlay(_contourOverlayOptions[_contourOverlayIndex - 1]);

            if (((IMyUtilities)MyAPIUtilities.Static).IsDedicated
                || MyAPIGateway.Session?.LocalHumanPlayer == null
                || _hookedMapControls) return;
            var mapScreen = Container.Get<MyMapSessionComponent>().MapScreen;
            if (mapScreen != null)
            {
                EquiCustomMapLayersControl.BindTo(mapScreen);
                _hookedMapControls = true;
            }
        }

        private bool CartographyTest(ulong sender, string message, MyChatCommandType handledAsType)
        {
            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender))?.ControlledEntity;
            if (player == null)
                return true;
            var ob = new MyObjectBuilder_EntityBase
            {
                EntityDefinitionId = new SerializableDefinitionId(typeof(MyObjectBuilder_EntityBase), "CartographicDataHost"),
                ComponentContainer = new MyObjectBuilder_ComponentContainer()
            };
            var planet = MyGamePruningStructureSandbox.GetClosestPlanet(player.GetPosition());
            ob.ComponentContainer.AddComponent(new MyObjectBuilder_EquiCartographicData
            {
                Planet = planet.Id.Value,
                Routes = new List<MyObjectBuilder_CartographicRoute>
                {
                    new MyObjectBuilder_CartographicRoute
                    {
                        Id = 123,
                        Name = "Test",
                        Vertices = new List<MyObjectBuilder_CartographicRoute.RouteVertex>
                        {
                            new EquiCartographicRoute.RouteVertex(EquiCartographicLocation.FromWorld(
                                planet, player.GetPosition())).Serialize()
                        }
                    }
                }
            });
            var entity = Container.Get<EquiExternalItemDataManager>().Create(ob);
            var data = entity.Get<EquiCartographicData>();

            player.GetInventory().Add(new EquiCartographyItem()
            {
                DataId = data.DataId,
                Durability = 8000,
                Subtype = MyStringHash.GetOrCompute("Testing"),
                Amount = 1
            }, MyInventoryBase.NewItemParams.AsNewStack);
            return true;
        }
    }
}