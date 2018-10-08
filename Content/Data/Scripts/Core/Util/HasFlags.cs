using VRage.Components.Session;

namespace Equinox76561198048419394.Core.Util
{
    public static class HasFlags
    {
        public static bool HasFlagEq(this TimesOfDay time, TimesOfDay f)
        {
            return (time & f) != 0;
        }
        public static bool HasFlagEq(this Season time, Season f)
        {
            return (time & f) != 0;
        }
    }
}