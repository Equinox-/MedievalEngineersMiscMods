using System;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public struct StackArray<T>
    {
        /// <summary>
        /// Maximum elements this object can store on the stack
        /// </summary>
        public const int MaxStackSize = 8;
        
        private T _v0, _v1, _v2, _v3, _v4, _v5, _v6, _v7;
        private T[] _vExtra;

        public void RemoveHeapStorage()
        {
            _vExtra = null;
        }

        public static ref T RefInternal(ref StackArray<T> array, int idx, bool allocate = false)
        {
            switch (idx)
            {
                case 0:
                    return ref array._v0;
                case 1:
                    return ref array._v1;
                case 2:
                    return ref array._v2;
                case 3:
                    return ref array._v3;
                case 4:
                    return ref array._v4;
                case 5:
                    return ref array._v5;
                case 6:
                    return ref array._v6;
                case 7:
                    return ref array._v7;
                default:
                    var extIdx = idx - MaxStackSize;
                    if (allocate && (array._vExtra == null || extIdx >= array._vExtra.Length))
                    {
                        var capacity = Math.Max(8, MathHelper.GetNearestBiggerPowerOfTwo(extIdx + 1));
                        Array.Resize(ref array._vExtra, capacity);
                    }

                    return ref array._vExtra[extIdx];
            }
        }
        
        public T this[int idx]
        {
            get => this.Ref(idx);
            set => this.Ref(idx, true) = value;
        }
    }

    public static class StackArrayExt
    {
        public static ref T Ref<T>(this ref StackArray<T> array, int idx, bool allocate = false) => ref StackArray<T>.RefInternal(ref array, idx, allocate);
        public static ref T Ref<T>(this ref StackArray<T> array, uint idx, bool allocate = false) => ref StackArray<T>.RefInternal(ref array, (int) idx, allocate);
    }
}