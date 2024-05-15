using System;
using System.Collections.Generic;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public static class MiscExt
    {
        public static void Append<T>(this Span<T> span, ReadOnlySpan<T> other, ref int index)
        {
            other.CopyTo(span.Slice(index));
            index += other.Length;
        }

        public static float Volume(this in OrientedBoundingBox box) => box.HalfExtent.Volume * 8;

        public static float Volume(this in BoundingSphere sphere) => (float)Math.PI * sphere.Radius * sphere.Radius * sphere.Radius * 4 / 3f;

        public static float Volume(this in Capsule capsule)
        {
            var r2 = capsule.Radius * capsule.Radius;
            var cylinder = Vector3.Distance(capsule.P0, capsule.P1) * MathHelper.Pi * r2;
            var sphere = 4 * MathHelper.Pi * r2 * capsule.Radius / 3f;
            return cylinder + sphere;
        }

        public static IDisposable Disposable(Action act) => new ActionDisposable(act);

        public static IDisposable Disposable<T>(this ICollection<T> collection) where T : IDisposable => new ActionDisposable(() =>
        {
            foreach (var item in collection)
                item.Dispose();
        });

        private sealed class ActionDisposable : IDisposable
        {
            private readonly Action _dispose;

            public ActionDisposable(Action dispose) => _dispose = dispose;

            public void Dispose() => _dispose();
        }
    }
}