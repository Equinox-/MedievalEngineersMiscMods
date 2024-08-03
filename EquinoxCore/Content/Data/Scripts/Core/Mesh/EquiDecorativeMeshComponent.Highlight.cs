using System;
using System.Collections.Generic;
using VRage.Components;
using VRage.Library.Collections;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        private readonly HashSet<FeatureKey> _highlight = new HashSet<FeatureKey>();
        public HighlightToken HighlightFeature(in FeatureKey key) => new HighlightToken(this, in key);

        private void StartHighlight(in FeatureKey key)
        {
            if (!_highlight.Add(key)) return;
            if (_highlight.Count == 1) AddFixedUpdate(RenderHighlight);
        }

        private void StopHighlight(in FeatureKey key)
        {
            if (!_highlight.Remove(key)) return;
            if (_highlight.Count == 0) RemoveFixedUpdate(RenderHighlight);
        }

        [FixedUpdate(false)]
        private void RenderHighlight()
        {
            if (_highlight.Count == 0) return;

            List<FeatureKey> removed = null;
            try
            {
                foreach (var key in _highlight)
                {
                    if (!_features.TryGetValue(key, out var renderData))
                    {
                        if (removed == null) removed = PoolManager.Get<List<FeatureKey>>();
                        removed.Add(key);
                        continue;
                    }

                    ref var feature = ref renderData.Value;
                    if (!feature.RenderId.HasValue) continue;
                    switch (key.Type)
                    {
                        case FeatureType.Decal:
                        case FeatureType.Line:
                        case FeatureType.Surface:
                            _dynamicMesh?.DrawHighlight(feature.RenderId.Value);
                            break;
                        case FeatureType.Model:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
            finally
            {
                if (removed != null)
                {
                    foreach (var rem in removed)
                        _highlight.Remove(rem);
                    PoolManager.Return(ref removed);
                }
            }
        }
        
        public readonly struct HighlightToken : IDisposable, IEquatable<HighlightToken>
        {
            public readonly EquiDecorativeMeshComponent Owner;
            public readonly FeatureKey Key;

            internal HighlightToken(EquiDecorativeMeshComponent owner, in FeatureKey key)
            {
                Owner = owner;
                Key = key;
                owner.StartHighlight(in Key);
            }

            public bool IsValid => Owner != null && Owner._features.ContainsKey(in Key);

            public void Dispose() => Owner?.StopHighlight(in Key);

            public bool Equals(HighlightToken other) => Equals(Owner, other.Owner) && Key.Equals(other.Key);

            public override bool Equals(object obj) => obj is HighlightToken other && Equals(other);

            public override int GetHashCode() => ((Owner != null ? Owner.GetHashCode() : 0) * 397) ^ Key.GetHashCode();
        }
    }
}