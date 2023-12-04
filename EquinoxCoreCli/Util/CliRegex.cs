using System.Text.RegularExpressions;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public readonly struct CliRegex
    {
        public readonly string Pattern;
        public readonly Regex Regex;

        public CliRegex(string pattern)
        {
            Pattern = pattern;
            Regex = new Regex(pattern, RegexOptions.IgnoreCase);
        }

        public static implicit operator string(CliRegex regex) => regex.Pattern;
        public static implicit operator CliRegex(string pattern) => new CliRegex(pattern);
        public static implicit operator Regex(CliRegex regex) => regex.Regex;

        public bool IsMatch(string value) => Regex.IsMatch(value);
    }
}