using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.Definitions.Crafting;
using Medieval.Entities.Components.Crafting.Recipes;
using Sandbox.Game;
using Sandbox.Game.Entities.Inventory.Constraints;
using VRage.Collections;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Crafting
{
    [MyComponent(typeof(MyObjectBuilder_EquiTemplateBasedRecipeProviderComponent))]
    [MyDefinitionRequired(typeof(EquiTemplateBasedRecipeProviderComponentDefinition))]
    public class EquiTemplateBasedRecipeProviderComponent : MyMultiComponent, IRecipeProvider
    {
        private EquiTemplateBasedRecipeProviderComponentDefinition _definition;
        private MyInventoryBase _templateInventory = null;

        private readonly List<MyCraftingRecipeDefinition> _recipes = new List<MyCraftingRecipeDefinition>();

        #region Init

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (EquiTemplateBasedRecipeProviderComponentDefinition)definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();

            _templateInventory = Container.Get<MyInventoryBase>(_definition.TemplateInventory);
            if (_templateInventory == null)
                this.GetLogger().Error(
                    $"ToolBasedRecipeProviderComponent {_definition.Id} cannot find monitored inventory {_definition.TemplateInventory}!");
            else
            {
                InspectInventory();
                _templateInventory.ContentsChanged += HandleInventoryChanged;
                if (_templateInventory is MyInventory inv && inv.Constraint == null)
                    inv.Constraint = _definition.TemplateConstraint;
            }
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (_templateInventory != null)
            {
                _templateInventory.ContentsChanged -= HandleInventoryChanged;
                _templateInventory = null;
            }

            base.OnBeforeRemovedFromContainer();
        }

        #endregion Init

        #region Private Methods

        private void InspectInventory()
        {
            _recipes.Clear();
            foreach (var item in _templateInventory.Items)
                if (_definition.TemplateItems.TryGetValue(item.DefinitionId, out var templateRecipes))
                    foreach (var recipe in templateRecipes)
                        _recipes.Add(recipe);
            OnRecipesChanged?.Invoke(this);
        }

        #endregion Private Methods

        #region Events

        private void HandleInventoryChanged(MyInventoryBase obj) => InspectInventory();

        #endregion Events

        #region IRecipeProvider

        public int NumberOfRecipes => _recipes.Count;

        public IEnumerable<MyCraftingRecipeDefinition> Recipes => _recipes;

        public IEnumerable<MyInventoryBase> AdditionalInventories => new[] { _templateInventory };

        public event Action<IRecipeProvider> OnRecipesChanged;

        public void CollectRecipesForConstraint(HashSet<MyDefinitionId> prereqSet, HashSet<MyDefinitionId> outputSet)
        {
            foreach (var recipe in _definition.Recipes)
            {
                foreach (var prereq in recipe.Prerequisites)
                    prereqSet?.Add(prereq.Id);
                foreach (var result in recipe.Results)
                    outputSet?.Add(result.Id);
            }
        }

        #endregion IRecipeProvider
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiTemplateBasedRecipeProviderComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiTemplateBasedRecipeProviderComponentDefinition))]
    [MyDependency(typeof(MyCraftingRecipeDefinition))]
    [MyDependency(typeof(MyCraftingCategoryDefinition))]
    public class EquiTemplateBasedRecipeProviderComponentDefinition : MyMultiComponentDefinition
    {
        public MyStringHash TemplateInventory { get; private set; }
        public MyInventoryConstraint TemplateConstraint { get; private set; }

        public ListReader<MyCraftingRecipeDefinition> Recipes { get; private set; }

        public DictionaryReader<MyDefinitionId, ListReader<MyCraftingRecipeDefinition>> TemplateItems { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiTemplateBasedRecipeProviderComponentDefinition)def;

            TemplateInventory = MyStringHash.GetOrCompute(ob.TemplateInventory ?? "TemplateInventory");

            var recipeSet = new HashSet<MyCraftingRecipeDefinition>();

            if (ob.Categories != null)
                foreach (var category in ob.Categories)
                    if (MyDefinitionManager.TryGet<MyCraftingCategoryDefinition>(MyStringHash.GetOrCompute(category), out var cat))
                        foreach (var recipe in cat.Recipes)
                            recipeSet.Add(recipe);
            if (ob.Recipes != null)
                foreach (var recipeId in ob.Recipes)
                    if (MyDefinitionManager.TryGet<MyCraftingRecipeDefinition>(recipeId, out var recipe))
                        recipeSet.Add(recipe);

            Recipes = new List<MyCraftingRecipeDefinition>(recipeSet);

            var templateItemsBuilder = new MyListDictionary<MyDefinitionId, MyCraftingRecipeDefinition>();
            foreach (var recipe in Recipes)
                if ((!MyFinalBuildConstants.IS_OFFICIAL || recipe.Public) && recipe.Enabled)
                    foreach (var output in recipe.Results)
                        templateItemsBuilder.Add(output.Id, recipe);

            var templateItems = new Dictionary<MyDefinitionId, ListReader<MyCraftingRecipeDefinition>>();
            foreach (var kv in templateItemsBuilder)
                templateItems.Add(kv.Key, new List<MyCraftingRecipeDefinition>(kv.Value));
            templateItemsBuilder.Clear();

            TemplateItems = templateItems;
            TemplateConstraint = new RecipeTemplateConstraint(this, ob);
        }

        private sealed class RecipeTemplateConstraint : MyInventoryConstraint
        {
            private readonly DictionaryReader<MyDefinitionId, ListReader<MyCraftingRecipeDefinition>> _valid;

            internal RecipeTemplateConstraint(EquiTemplateBasedRecipeProviderComponentDefinition owner, MyObjectBuilder_EquiTemplateBasedRecipeProviderComponentDefinition ob)
            {
                _valid = owner.TemplateItems;
                Init(ob, owner.Package);
            }

            public override bool Check(MyDefinitionId itemId) => _valid.ContainsKey(itemId);

            public override MyInventoryConstraint Clone() => this;
        }
    }

    /// <summary>
    /// Crafting recipe provider that sets the recipe based on the template items provided in the TemplateInventory, limited
    /// to the recipes listed on the definition.
    /// </summary>
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiTemplateBasedRecipeProviderComponentDefinition : MyObjectBuilder_MultiComponentDefinition
    {
        /// <summary>
        /// SubtypeId of inventory to store template items in.
        /// </summary>
        [XmlElement]
        public string TemplateInventory;

        /// <summary>
        /// Support crafting recipes from this category.
        /// </summary>
        [XmlElement("Category")]
        public string[] Categories;

        /// <summary>
        /// Support crafting these recipes.
        /// </summary>
        [XmlElement("Recipe")]
        public SerializableDefinitionId[] Recipes;
    }
}