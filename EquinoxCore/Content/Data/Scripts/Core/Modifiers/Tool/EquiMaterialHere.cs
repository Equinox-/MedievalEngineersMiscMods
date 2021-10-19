using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Harvest;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Medieval.GameSystems;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Block;
using VRage.Components.Entity;
using VRage.Components.Entity.CubeGrid;
using VRage.Engine;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.GUI.Crosshair;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRage.Systems;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Modifiers.Tool
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiMaterialHereBehaviorDefinition))]
    public class EquiMaterialHereBehavior : MyToolBehaviorBase
    {
        private EquiMaterialHereBehaviorDefinition _definition;

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiMaterialHereBehaviorDefinition) definition;
        }

        protected override bool ValidateTarget()
        {
            return true;
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            return true;
        }

        protected override void Hit()
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            var block = Target.Block;
            var grid = Target.Entity?.Get<MyBlockComponent>()?.GridData ?? Target.Entity?.Get<MyGridDataComponent>();
            MatrixD matrix = default;
            string asset = null;
            if (block != null && grid != null)
            {
                matrix = MatrixD.Invert(grid.GetBlockWorldMatrix(block, true));
                asset = block.Model.AssetName;
            } else if (Target.Entity != null)
            {
                matrix = Target.Entity.PositionComp.WorldMatrixInvScaled;
                asset = Target.Entity.Model?.AssetName;
            }
            if (asset == null)
                return;
            var bvh = MySession.Static.Components.Get<DerivedModelManager>().GetMaterialBvh(asset);
            var caster = Holder.Get<MyCharacterDetectorComponent>(); 
            var localRay = new Ray((Vector3) Vector3D.Transform(caster.StartPosition, matrix),
                (Vector3) Vector3D.TransformNormal(caster.Direction, matrix));

            if (bvh.RayCast(in localRay, out var section, out var material, out var dist))
                MyAPIGateway.Utilities.ShowNotification($"S={section}, M={material}, D={dist}");
            else
                MyAPIGateway.Utilities.ShowNotification("Nothing");
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiMaterialHereBehaviorDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiMaterialHereBehaviorDefinition : MyToolBehaviorDefinition
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMaterialHereBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
    }
}