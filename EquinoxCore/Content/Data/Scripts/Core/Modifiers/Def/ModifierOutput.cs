using System;
using Equinox76561198048419394.Core.ModelGenerator;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    public struct ModifierOutput : IDisposable
    {
        public string Model;
        public MaterialEditsBuilder MaterialEditsBuilder;
        public Vector3? ColorMaskHsv;

        public override string ToString()
        {
            return $"{nameof(Model)}: {Model}, Materials: {MaterialEditsBuilder}, Color: {ColorMaskHsv}";
        }

        public void Dispose()
        {
            MaterialEditsBuilder?.Dispose();
        }
    }
}