using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public static class NullCleaner
    {
        private const string MatchAny = "__MATCH_ANY__";

        private static readonly Dictionary<string, string> TimeDefinitionSuppression = new Dictionary<string, string>()
        {
            ["Days"] = "0",
            ["Hours"] = "0",
            ["Minutes"] = "0",
            ["Seconds"] = "0",
            ["Milliseconds"] = "0",
            ["Value"] = MatchAny
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Suppressed = new Dictionary<string, Dictionary<string, string>>
        {
            ["Definition"] = new Dictionary<string, string>
            {
                ["Abstract"] = "false",
                ["Enabled"] = "true",
                ["Merge"] = "Overwrite"
            },
            ["TimeToNextStep"] = TimeDefinitionSuppression,
            ["GrowthStep"] = new Dictionary<string, string> { ["TimeToNextStepInHours"] = MatchAny },
        };

        public static void Clean(XContainer container)
        {
            Dictionary<string, string> suppressed = null;
            if (container is XElement element)
            {
                Suppressed.TryGetValue(element.Name.LocalName, out suppressed);
                if (suppressed != null)
                {
                    foreach (var remove in element.Attributes()
                                 .Where(x =>
                                 {
                                     var filter = suppressed.GetValueOrDefault(x.Name.LocalName);
                                     return filter == MatchAny || filter == x.Value;
                                 })
                                 .ToList())
                        remove.Remove();
                }

                if (!element.Nodes().Any() && string.IsNullOrEmpty(element.Value))
                {
                    if (element.Attributes().Any(x =>
                            x.Name.LocalName == "nil" && x.Name.NamespaceName.EndsWith("XMLSchema-instance", StringComparison.OrdinalIgnoreCase))
                        || !element.Attributes().Any())
                    {
                        container.Remove();
                        return;
                    }
                }
            }

            foreach (var child in container.Nodes().ToArray())
            {
                if (suppressed != null
                    && child is XElement childElement
                    && !childElement.Attributes().Any()
                    && suppressed.TryGetValue(childElement.Name.LocalName, out var filter)
                    && (filter == MatchAny || filter == childElement.Value))
                {
                    child.Remove();
                    continue;
                }

                if (child is XContainer containerChild)
                    Clean(containerChild);
            }
        }
    }
}