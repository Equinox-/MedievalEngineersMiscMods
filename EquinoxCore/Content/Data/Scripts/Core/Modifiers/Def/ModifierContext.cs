using System.Linq;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.Block;
using Medieval.Entities.Components.Grid;
using VRage.Components.Entity.CubeGrid;
using VRage.Definitions.Components.Entity;
using VRage.Entity.Block;
using VRage.Game.Entity;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    public struct ModifierContext
    {
        private readonly object _a;
        private readonly object _b;

        public ModifierContext(MyEntity e, InterningBag<EquiModifierBaseDefinition> modifiers)
        {
            _a = e;
            _b = null;
            OriginalModel = e?.Definition?.Get<MyModelComponentDefinition>()?.Model;
            Modifiers = modifiers;
        }

        public ModifierContext(MyGridDataComponent data, MyBlock block, InterningBag<EquiModifierBaseDefinition> modifiers)
        {
            _a = data;
            _b = block;
            OriginalModel = GetOriginalBlockModel(data, block);
            Modifiers = modifiers;
        }

        public MyGridDataComponent GridData => _a as MyGridDataComponent;
        public MyBlock Block => _b as MyBlock;
        public MyEntity Entity => _a as MyEntity;

        public readonly string OriginalModel;
        public readonly InterningBag<EquiModifierBaseDefinition> Modifiers;

        public Vector3D? Position => GridData != null && Block != null ? GridData.GetBlockWorldBounds(Block).Center : Entity?.PositionComp?.WorldVolume.Center;

        private static string GetOriginalBlockModel(MyGridDataComponent gridData, MyBlock block)
        {
            var buildableDef = block.Definition as MyBuildableBlockDefinition;
            if (buildableDef == null)
                return block.Definition.Model;
            var currentState = gridData.Container?.Get<MyGridBuildingComponent>()?.GetBlockState(block.Id);
            if (currentState == null)
                return block.Definition.Model;

            var buildProgress = (float) currentState.BuildIntegrity / currentState.MaxIntegrity;
            for (var i = buildableDef.BuildProgressModels.Count - 1; i >= 0; i--)
            {
                var model = buildableDef.BuildProgressModels[i];
                if (buildProgress <= model.UpperBound)
                    return model.Model;
            }

            return block.Definition.Model;
        }

        public string GetCurrentModel() => Entity?.Model?.AssetName ?? Block?.Model?.AssetName;

        public override string ToString()
        {
            return $"a:{_a} b:{_b} model:{OriginalModel} modifiers:[{string.Join(", ", Modifiers.Select(x => x.Id))}]";
        }
    }
}