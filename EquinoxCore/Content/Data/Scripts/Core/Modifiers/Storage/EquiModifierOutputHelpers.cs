using System;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Entity.Render;
using VRage.Entity.Block;
using VRage.Entity.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.Logging;
using VRage.Models;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Modifiers.Storage
{
    [MyComponent]
    public sealed class ForceOldPipelineRenderComponent : MyRenderComponent
    {
        public override RenderFlags GetRenderFlags()
        {
            return base.GetRenderFlags() | RenderFlags.ForceOldPipeline;
        }
    }

    public static class EquiModifierOutputHelpers
    {

        private static NamedLogger _log = new NamedLogger(nameof(EquiModifierOutputHelpers), MyLog.Default);

        private static bool IsDedicated => ((IMyUtilities) MyAPIUtilities.Static).IsDedicated;

        public static void Apply(in ModifierOutput modifier, MyEntity target)
        {
            try
            {
                var modelComp = target.Get<MyModelComponent>();
                var render = target.Get<MyRenderComponentBase>();
                if (modelComp == null || modelComp.Model is MyFracturedCompoundModel)
                    return;

                var forceRenderInit = false;
                if (!IsDedicated)
                {
                    modelComp.ColorMask = modifier.ColorMaskHsv ?? Vector3.Zero;
                    if (render != null && !render.EnableColorMaskHsv)
                        render.EnableColorMaskHsv = true;
#if !VRAGE_VERSION_0_7_4
                    if (modifier.ColorMaskHsv.HasValue
                        && render is MyRenderComponent && !(render is ForceOldPipelineRenderComponent))
                    {
                        // Big hack...
                        target.Components.Remove(render);
                        render = new ForceOldPipelineRenderComponent();
                        target.Render = render;
                        forceRenderInit = true;
                    }
#endif
                }

                var model = modifier.Model;
                if (modifier.MaterialEditsBuilder != null && !IsDedicated)
                    model = MySession.Static.Components.Get<DerivedModelManager>().CreateModel(model, modifier.MaterialEditsBuilder);

                var modelData = MyModels.GetModelOnlyData(model);
                var collisionData = MyModels.GetModelOnlyData(modifier.Model) ?? modelData;
                if (modelData != null && (forceRenderInit ||
                                          (modelComp.Model != modelData || modelComp.ModelCollision != collisionData) &&
                                          !(modelComp.Model is MyFracturedCompoundModel)))
                {
                    modelComp.SetModel(modelData, collisionData);
                    if (render?.RenderObjectIDs != null && render.RenderObjectIDs.Length > 0)
                    {
                        foreach (var renderObj in render.RenderObjectIDs)
                            if (renderObj != MyRenderProxy.RENDER_ID_UNASSIGNED)
                                MyRenderProxy.UpdateHighlightOverlappingModel(renderObj, false);
                        render.RemoveRenderObjects();
                        render.AddRenderObjects();
                        foreach (var renderObj in render.RenderObjectIDs)
                            if (renderObj != MyRenderProxy.RENDER_ID_UNASSIGNED)
                            {
                                MyRenderProxy.UpdateHighlightOverlappingModel(renderObj, false);
                                MyRenderProxy.UpdateLodImmediately(renderObj);
                            }
                    }
                }
                else if (!IsDedicated && render?.RenderObjectIDs != null && render.RenderObjectIDs.Length > 0)
                {
                    foreach (var renderObj in render.RenderObjectIDs)
                        if (renderObj != MyRenderProxy.RENDER_ID_UNASSIGNED)
                            MyRenderProxy.UpdateRenderEntity(renderObj, render.GetDiffuseColor(), modifier.ColorMaskHsv ?? Vector3.Zero);
                }

                if (DebugFlags.Trace(typeof(EquiModifierOutputHelpers)))
                    _log.Info($"Applied modifiers to {target} produced model={model} color={modifier.ColorMaskHsv}");
            }
            catch (Exception ex)
            {
                DebugFlags.MaybeFailFast(nameof(EquiModifierOutputHelpers), $"Failed to apply modifiers to {target}.\nModifiers: {modifier}", ex);
            }
        }

        public static void Apply(in ModifierOutput modifier, MyBlock block, MyGridDataComponent gridData, MyRenderComponentGrid gridRender)
        {
            try
            {
                var model = modifier.Model;
                if (modifier.MaterialEditsBuilder != null && !IsDedicated)
                    model = MySession.Static.Components.Get<DerivedModelManager>().CreateModel(model, modifier.MaterialEditsBuilder);

                var modelData = MyModels.GetModelOnlyData(model);
                if (modelData != null && block.Model != modelData && !(block.Model is MyFracturedCompoundModel))
                    gridData.ChangeModel(block, modelData);

                if (IsDedicated || gridRender == null)
                    return;
                var colorMaskHsv = modifier.ColorMaskHsv ?? Vector3.Zero;
                foreach (var renderable in gridRender.GetBlockRenderObjectIDs(block.Id))
                    MyRenderProxy.UpdateRenderEntity(renderable, null, colorMaskHsv);

                if (DebugFlags.Trace(typeof(EquiModifierOutputHelpers)))
                    _log.Info($"Applied modifiers to {block} on {gridData.Entity} produced model={model} color={modifier.ColorMaskHsv}");
            }
            catch (Exception ex)
            {
                DebugFlags.MaybeFailFast(nameof(EquiModifierOutputHelpers),
                    $"Failed to apply modifiers to {block} on {gridData.Entity}.\nModifiers: {modifier}", ex);
            }
        }
    }
}