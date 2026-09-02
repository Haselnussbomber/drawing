using Lumina.Text.ReadOnly;

namespace Una.Drawing.Font;

internal partial class DynamicFont
{
    private MeasuredText MeasureSingleLine(
        ReadOnlySeString text,
        int              fontSize,
        float            maxWidth,
        bool             textOverflow
    )
    {
        List<Chunk> chunks = GenerateChunks(text, fontSize);
        List<Chunk> line   = [];

        float width  = 0;
        float height = GetLineHeight(fontSize);

        if (textOverflow || maxWidth == 0 || maxWidth >= float.MaxValue - 1f) {
            foreach (var chunk in chunks) {
                width += chunk.Width;
                line.Add(chunk);
            }
        } else {
            foreach (var chunk in chunks) {
                if (BreakChunkAt(chunk, width, maxWidth, fontSize, out var result)) {
                    width += chunk.Width;
                    line.Add(result);
                } else {
                    width += chunk.Width;
                    width =  MathF.Min(width, maxWidth);
                    line.Add(result);
                    break;
                }
            }
        }

        if (maxWidth == 0) maxWidth = float.MaxValue;

        return new() { Lines = [line.ToArray()], Size = new((int)MathF.Min(maxWidth, width), (int)height), LineCount = 1, };
    }
}