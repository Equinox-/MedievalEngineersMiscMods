using System.IO;

namespace Equinox76561198048419394.Core.Util
{
    public interface ISerializer<T>
    {
        void Write(BinaryWriter target, in T value);

        void Read(BinaryReader source, out T value);
    }

    public class VoidSerializer<T> : ISerializer<T>
    {
        public static readonly ISerializer<T> Instance = new VoidSerializer<T>();

        private VoidSerializer()
        {
        }

        public void Write(BinaryWriter target, in T value)
        {
        }

        public void Read(BinaryReader source, out T value)
        {
            value = default;
        }
    }
}