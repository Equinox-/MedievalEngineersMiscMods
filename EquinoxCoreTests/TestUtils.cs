using System.Collections.Generic;
using System.IO;
using Equinox76561198048419394.Core.ModelGenerator.ModelIO;

namespace EquinoxCoreTests
{
    public static class TestUtils
    {
        public const string ContentRoot = @"C:\Program Files (x86)\Steam\steamapps\common\MedievalEngineers\Content";

        public static Dictionary<string, object> ReadModel(string name)
        {
            var import = new ModelImporter();
            import.ImportData(new BinaryReader(File.Open(Path.Combine(TestUtils.ContentRoot, name), FileMode.Open)));
            var tags = import.GetTagData();
            return tags;
        }
    }
}