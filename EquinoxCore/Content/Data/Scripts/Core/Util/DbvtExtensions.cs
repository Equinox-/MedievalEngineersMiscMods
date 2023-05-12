using System.Collections;
using System.Collections.Generic;
using VRage.Collections;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public static class DbvtExtensions
    {
        public struct NearestNodeResult
        {
            private readonly MyDynamicAABBTreeD _tree;
            public readonly int NodeIndex;
            public readonly double DistanceSquared;

            public NearestNodeResult(int idx, double distSq, MyDynamicAABBTreeD tree)
            {
                NodeIndex = idx;
                _tree = tree;
                DistanceSquared = distSq;
            }

            public T GetUserData<T>() => _tree.GetUserData<T>(NodeIndex);
            public BoundingBoxD Bounds => _tree.GetAabb(NodeIndex);
        }

        public static IEnumerator<NearestNodeResult> EquiSortedByDistance(this MyDynamicAABBTreeD tree, Vector3D test,
            double maxDistanceSq = double.PositiveInfinity)
        {
            var query = PoolManager.Get<NearestNodeQuery>();
            query.Init(test, tree, maxDistanceSq);
            return query;
        }

        private class NearestNodeQuery : IEnumerator<NearestNodeResult>
        {
            private Vector3D _vec;
            private MyDynamicAABBTreeD _tree;
            private double _maxDistanceSq;
            private readonly MyBinaryHeap<double, int> _tmp = new MyBinaryHeap<double, int>();

            public void Init(Vector3D v, MyDynamicAABBTreeD tree, double maxDistanceSq)
            {
                _vec = v;
                _tree = tree;
                _maxDistanceSq = maxDistanceSq;
                Reset();
            }

            private void Insert(int node)
            {
                var box = _tree.GetAabb(node);
                Vector3D tmp;
                Vector3D.Clamp(ref _vec, ref box.Min, ref box.Max, out tmp);
                var dist = Vector3D.DistanceSquared(tmp, _vec);
                if (dist <= _maxDistanceSq)
                    _tmp.Insert(node, dist);
            }

            public void Dispose()
            {
                _tmp.Clear();
                _tree = null;
                var tmp = this;
                PoolManager.Return(ref tmp);
            }

            public bool MoveNext()
            {
                while (_tmp.Count > 0)
                {
                    var minKey = _tmp.MinKey();
                    var min = _tmp.RemoveMin();
                    _tree.GetChildren(min, out var child1, out var child2);
                    if (child1 == -1)
                    {
                        Current = new NearestNodeResult(min, minKey, _tree);
                        return true;
                    }

                    Insert(child1);
                    Insert(child2);
                }

                Current = default(NearestNodeResult);
                return false;
            }

            public void Reset()
            {
                _tmp.Clear();
                var rt = _tree.GetRoot();
                if (rt >= 0)
                    Insert(rt);
            }

            public NearestNodeResult Current { get; private set; }

            object IEnumerator.Current => Current;
        }
    }
}