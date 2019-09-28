using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entity.Block;
using VRage.Entity.EntityComponents;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.Session;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Modifiers.Storage
{
    public static class EquiModifierOutputHelpers
    {
        private static bool IsDedicated => ((IMyUtilities) MyAPIUtilities.Static).IsDedicated;

        public static void Apply(in ModifierOutput modifier, MyEntity target)
        {
            var modelComp = target.Get<MyModelComponent>();
            var render = target.Get<MyRenderComponentBase>();
            if (modelComp == null)
                return;

            var model = modifier.Model;
            if (modifier.MaterialEditsBuilder != null && !IsDedicated)
                model = MySession.Static.Components.Get<DerivedModelManager>().CreateModel(model, modifier.MaterialEditsBuilder);

            var modelData = MyModels.GetModel(model);
            if (modelData != null && modelComp.Model != modelData)
                modelComp.SetModel(modelData);

            if (IsDedicated || render == null)
                return;
            if (modifier.ColorMask.HasValue)
            {
                render.EnableColorMaskHsv = true;
                modelComp.ColorMask = modifier.ColorMask.Value;
            }
            else
            {
                render.EnableColorMaskHsv = false;
            }
        }

        public static void Apply(in ModifierOutput modifier, MyBlock block, MyGridDataComponent gridData, MyRenderComponentGrid gridRender)
        {
            var model = modifier.Model;
            if (modifier.MaterialEditsBuilder != null && !((IMyUtilities) MyAPIUtilities.Static).IsDedicated)
                model = MySession.Static.Components.Get<DerivedModelManager>().CreateModel(model, modifier.MaterialEditsBuilder);

            var modelData = MyModels.GetModelOnlyData(model);
            if (modelData != null && block.Model != modelData)
                gridData.ChangeModel(block, modelData);
            
            if (IsDedicated || gridRender == null)
                return;
            var colorMask = modifier.ColorMask ?? Vector3.Zero;
            foreach (var renderable in gridRender.GetBlockRenderObjectIDs(block.Id))
                MyRenderProxy.UpdateRenderEntity(renderable, null, colorMask);
        }
    }
}