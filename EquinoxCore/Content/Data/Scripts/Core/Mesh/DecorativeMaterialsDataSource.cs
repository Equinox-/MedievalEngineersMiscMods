using System;
using Equinox76561198048419394.Core.UI;
using Medieval.GUI.ContextMenu.DataSources;
using Sandbox.Graphics;
using Sandbox.Gui.Controls;
using Sandbox.Gui.Styles;
using Sandbox.Gui.Utility;
using VRage.Collections;
using VRage.ObjectBuilders;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Mesh
{
    public class DecorativeMaterialsDataSource<T> : IMyGridDataSource<IEquiIconGridItem> where T : IEquiIconGridItem
    {
        private readonly ListReader<T> _materials;
        private readonly Func<int> _get;
        private readonly Action<int> _set;

        public DecorativeMaterialsDataSource(ListReader<T> def,
            Func<int> get,
            Action<int> set)
        {
            _materials = def;
            _get = get;
            _set = set;
        }

        public void Close()
        {
        }

        public IEquiIconGridItem GetData(int index) => _materials[index];

        void IMyArrayDataSource<IEquiIconGridItem>.SetData(int index, IEquiIconGridItem value)
        {
        }

        public int Length => _materials.Count;

        public int? SelectedIndex
        {
            get => _get() % Length;
            set
            {
                if (value.HasValue)
                    _set(value.Value);
            }
        }
    }


    [MyItemRendererDescriptor("EquiDecorativeMaterial")]
    internal class DecorativeMaterialRenderer : MyGridItemRendererBase
    {
        public override void Draw(MyGrid.Item item, MyGrid.GridItemState state, RectangleF itemRect, Color colormask, MyStateBase style, float transitionAlpha)
        {
            switch (item.UserData)
            {
                case EquiDecorativeDecalToolDefinition.DecalDef decalDef when decalDef.UiIconUsesUv:
                {
                    if (item.Icons == null) return;
                    var position = itemRect.Position;
                    var size = itemRect.Size;
                    var enabled = item.Enabled && state != MyGrid.GridItemState.Disabled;
                    var color = ApplyColorMaskModifiers(colormask * item.Color, enabled, transitionAlpha);
                    var normalizedPos = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(position);
                    var normalizedSize = MyGuiManager.GetScreenSizeFromNormalizedSize(size);
                    var textureOffset = decalDef.TopLeftUv.ToVector2();
                    var textureSize = decalDef.BottomRightUv.ToVector2() - textureOffset;
                    foreach (var icon in item.Icons)
                        MyRenderProxy.DrawSpriteAtlas(icon, normalizedPos + normalizedSize / 2, textureOffset, textureSize, Vector2.UnitX,
                            Vector2.One, color, normalizedSize / 2);

                    return;
                }
                default:
                    base.Draw(item, state, itemRect, colormask, style, transitionAlpha);
                    return;
            }
        }
    }

    public class MyObjectBuilder_EquiDecorativeMaterial : MyObjectBuilder_Base
    {
    }
}