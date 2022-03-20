using System;
using System.Diagnostics.Contracts;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public static class CatenarySolver
    {
        public static float CatenaryValue(float a, float x) => a * (float)Math.Cosh(x / a);
        public static float CatenaryDerivative(float a, float x) => (float)Math.Sinh(x / a);

        public static bool TrySolve(
            float horizontalDistance,
            float heightIncrease, float catenaryLength, out CatenaryEquation equation)
        {
            equation = default;
            var diffHeightLength2 = catenaryLength * catenaryLength - heightIncrease * heightIncrease;
            if (diffHeightLength2 <= 1e-3f) return false;
            var solveB = new CatenarySolveParam(horizontalDistance, diffHeightLength2);
            var b = RootSolver.SolveRoot(solveB.SearchY / 3, in solveB);
            if (!b.HasValue) return false;

            var a = b.Value * horizontalDistance;
            var solveXOffset = new CatenaryOffsetFinder(a, horizontalDistance, heightIncrease);
            var xOffset = RootSolver.SolveRoot(0, in solveXOffset);
            if (!xOffset.HasValue)
            {
                RootSolver.SolveRoot(0, in solveXOffset);
                return false;
            }
            equation = new CatenaryEquation(a, xOffset.Value, -CatenaryValue(a, xOffset.Value));
            return true;
        }
        
        public readonly struct CatenaryEquation
        {
            private readonly float _a;
            private readonly float _xOffset;
            private readonly float _yOffset;

            public CatenaryEquation(float a, float xOffset, float yOffset)
            {
                _a = a;
                _xOffset = xOffset;
                _yOffset = yOffset;
            }

            [Pure]
            public float Evaluate(float x) => CatenaryValue(_a, _xOffset + x) + _yOffset;
        }
        
        private readonly struct CatenarySolveParam : RootSolver.ICurveWithDerivative
        {
            internal readonly float SearchY;

            private static float InvSqrt(float v) => 1 / (float)Math.Sqrt(v);

            public CatenarySolveParam(float h, float diffHeightLength2)
            {
                SearchY = InvSqrt((float)Math.Sqrt(diffHeightLength2) / h - 1);
            }

            public void Compute(float t, out float value, out float derivative)
            {
                var b2 = 2 * t;
                var invB2 = 1 / b2;
                var cosH = (float)Math.Cosh(invB2);
                var sinH = (float)Math.Sinh(invB2);

                var inner = b2 * sinH - 1;
                var invInner = InvSqrt(inner);
                value = invInner - SearchY;
                derivative = (invB2 * cosH - sinH) * invInner / inner;
            }
        }

        private readonly struct CatenaryOffsetFinder : RootSolver.ICurveWithDerivative
        {
            private readonly float _a;
            private readonly float _h;
            private readonly float _v;

            /// <param name="a">a value of catenary equation</param>
            /// <param name="h">horizontal spacing of points</param>
            /// <param name="v">increase in height from start to end</param>
            public CatenaryOffsetFinder(float a, float h, float v)
            {
                _a = a;
                _h = h;
                _v = v;
            }

            public void Compute(float t, out float value, out float derivative)
            {
                value = CatenaryValue(_a, t + _h) - CatenaryValue(_a, t) - _v;
                derivative = CatenaryDerivative(_a, t + _h) - CatenaryDerivative(_a, t);
            }
        }
    }
}