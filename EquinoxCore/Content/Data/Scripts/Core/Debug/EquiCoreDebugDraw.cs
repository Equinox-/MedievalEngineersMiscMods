using System.Collections.Generic;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.Mesh;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Sandbox.Game.Gui;
using VRage.Components;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Debug
{
    [MySessionComponent]
    public class EquiCoreDebugDrawRegistration : ModDebugScreenComponent
    {
        protected override IEnumerable<DebugScreen> ScreensInternal => new[]
        {
            CreateDebugScreen<EquiCoreDebugDraw>("Equinox Core Debug Draw"),
            CreateDebugScreen<FacadeEditorDebug>("Facade Editor"),
            CreateDebugScreen<EquiInvertedVisualInventoryDebug>("Inverted Visual Inventory"),
        };
    }

    public class EquiCoreDebugDraw : MyGuiScreenDebugBase
    {
        public override string GetFriendlyName() => "EquiCoreDebugDraw";

        public EquiCoreDebugDraw()
        {
            RecreateControls(true);
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);

            m_currentPosition = -m_size.Value / 2.0f + new Vector2(0.02f, 0.10f);
            m_currentPosition.Y += 0.01f;
            m_scale = 0.7f;

            AddCaption("Core debug draw", Color.Yellow.ToVector4());
            AddShareFocusHint();

            AddLabel("Dynamic Mesh", Color.Yellow, 1);
            AddCheckBox("Draw count", () => DebugDraw<EquiDynamicMeshComponent>.Enabled, val => DebugDraw<EquiDynamicMeshComponent>.Enabled = val);
            AddCheckBox("Draw decals", () => EquiDynamicMeshComponent.ShowDecals, val => ChangeDynamicMesh(ref EquiDynamicMeshComponent.ShowDecals, val));
            AddCheckBox("Draw lines", () => EquiDynamicMeshComponent.ShowLines, val => ChangeDynamicMesh(ref EquiDynamicMeshComponent.ShowLines, val));
            AddCheckBox("Draw surfaces", () => EquiDynamicMeshComponent.ShowSurfaces, val => ChangeDynamicMesh(ref EquiDynamicMeshComponent.ShowSurfaces, val));
            AddButton("Recreate", _ => RegenerateDynamicMesh());

            m_currentPosition.Y += 0.01f;
            AddLabel("Modifiers", Color.Yellow, 1);
            AddCheckBox("Draw count", () => DebugDraw<EquiGridModifierComponent>.Enabled, val => DebugDraw<EquiGridModifierComponent>.Enabled = val);
        }

        private static void ChangeDynamicMesh(ref bool target, bool value)
        {
            if (target == value) return;
            target = value;
            RegenerateDynamicMesh();
        }

        private static void RegenerateDynamicMesh()
        {
            foreach (var comp in ComponentDebug.GetAll<EquiDynamicMeshComponent>())
                comp.DebugMarkAllDirty();
        }
    }
}