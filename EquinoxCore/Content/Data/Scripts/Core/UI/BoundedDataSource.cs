using System;
using Medieval.GUI.ContextMenu.DataSources;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    public interface IBoundedSingleValueDataSource<T> : IMySingleValueDataSource<T>
    {
        T Default { get; }
        T Min { get; }
        T Max { get; }
    }

    public interface IBoundedArrayDataSource<T> : IMyArrayDataSource<T>
    {
        T GetDefault(int index);
        T GetMin(int index);
        T GetMax(int index);
    }

    public sealed class SimpleBoundedDataSource<T> : IBoundedSingleValueDataSource<T>
    {
        private readonly Func<T> _getter;
        private readonly Action<T> _setter;

        public SimpleBoundedDataSource(T min, T @default, T max, Func<T> getter, Action<T> setter)
        {
            _getter = getter;
            _setter = setter;
            Min = min;
            Default = @default;
            Max = max;
        }

        public void Close()
        {
        }

        public T GetData() => _getter();

        public void SetData(T value) => _setter(value);
        public T Default { get; }
        public T Min { get; }
        public T Max { get; }
    }

    public abstract class VectorBoundedArrayDataSource<TComponent, TVec> : IBoundedArrayDataSource<TComponent>, IBoundedSingleValueDataSource<TVec>
    {
        protected abstract TComponent Read(TVec vec, int index);
        protected abstract void Write(ref TVec vec, int index, TComponent value);


        public TComponent GetData(int index) => Read(GetData(), index);

        public void SetData(int index, TComponent value)
        {
            var val = GetData();
            Write(ref val, index, value);
            SetData(val);
        }

        public abstract int Length { get; }

        public TComponent GetDefault(int index) => Read(Default, index);

        public TComponent GetMin(int index) => Read(Min, index);

        public TComponent GetMax(int index) => Read(Max, index);

        public abstract void Close();

        public abstract TVec GetData();

        public abstract void SetData(TVec value);

        public abstract TVec Default { get; }

        public abstract TVec Min { get; }

        public abstract TVec Max { get; }
    }

    public sealed class Vector3BoundedArrayDataSource : VectorBoundedArrayDataSource<float, Vector3>
    {
        private readonly Func<Vector3> _get;
        private readonly Action<Vector3> _set;

        public Vector3BoundedArrayDataSource(Vector3 min, Vector3 @default, Vector3 max,
            Func<Vector3> get,
            Action<Vector3> set)
        {
            Min = min;
            Default = @default;
            Max = max;
            _get = get;
            _set = set;
        }

        protected override float Read(Vector3 vec, int index) => vec.GetDim(index);

        protected override void Write(ref Vector3 vec, int index, float value) => vec.SetDim(index, value);

        public override int Length => 3;

        public override void Close()
        {
        }

        public override Vector3 GetData() => _get();

        public override void SetData(Vector3 value) => _set(value);

        public override Vector3 Default { get; }
        public override Vector3 Min { get; }
        public override Vector3 Max { get; }
    }
}