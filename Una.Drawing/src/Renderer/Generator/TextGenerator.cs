using System.Linq;
using Una.Drawing.Font;

namespace Una.Drawing.Generator;

internal class TextGenerator : IGenerator
{
    public int RenderOrder => 999;

    public bool Generate(SKCanvas canvas, Node node, Vector2 origin)
    {
        MeasuredText? measurement = node.NodeValueMeasurement;
        if (null == measurement || measurement.Value.LineCount == 0) return false;

        Size  size       = node.NodeValueMeasurement!.Value.Size;
        IFont font       = FontRegistry.Fonts[node.ComputedStyle.Font];
        int   fontSize   = node.ComputedStyle.FontSize;
        float lineHeight = font.GetLineHeight(fontSize);
        var   metrics    = font.GetMetrics(node.ComputedStyle.FontSize);

        var y = origin.Y + (lineHeight + node.ComputedStyle.TextOffset.Y) - metrics.Descent - 1.5f;
        var x = origin.X + node.ComputedStyle.TextOffset.X;

        y = (int)Math.Ceiling(y);

        if (node.ComputedStyle.TextAlign.IsTop) y    += node.ComputedStyle.Padding.Top;
        if (node.ComputedStyle.TextAlign.IsLeft) x   += node.ComputedStyle.Padding.Left;
        if (node.ComputedStyle.TextAlign.IsRight) x  -= node.ComputedStyle.Padding.Right;
        if (node.ComputedStyle.TextAlign.IsMiddle) y += (node.Height - size.Height) / 2f;
        if (node.ComputedStyle.TextAlign.IsBottom) y += node.Height - size.Height;

        foreach (Chunk[] line in node.NodeValueMeasurement!.Value.Lines) {
            PrintLine(canvas, font, node, line, x, y);
            y += (lineHeight * node.ComputedStyle.LineHeight);
        }

        return true;
    }

    private static void PrintLine(SKCanvas canvas, IFont font, Node node, Chunk[] line, float x, float y)
    {
        float lineWidth = line.Sum(chunk => chunk.Width);

        if (node.ComputedStyle.TextAlign.IsCenter) x += (node.Bounds.PaddingSize.Width - lineWidth) / 2;
        if (node.ComputedStyle.TextAlign.IsRight) x  += (node.Bounds.PaddingSize.Width - lineWidth);

        using SKPaint paint        = new();
        SKPoint       point        = new(MathF.Round(x), MathF.Round(y));

        font.DrawText(canvas, paint, point, node.ComputedStyle, line);
    }
}