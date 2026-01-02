using VRage.Components;
using VRage.Models;
using VRage.Session;
using VRageRender;

namespace Equinox76561198048419394.DisablePreloading
{
    [MySessionComponent(AlwaysOn = true)]
    [MyForwardDependency(typeof(MyModelDestructionData))]
    public class EquiDisablePreloading : MySessionComponent
    {
        protected override void OnLoad()
        {
            MyModelDestructionData.PreloadAssets = false;
            MyRenderProxy.PreloadMaterials(MyModelDestructionData.PreloadMaterialsAsset);
        }
    }
}