using System;
using System.Diagnostics.Contracts;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public static class RootSolver
    {
        public interface ICurveWithDerivative
        {
            [Pure]
            void Compute(float t, out float value, out float derivative);
        }

        public static float? SolveRoot<T>(float guess, in T curve, float eps = 1e-4f, int limit = 100) where T : struct, ICurveWithDerivative
        {
            for (var i = 0; i < limit; i++)
            {
                curve.Compute(guess, out var val, out var derivative);
                if (Math.Abs(val) < eps)
                    return guess;
                guess -= val / derivative;
            }

            return null;
        }
    }
}