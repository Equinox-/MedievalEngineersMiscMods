using System;
using System.Collections;
using System.Collections.Generic;
using VRage.Library.Collections;
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
        
        public T this[int idx]
        {
            get
            {
                switch (idx)
                {
                    case 0:
                        return _v0;
                    case 1:
                        return _v1;
                    case 2:
                        return _v2;
                    case 3:
                        return _v3;
                    case 4:
                        return _v4;
                    case 5:
                        return _v5;
                    case 6:
                        return _v6;
                    case 7:
                        return _v7;
                    default:
                        return _vExtra[idx - MaxStackSize];
                }
            }
            set
            {
                switch (idx)
                {
                    case 0:
                        _v0 = value;
                        return;
                    case 1:
                        _v1 = value;
                        return;
                    case 2:
                        _v2 = value;
                        return;
                    case 3:
                        _v3 = value;
                        return;
                    case 4:
                        _v4 = value;
                        return;
                    case 5:
                        _v5 = value;
                        return;
                    case 6:
                        _v6 = value;
                        return;
                    case 7:
                        _v7 = value;
                        return;
                    default:
                    {
                        var extIdx = idx - MaxStackSize;
                        if (_vExtra == null || extIdx >= _vExtra.Length)
                        {
                            var capacity = Math.Max(8, MathHelper.GetNearestBiggerPowerOfTwo(extIdx + 1));
                            if (_vExtra != null)
                                Array.Resize(ref _vExtra, capacity);
                            else
                                _vExtra = new T[capacity];
                        }

                        _vExtra[extIdx] = value;
                        return;
                    }
                }
            }
        }
    }
}