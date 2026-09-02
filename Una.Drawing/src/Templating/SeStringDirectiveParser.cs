using Dalamud.Utility;

namespace Una.Drawing;

public class SeStringDirectiveParser : IUdtDirectiveParser
{
    public string Name { get; } = "sestringvalue";
    
    public void Parse(Node node, string value)
    {
        using var rssb = new RentedSeStringBuilder();

        node.NodeValue = rssb.Builder
            .AppendMacroString(value)
            .ToReadOnlySeString();
    }
}
