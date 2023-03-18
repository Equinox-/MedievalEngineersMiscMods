using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using VRage;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Utils
{
    public static class LineSimplifier
    {
        public static EqSpan<Vector2> SimplifySequence(EqSpan<Vector2> sequence, float simplificationDistanceSq)
        {
            using (PoolManager.Get(out Stack<MyTuple<int, int>> ranges))
            {
                return SimplifySequence(sequence, simplificationDistanceSq, ranges);
            }
        }

        public static EqSpan<Vector2> SimplifySequence(EqSpan<Vector2> sequence, float simplificationDistanceSq, Stack<MyTuple<int, int>> ranges)
        {
            if (sequence.Length <= 1)
                return sequence;
            ranges.Clear();
            if (Vector2.DistanceSquared(sequence[0], sequence[sequence.Length - 1]) < simplificationDistanceSq)
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
                MaxDistSq(sequence, left, right, out var maxDist2, out var maxDistIndex);
                if (maxDist2 <= simplificationDistanceSq)
                {
                    // Acceptable error, clear out inner vertices
                    for (var i = left + 1; i <= right - 1; i++)
                        sequence[i] = new Vector2(float.NaN, float.NaN);
                    continue;
                }

                // Subdivide
                if (left < maxDistIndex - 1)
                    ranges.Push(MyTuple.Create(left, maxDistIndex));
                if (maxDistIndex + 1 < right)
                    ranges.Push(MyTuple.Create(maxDistIndex, right));
            }

            var removed = 0;
            for (var i = 0; i < sequence.Length; i++)
            {
                ref var pt = ref sequence[i];
                if (float.IsNaN(pt.X))
                    removed++;
                else if (removed > 0)
                    sequence[i - removed] = pt;
            }

            return sequence.Slice(0, sequence.Length - removed);
        }

        private static void MaxDistSq(EqSpan<Vector2> sequence, int left, int right, out float maxDist2, out int maxDistIndex)
        {
            var leftPos = sequence[left];
            var delta = sequence[right] - leftPos;
            var deltaLen2 = delta.LengthSquared();
            maxDist2 = -1;
            maxDistIndex = left;
            for (var i = left + 1; i <= right - 1; i++)
            {
                var pos = sequence[i];
                var t = MathHelper.Clamp(Vector2.Dot(pos - leftPos, delta) / deltaLen2, 0, 1);
                var dist2 = Vector2.DistanceSquared(pos, leftPos + t * delta);
                if (dist2 > maxDist2)
                {
                    maxDist2 = dist2;
                    maxDistIndex = i;
                }
            }
        }
    }
}