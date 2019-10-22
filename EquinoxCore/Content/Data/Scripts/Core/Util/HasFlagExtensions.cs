using VRage.Components.Session;
using VRage.Game.Entity.UseObject;

namespace Equinox76561198048419394.Core.Util
{
    public static class HasFlagExtensions
    {
        public static bool HasFlags(this TimesOfDay time, TimesOfDay f)
        {
            return (time & f) == f;
        }

        public static bool HasFlags(this Season time, Season f)
        {
            return (time & f) == f;
        }

        public static bool HasFlags(this UseActionEnum e, UseActionEnum f)
        {
            return (e & f) == f;
        }
    }
}