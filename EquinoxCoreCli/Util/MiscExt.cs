using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public static void StopwatchStart(out long start) => start = Stopwatch.GetTimestamp();

        public static double StopwatchReadAndReset(ref long start)
        {
            var time = Stopwatch.GetTimestamp();
            var sec = (time - start) / (double)Stopwatch.Frequency;
            start = time;
            return sec;
        }

        /// <summary>
        /// Given a type that is assignable to a parameterized version of genericBase,
        /// get that parameterized base.  If multiple parameterized versions are
        /// assignable, the first one will be returned.
        /// </summary>
        public static bool TryGetGenericBase(this Type type, Type genericBase, out Type parameterizedBase)
        {
            foreach (var interfaceImpl in type.GetInterfaces())
                if (interfaceImpl.IsGenericType && interfaceImpl.GetGenericTypeDefinition() == genericBase)
                {
                    parameterizedBase = interfaceImpl;
                    return true;
                }

            for (var candidate = type; candidate != null; candidate = candidate.BaseType)
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericBase)
                {
                    parameterizedBase = candidate;
                    return true;
                }

            parameterizedBase = default;
            return false;
        }

        /// <summary>
        /// Given a type that is assignable to a parameterized version of genericBase,
        /// get the type argument of that parameterized base.  If multiple parameterized
        /// versions are assignable, the first one will be returned.
        /// </summary>
        public static bool TryGetGenericArgument(this Type type, Type baseGeneric, out Type genericArg, int index = 0)
        {
            if (type.TryGetGenericBase(baseGeneric, out var parameterizedBase))
            {
                genericArg = parameterizedBase.GenericTypeArguments[index];
                return true;
            }

            genericArg = default;
            return false;
        }

        private sealed class ActionDisposable : IDisposable
        {
            private readonly Action _dispose;

            public ActionDisposable(Action dispose) => _dispose = dispose;

            public void Dispose() => _dispose();
        }
    }
}