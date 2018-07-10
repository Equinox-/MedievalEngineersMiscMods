using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;

namespace Equinox76561198048419394.Core.Util
{
    public static class BooleanMath
    {
        public static class Helper<T>
        {
            public static readonly DelEvaluate<T> False = (x) => false;
            public static readonly DelEvaluate<T> True = (x) => true;
        }

        public delegate bool DelEvaluate<in T>(T data);

        public static DelEvaluate<T> And<T>(IEnumerable<DelEvaluate<T>> values)
        {
            var arr = values.ToArray();
            if (arr.Any(x => x == Helper<T>.False))
                return Helper<T>.False;
            if (arr.All(x => x == Helper<T>.True))
                return Helper<T>.True;
            
            switch (arr.Length)
            {
                case 0:
                    return (x) => true;
                case 1:
                    return arr[0];
                default:
                    return (x) =>
                    {
                        foreach (var k in arr)
                            if (!k(x))
                                return false;
                        return true;
                    };
            }
        }

        public static DelEvaluate<T> Or<T>(IEnumerable<DelEvaluate<T>> values)
        {
            var arr = values.ToArray();
            if (arr.All(x => x == Helper<T>.False))
                return Helper<T>.False;
            if (arr.Any(x => x == Helper<T>.True))
                return Helper<T>.True;
            
            switch (arr.Length)
            {
                case 0:
                    return (x) => false;
                case 1:
                    return arr[0];
                default:
                    return (x) =>
                    {
                        foreach (var k in arr)
                            if (k(x))
                                return true;
                        return false;
                    };
            }
        }

        public static DelEvaluate<T> Inverted<T>(this DelEvaluate<T> d)
        {
            if (d == Helper<T>.False)
                return Helper<T>.True;
            if (d == Helper<T>.True)
                return Helper<T>.False;
            return (x) => !d(x);
        }

        public static DelEvaluate<T> Nor<T>(IEnumerable<DelEvaluate<T>> values)
        {
            return Or(values).Inverted();
        }

        public static DelEvaluate<T> Nand<T>(IEnumerable<DelEvaluate<T>> values)
        {
            return And(values).Inverted();
        }
    }
}