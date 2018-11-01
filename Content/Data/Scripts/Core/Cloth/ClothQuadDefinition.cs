using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace Equinox76561198048419394.Core.Cloth
{
    public struct ClothQuadDefinition
    {
        public readonly int ResX, ResY;
        public readonly float TensionStrength;
        public readonly float ShearStrength;
        public readonly float StructuralStrength;
        public readonly float Damping;
        public readonly float Mass;

        public readonly Vector3 V00, V01, V10, V11;

        public readonly ImmutablePointRef[] Pins;

        public ClothQuadDefinition(MyObjectBuilder_ClothSquaresComponentDefinition.ClothSquare sq)
        {
            ResX = sq.ResX;
            ResY = sq.ResY;
            V00 = sq.V00;
            V01 = sq.V01;
            V10 = sq.V10;
            V11 = sq.V11;
            var pins = new List<ImmutablePointRef>();
            if (sq.Pins != null)
                foreach (var p in sq.Pins)
                {
                    for (var xo = 0; xo < p.XCount; xo++)
                    for (var yo = 0; yo < p.YCount; yo++)
                        pins.Add(new ImmutablePointRef(p.X + xo, p.Y + yo));
                }

            Pins = pins.ToArray();
            TensionStrength = sq.TensionStrength;
            ShearStrength = sq.ShearStrength;
            StructuralStrength = sq.StructuralStrength;
            Damping = sq.Damping;
            Mass = sq.Mass;
        }
    }
}