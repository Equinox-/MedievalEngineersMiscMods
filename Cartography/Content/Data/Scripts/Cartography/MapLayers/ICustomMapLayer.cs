using Medieval.GUI.Ingame.Map;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public interface ICustomMapLayer : IMyMapRenderLayer
    {
        MyMapGridView BoundTo { get; }
        
        EquiCustomMapLayerDefinition Source { get; }
    }
}