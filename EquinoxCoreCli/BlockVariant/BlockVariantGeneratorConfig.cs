using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Def;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace Equinox76561198048419394.Core.Cli.BlockVariant
{
    [XmlRoot(nameof(BlockVariantGeneratorConfig))]
    public class BlockVariantGeneratorConfig
    {
        [XmlElement]
        public string VariantName;

        [XmlElement]
        public string OutputDirectory;

        [XmlArrayItem("Change")]
        [XmlArray("MaterialTranslations")]
        public MyObjectBuilder_EquiModifierChangeMaterialDefinition.MaterialModifier[] Changes;

        [XmlElement("ContentRoot")]
        public List<string> ContentRoots;

        [XmlElement("DefinitionToTranslate")]
        public List<SerializableDefinitionId> DefinitionsToTranslate;



        [XmlArrayItem("Translation")]
        [XmlArray("DefinitionTranslations")]
        public List<Translation<DefinitionTagId>> TranslationsSerialized;
        private Dictionary<MyDefinitionId, MyDefinitionId> _translations;
        [XmlIgnore]
        public Dictionary<MyDefinitionId, MyDefinitionId> Translations
        {
            get
            {
                if (_translations != null)
                    return _translations;
                _translations = new Dictionary<MyDefinitionId, MyDefinitionId>();
                if (Translations == null)
                    return _translations;
                foreach (var k in TranslationsSerialized)
                    _translations[k.From] = k.To;
                return _translations;
            }
        }
        
        [XmlArrayItem("Translation")]
        [XmlArray("AssetTranslations")]
        public List<Translation<string>> AssetTranslationsSerialized;
        private Dictionary<string, string> _assetTranslations;
        [XmlIgnore]
        public Dictionary<string, string> AssetTranslations
        {
            get
            {
                if (_assetTranslations != null)
                    return _assetTranslations;
                _assetTranslations = new Dictionary<string, string>();
                if (Translations == null)
                    return _assetTranslations;
                foreach (var k in AssetTranslationsSerialized)
                    _assetTranslations[k.From] = k.To;
                return _assetTranslations;
            }
        }


        public struct Translation<T>
        {
            public T From;
            public T To;
        }
    }
}