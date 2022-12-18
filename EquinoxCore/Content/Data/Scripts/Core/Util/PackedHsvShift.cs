using System;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Util.EqMath;
using ObjectBuilders.Definitions.GUI;
using VRage.Network;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Util
{
    [RpcSerializable]
    public struct PackedHsvShift : IEquatable<PackedHsvShift>
    {
        public ushort H;
        public sbyte S;
        public sbyte V;

        public bool Equals(PackedHsvShift other) => H == other.H && S == other.S && V == other.V;

        public override bool Equals(object obj) => obj is PackedHsvShift other && Equals(other);

        public Color ToRgb(float hue = 0, float saturation = 0, float value = 0.95f, float alpha = 1f)
        {
            var hsv = new Vector3(hue, saturation, value) + this;
            var color = hsv.HSVtoColor();
            color.A = (byte)MathHelper.Clamp(alpha * 255, 0, 255);
            return color;
        }  

        public override int GetHashCode()
        {
            var hashCode = H.GetHashCode();
            hashCode = (hashCode * 397) ^ S.GetHashCode();
            hashCode = (hashCode * 397) ^ V.GetHashCode();
            return hashCode;
        }

        public static implicit operator Vector3(PackedHsvShift hsv) => new Vector3(hsv.H / 360f, hsv.S / 100f, hsv.V / 100f);

        public static implicit operator PackedHsvShift(Vector3 hsv) => new PackedHsvShift
        {
            H = (ushort)MathHelper.Clamp(MiscMath.UnsignedModulo(hsv.X, 1) * 360, 0, 360),
            S = (sbyte)(MathHelper.Clamp(hsv.Y, -1, 1) * 100),
            V = (sbyte)(MathHelper.Clamp(hsv.Z, -1, 1) * 100)
        };

        public static implicit operator ColorDefinitionHSV(PackedHsvShift hsv) => new ColorDefinitionHSV
        {
            H = hsv.H,
            S = hsv.S,
            V = hsv.V
        };

        public static implicit operator PackedHsvShift(ColorDefinitionHSV hsv) => new PackedHsvShift
        {
            H = (ushort)MathHelper.Clamp(MiscMath.UnsignedModulo(hsv.H, 360), 0, 360),
            S = (sbyte)MathHelper.Clamp(hsv.S, -100, 100),
            V = (sbyte)MathHelper.Clamp(hsv.V, -100, 100)
        };

        public override string ToString() => ModifierDataColor.Serialize(this);
    }
}