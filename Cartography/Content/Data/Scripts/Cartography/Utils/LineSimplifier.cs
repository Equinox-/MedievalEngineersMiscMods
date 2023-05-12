using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using VRage;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Utils
{
    public static class LineSimplifier
    {
        private interface ISimplifierMath<TVec, TScalar> where TScalar : IComparable<TScalar>
        {
            TScalar DistanceSquared(in TVec a, in TVec b);
            /// <summary>
            /// a * scalar + add
            /// </summary>
            TVec Fma(in TVec a, TScalar scalar, in TVec add);
            /// <summary>
            /// a - b
            /// </summary>
            TVec Subtract(in TVec a, in TVec b);
            TScalar LengthSquared(in TVec a);
            TScalar Dot(in TVec a, in TVec b);
            TScalar ClampToNorm(TScalar val);
            TScalar Div(TScalar a, TScalar b);

            TVec NaN { get; }
            bool IsNaN(in TVec vec);
        }

        public static EqSpan<Vector2> SimplifySequence(EqSpan<Vector2> sequence, float simplificationDistanceSq)
        {
            using (PoolManager.Get(out Stack<MyTuple<int, int>> ranges))
            {
                return SimplifySequence(sequence, simplificationDistanceSq, ranges);
            }
        }

        public static EqSpan<Vector2> SimplifySequence(EqSpan<Vector2> sequence, float simplificationDistanceSq, Stack<MyTuple<int, int>> ranges)
        {
            var math = new Vec2Math();
            SimplifySequenceInternal(sequence, simplificationDistanceSq, ranges, ref math);
            return PruneNans<Vector2, float, Vec2Math>(sequence, ref math);
        }

        public static void SimplifySequenceWithNan(EqSpan<Vector3D> sequence, double simplificationDistanceSq)
        {
            using (PoolManager.Get(out Stack<MyTuple<int, int>> ranges))
            {
                var math = new Vec3DMath();
                SimplifySequenceInternal(sequence, simplificationDistanceSq, ranges, ref math);
            }
        }

        private readonly struct Vec2Math : ISimplifierMath<Vector2, float>
        {
            public float DistanceSquared(in Vector2 a, in Vector2 b) => Vector2.DistanceSquared(a, b);
            public Vector2 Fma(in Vector2 a, float scalar, in Vector2 add) => a * scalar + add;
            public Vector2 Subtract(in Vector2 a, in Vector2 b) => a - b;
            public float LengthSquared(in Vector2 a) => a.LengthSquared();
            public float Dot(in Vector2 a, in Vector2 b) => Vector2.Dot(a, b);
            public float ClampToNorm(float val) => MathHelper.Clamp(val, 0, 1);
            public float Div(float a, float b) => a / b;
            public Vector2 NaN => new Vector2(float.NaN, 0);
            public bool IsNaN(in Vector2 vec) => float.IsNaN(vec.X);
        }

        private readonly struct Vec3DMath : ISimplifierMath<Vector3D, double>
        {
            public double DistanceSquared(in Vector3D a, in Vector3D b) => Vector3D.DistanceSquared(a, b);

            public Vector3D Fma(in Vector3D a, double scalar, in Vector3D add) => a * scalar + add;
            public Vector3D Subtract(in Vector3D a, in Vector3D b) => a - b;
            public double LengthSquared(in Vector3D a) => a.X * a.X + a.Y*a.Y + a.Z * a.Z;
            public double Dot(in Vector3D a, in Vector3D b) => Vector3D.Dot(a, b);
            public double ClampToNorm(double val) => MathHelper.Clamp(val, 0, 1);
            public double Div(double a, double b) => a / b;
            public Vector3D NaN => new Vector3D(double.NaN, 0, 0);
            public bool IsNaN(in Vector3D vec) => double.IsNaN(vec.X);
        }

        private static EqSpan<TVec> PruneNans<TVec, TScalar, TMath>(
            EqSpan<TVec> sequence,
            ref TMath math)
            where TMath : struct, ISimplifierMath<TVec, TScalar>
            where TScalar : IComparable<TScalar>
        {
            var removed = 0;
            for (var i = 0; i < sequence.Length; i++)
            {
                ref var pt = ref sequence[i];
                if (math.IsNaN(pt))
                    removed++;
                else if (removed > 0)
                    sequence[i - removed] = pt;
            }

            return sequence.Slice(0, sequence.Length - removed);
        }

        private static void SimplifySequenceInternal<TVec, TScalar, TMath>(
            EqSpan<TVec> sequence,
            TScalar simplificationDistanceSq,
            Stack<MyTuple<int, int>> ranges,
            ref TMath math)
            where TMath : struct, ISimplifierMath<TVec, TScalar>
            where TScalar : IComparable<TScalar>
        {
            if (sequence.Length <= 1)
                return;
            ranges.Clear();
            if (math.DistanceSquared(sequence[0], sequence[sequence.Length - 1]).CompareTo(simplificationDistanceSq) < 0)
            {
                if (sequence.Length >= 3)
                {
                    var midway = sequence.Length / 2;
                    ranges.Push(MyTuple.Create(0, midway));
                    ranges.Push(MyTuple.Create(midway, sequence.Length - 1));
                }
            }
            else
            {
                ranges.Push(MyTuple.Create(0, sequence.Length - 1));
            }

            while (ranges.Count > 0)
            {
                var tmp = ranges.Pop();
                var left = tmp.Item1;
                var right = tmp.Item2;
                MaxDistSq<TVec, TScalar, TMath>(sequence, left, right, out var maxDist2, out var maxDistIndex, ref math);
                if (maxDist2.CompareTo(simplificationDistanceSq) <= 0)
                {
                    // Acceptable error, clear out inner vertices
                    for (var i = left + 1; i <= right - 1; i++)
                        sequence[i] = math.NaN;
                    continue;
                }

                // Subdivide
                if (left < maxDistIndex - 1)
                    ranges.Push(MyTuple.Create(left, maxDistIndex));
                if (maxDistIndex + 1 < right)
                    ranges.Push(MyTuple.Create(maxDistIndex, right));
            }
        }

        private static void MaxDistSq<TVec, TScalar, TImpl>(EqSpan<TVec> sequence, int left, int right, out TScalar maxDist2, out int maxDistIndex,
            ref TImpl math)
            where TImpl : struct, ISimplifierMath<TVec, TScalar>
            where TScalar : IComparable<TScalar>
        {
            var leftPos = sequence[left];
            var delta = math.Subtract(sequence[right], leftPos);
            var deltaLen2 = math.LengthSquared(delta);
            var first = true;
            maxDist2 = default;
            maxDistIndex = left;
            for (var i = left + 1; i <= right - 1; i++)
            {
                var pos = sequence[i];
                var t = math.ClampToNorm(math.Div(math.Dot(math.Subtract(pos , leftPos), delta), deltaLen2));
                var dist2 = math.DistanceSquared(pos, math.Fma(delta, t, leftPos));
                if (first || dist2.CompareTo(maxDist2) > 0)
                {
                    maxDist2 = dist2;
                    maxDistIndex = i;
                    first = false;
                }
            }
        }
    }
}