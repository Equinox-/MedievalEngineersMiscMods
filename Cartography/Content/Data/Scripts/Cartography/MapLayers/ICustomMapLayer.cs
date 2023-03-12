using System;
using Medieval.GUI.Ingame.Map;
using Sandbox.Graphics.GUI;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    [Flags]
    public enum CustomMapLayerVisibility
    {
        None = 0,
        Kingdom = 1,
        Kingdoms = 1,
        Region = 2,
        Regions = 2,
        Both = Kingdom | Region,
    }

    public interface ICustomMapLayer : IMyMapRenderLayer
    {
        MyPlanetMapControl Map { get; }

        MyMapGridView View { get; }
        
        EquiCustomMapLayerDefinition Source { get; }
    }

    public static class CustomMapLayerExt
    {
        public static void BindTooltip(this ICustomMapLayer layer, MyTooltip tooltip)
        {
            EquiCustomMapLayersControl.CustomLayers(layer.Map)?.BindTooltip(layer, tooltip);   
        }

        public static bool IsVisible(this CustomMapLayerVisibility visibility, MyPlanetMapZoomLevel zoom)
        {
            switch (zoom)
            {
                case MyPlanetMapZoomLevel.Kingdom:
                    return (visibility & CustomMapLayerVisibility.Kingdom) != 0;
                case MyPlanetMapZoomLevel.Region:
                    return (visibility & CustomMapLayerVisibility.Region) != 0;
                default:
                    return false;
            }
        }
    }
}