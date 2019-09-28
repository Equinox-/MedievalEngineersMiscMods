namespace Equinox76561198048419394.Core.Modifiers.Data
{
    public class ModifierDataLong : IModifierData
    {
        public long Raw;

        public ModifierDataLong(long raw)
        {
            Raw = raw;
        }

        public ModifierDataLong(string data)
        {
            long.TryParse(data, out Raw);
        }

        public string Serialize()
        {
            return Raw.ToString();
        }
    }
}