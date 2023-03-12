using VRageMath;

namespace Equinox76561198048419394.Cartography.Utils
{
    public interface ICoordinateAccessor<TVector, TValue> where TVector : struct where TValue : struct
    {
        ref TValue Write(ref TVector vector);

        TValue Read(in TVector vector);
    }

    public readonly struct XAccessor : ICoordinateAccessor<Vector2, float>, ICoordinateAccessor<Vector2I, int>
    {
        public ref float Write(ref Vector2 vector) => ref vector.X;
        public float Read(in Vector2 vector) => vector.X;

        public ref int Write(ref Vector2I vector) => ref vector.X;
        public int Read(in Vector2I vector) => vector.X;
    }

    public readonly struct YAccessor : ICoordinateAccessor<Vector2, float>, ICoordinateAccessor<Vector2I, int>
    {
        public ref float Write(ref Vector2 vector) => ref vector.Y;
        public float Read(in Vector2 vector) => vector.Y;

        public ref int Write(ref Vector2I vector) => ref vector.Y;
        public int Read(in Vector2I vector) => vector.Y;
    }
}