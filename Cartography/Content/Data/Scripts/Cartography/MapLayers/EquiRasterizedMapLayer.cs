using System;
using Medieval.GameSystems;
using Medieval.GUI.Ingame.Map;
using Medieval.GUI.Ingame.Map.RenderLayers;
using Sandbox.Game.Entities;
using Sandbox.Graphics;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public abstract class EquiRasterizedMapLayer<TArgs> : MyPlanetMapRenderLayerBase where TArgs : IEquatable<TArgs>
    {
        private readonly string _targetTexture;

        private TArgs _renderedArgs;
        private Vector2I _renderedSize;
        private Vector2I _targetTextureSize;
        private byte[] _targetTextureData;
        private byte[] _stencilData;

        protected EquiRasterizedMapLayer()
        {
            _targetTexture = $"__equinox_rasterized_texture_{base.GetHashCode():X}__";
        }

        protected abstract TArgs GetArgs(out bool shouldRender);
        protected abstract void Render(in TArgs args, ref RenderContext ctx);

        public readonly struct StencilAccessor
        {
            public readonly Vector2I Size;
            private readonly byte[] _stencil;
            private readonly int _rowStride;

            public StencilAccessor(Vector2I size, byte[] stencil, int rowStride)
            {
                Size = size;
                _stencil = stencil;
                _rowStride = rowStride;
            }

            public byte ReadStencil(int x, int y) => _stencil[y * _rowStride + x];
        }

        protected virtual void AfterDrawn(in TArgs args, in StencilAccessor stencil)
        {
        }

        public override void Draw(float transitionAlpha)
        {
            var args = GetArgs(out var shouldRender);
            if (!shouldRender)
                return;
            var pos = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(Map.GetPositionAbsoluteTopLeft() + Map.MapOffset).Round();
            var uiSize = MyGuiManager.GetScreenSizeFromNormalizedSize(Map.MapSize).Round() - 1;
            const int pixelSize = 1;
            var size = uiSize / pixelSize;
            var texSize = new Vector2I(MathHelper.GetNearestBiggerPowerOfTwo(size.X), MathHelper.GetNearestBiggerPowerOfTwo(size.Y));
            if (texSize != _targetTextureSize)
            {
                _targetTextureSize = texSize;
                MyRenderProxy.UnloadTexture(_targetTexture);
                MyRenderProxy.CreateGeneratedTexture(_targetTexture, texSize.X, texSize.Y);
                var stencilSize = texSize.X * texSize.Y;
                var neededSize = texSize.X * texSize.Y * 4;
                if (_targetTextureData == null || _targetTexture.Length != neededSize)
                    _targetTextureData = new byte[neededSize];
                if (_stencilData == null || _stencilData.Length != stencilSize)
                    _stencilData = new byte[stencilSize];
            }

            if (_renderedSize != size || !args.Equals(_renderedArgs))
                RenderInternal(in args, size);

            var dest = new RectangleF(pos, uiSize);
            Rectangle? src = new Rectangle(Vector2.Zero, size);
            var origin = Vector2.Zero;
            MyRenderProxy.DrawSprite(_targetTexture, ref dest, true, ref src, Color.White, 0,
                Vector2.UnitX, ref origin, SpriteEffects.None, 0);
            var stencil = new StencilAccessor(_renderedSize, _stencilData, _targetTextureSize.Y);
            AfterDrawn(in args, in stencil);
        }

        public override void OnRemoving()
        {
            base.OnRemoving();
            MyRenderProxy.UnloadTexture(_targetTexture);
            _renderedArgs = default;
            _targetTextureSize = default;
        }

        private void RenderInternal(in TArgs args, Vector2I size)
        {
            var targetData = _targetTextureData;
            var stencilData = _stencilData;
            Fill(targetData, 0);
            Fill(stencilData, 0);
            var context = new RenderContext(targetData, stencilData, size, _targetTextureSize.Y * 4, 4);
            Render(in args, ref context);
            MyRenderProxy.ResetGeneratedTexture(_targetTexture, targetData);
            _renderedArgs = args;
            _renderedSize = size;
        }

        private static void Fill(byte[] data, byte value)
        {
            var cleared = Math.Min(128, data.Length);
            for (var i = 0; i < cleared; i++)
                data[i] = value;
            while (cleared < data.Length)
            {
                var remaining = Math.Min(cleared, data.Length - cleared);
                Array.Copy(data, 0, data, cleared, remaining);
                cleared += remaining;
            }
        }
    }


    public readonly struct SimpleRasterizedLayerArgs : IEquatable<SimpleRasterizedLayerArgs>
    {
        public readonly MyPlanet Planet;
        public readonly int Face;
        public readonly RectangleF Region;

        public SimpleRasterizedLayerArgs(MyPlanet planet, int face, RectangleF region)
        {
            Planet = planet;
            Face = face;
            Region = region;
        }

        public bool Equals(SimpleRasterizedLayerArgs other) => Equals(Planet, other.Planet) && Face == other.Face && Region.Equals(other.Region);

        public override bool Equals(object obj) => obj is SimpleRasterizedLayerArgs other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Planet != null ? Planet.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Face;
                hashCode = (hashCode * 397) ^ Region.GetHashCode();
                return hashCode;
            }
        }
    }

    public abstract class EquiSimpleRasterizedMapLayer : EquiRasterizedMapLayer<SimpleRasterizedLayerArgs>
    {
        protected override SimpleRasterizedLayerArgs GetArgs(out bool shouldRender)
        {
            var planet = Map.Planet;
            shouldRender = true;
            var rectangle = Map.GetEnvironmentMapViewport(out var face);
            return new SimpleRasterizedLayerArgs(planet, face, rectangle);
        }
    }
}