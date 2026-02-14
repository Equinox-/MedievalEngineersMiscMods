using System;
using Equinox76561198048419394.Cartography.Derived;
using Equinox76561198048419394.Cartography.Derived.Contours;
using Sandbox.Game.Gui;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Cartography
{
    internal class EquiCartographyDebugDraw : MyGuiScreenDebugBase
    {
        public override string GetFriendlyName() => "EquiCartographyDebugDraw";

        public EquiCartographyDebugDraw()
        {
            RecreateControls(true);
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);

            m_currentPosition = -m_size.Value / 2.0f + new Vector2(0.02f, 0.10f);
            m_currentPosition.Y += 0.01f;
            m_scale = 0.7f;

            AddCaption("Cartography debug draw", Color.Yellow.ToVector4());
            AddShareFocusHint();

            AddLabel("Elevation", Color.Yellow, 1);
            AddRasterSizeSlider("Raster Size", () => ElevationData.RasterSize, val => ElevationData.RasterSize = val);
            AddButton("Recreate", _ => MySession.Static?.Components.Get<EquiCartography>()?.InvalidateCachedElevation());

            m_currentPosition.Y += 0.01f;
            AddLabel("Contours", Color.Yellow, 1);
            AddRasterSizeSlider("Raster Size", () => EquiContourCalculator.RasterSize, val => EquiContourCalculator.RasterSize = val);
            AddSlider("Simplification", (float)Math.Sqrt(EquiContourCalculator.SimplificationDistanceSq), 0, 1,
                slider => EquiContourCalculator.SimplificationDistanceSq = slider.Value * slider.Value);
            AddSlider("Fake Depth Bias", EquiContourOverlay.FakeDepthBiasFactor, 0, 1, slider => EquiContourOverlay.FakeDepthBiasFactor = slider.Value);
            AddButton("Recreate", _ => MySession.Static?.Components.Get<EquiCartography>()?.InvalidateCachedContours());
            return;

            void AddRasterSizeSlider(string name, Func<int> get, Action<int> set)
            {
                AddSlider(name, MathHelper.Log2(get()), 4, 10, slider => set(1 << (int)slider.Value)).IntValue = true;
            }
        }
    }
}