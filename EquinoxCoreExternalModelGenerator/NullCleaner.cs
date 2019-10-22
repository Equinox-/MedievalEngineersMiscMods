using System;
using System.Linq;
using System.Xml.Linq;

namespace Equinox76561198048419394.Core.ModelCreator
{
    public static class NullCleaner
    {
        public static void Clean(XContainer container)
        {
            if (container is XElement element)
            {
                if (!element.Nodes().Any() && string.IsNullOrEmpty(element.Value))
                {
                    if (element.Attributes().Any(x => x.Name.LocalName == "nil" && x.Name.NamespaceName.EndsWith("XMLSchema-instance", StringComparison.OrdinalIgnoreCase)))
                    {
                        container.Remove();
                        return;
                    }
                }
            }
            foreach (var child in container.Nodes().ToArray())
            {
                if (child is XContainer containerChild)
                    Clean(containerChild);
            }
        }
    }
}