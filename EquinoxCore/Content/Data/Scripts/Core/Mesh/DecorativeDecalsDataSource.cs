using Medieval.GUI.ContextMenu.DataSources;

namespace Equinox76561198048419394.Core.Mesh
{
    public class DecorativeDecalsDataSource : IMyGridDataSource<EquiDecorativeDecalToolDefinition.DecalDef>
    {
        private readonly EquiDecorativeDecalToolDefinition _def;

        public DecorativeDecalsDataSource(EquiDecorativeDecalToolDefinition def)
        {
            _def = def;
        }

        public void Close()
        {
        }

        public EquiDecorativeDecalToolDefinition.DecalDef GetData(int index) => _def.SortedDecals[index];

        public void SetData(int index, EquiDecorativeDecalToolDefinition.DecalDef value)
        {
        }

        public int Length => _def.SortedDecals.Count;

        public int? SelectedIndex
        {
            get => DecorativeToolSettings.DecalIndex % Length;
            set
            {
                if (value.HasValue)
                    DecorativeToolSettings.DecalIndex = value.Value;
            }
        }
    }
}