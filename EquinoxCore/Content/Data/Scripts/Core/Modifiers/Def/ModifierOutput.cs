using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    public struct ModifierOutput : IDisposable
    {
        public string Model;
        public List<IModifierModelEdit> ModelEdits;
        public Vector3? ColorMaskHsv;

        public override string ToString()
        {
            return $"Model: {Model}, Materials: {ModelEdits?.Count}, Color: {ColorMaskHsv}";
        }

        public void AddModelEdit(IModifierModelEdit edit)
        {
            if (ModelEdits == null) ModelEdits = PoolManager.Get<List<IModifierModelEdit>>();
            ModelEdits.Add(edit);
        }

        public void Reset()
        {
            Model = null;
            ColorMaskHsv = null;
            if (ModelEdits == null) return;
            foreach (var item in ModelEdits)
                item.ReturnToPool();
            ModelEdits.Clear();
        }

        public void Dispose()
        {
            Reset();
            if (ModelEdits != null) PoolManager.Return(ref ModelEdits);
        }
    }

    public interface IModifierModelEdit
    {
        Hashing.Hash128 RuntimeHash { get; }

        void Apply(MaterialEditsBuilder target);

        void ReturnToPool();
    }
}