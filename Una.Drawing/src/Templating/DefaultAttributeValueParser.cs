using Dalamud.Game.Text;
using Dalamud.Interface;
using System.Text.RegularExpressions;

namespace Una.Drawing;

internal partial class DefaultAttributeValueParser : IUdtAttributeValueParser
{
    public string Parse(string value)
    {
        while (SeIconCharRegex.IsMatch(value)) {
            Match  match = SeIconCharRegex.Match(value);
            string icon  = match.Groups[1].Value;

            value = value.Replace(
                match.Value,
                Enum.TryParse<SeIconChar>(icon, true, out var se) ? se.ToIconChar().ToString() : "?"
            );
        }

        while (FontAwesomeRegex.IsMatch(value)) {
            Match  match = FontAwesomeRegex.Match(value);
            string icon  = match.Groups[1].Value;
            
            value = value.Replace(
                match.Value,
                Enum.TryParse<FontAwesomeIcon>(icon, true, out var fa) ? fa.ToIconChar().ToString() : "?"
            );
        }
        
        return value;
    }

    public void Dispose() { }

    [GeneratedRegex(@"SeIcon\(([A-Za-z0-9]+)\)")]
    private static partial Regex SeIconCharRegex { get; }

    [GeneratedRegex(@"FA\(([A-Za-z0-9]+)\)")]
    private static partial Regex FontAwesomeRegex { get; }
}
