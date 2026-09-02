using Lumina.Text.ReadOnly;

namespace Una.Drawing.Font;

internal partial class DynamicFont
{
    /// <summary>
    /// Measures text, breaking lines based on maxWidth and explicit newline characters.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="fontSize">The font size.</param>
    /// <param name="maxWidth">The maximum allowed width for a line.</param>
    /// <param name="lineHeightFactor">Factor to multiply the default line height by.</param>
    /// <returns>A MeasuredText object containing the lines and dimensions.</returns>
    private MeasuredText MeasureMultiLine(
        ReadOnlySeString text,
        int              fontSize,
        float            maxWidth,
        float            lineHeightFactor
    )
    {
        List<Chunk> chunks = GenerateChunks(text, fontSize);

        // Handle null or empty input gracefully
        if (chunks.Count == 0) {
            return new() { Lines = [], Size = new(0, 0), LineCount = 0, };
        }

        List<Chunk[]> lines = [];
        List<Chunk>   line  = [];

        float lineHeight       = GetLineHeight(fontSize);
        float currentLineWidth = 0;
        float maxLineWidth     = 0;
        float totalHeight      = 0;

        var newLine = (float newTotalWidth, ref float th) => {
            if (newTotalWidth <= maxWidth) return;
            currentLineWidth =  0;
            th               += (lineHeight * lineHeightFactor);
            lines.Add(line.ToArray());
            line.Clear();
        };

        // Process chunk by chunk to handle potential font changes
        foreach (var chunk in chunks) {
            SKFont chunkFont  = GetFont(chunk, fontSize);
            float  spaceWidth = chunkFont.MeasureText(" ");

            switch (chunk.Type) {
                case Chunk.Kind.BitmapIcon: {
                    float iconWidth = chunk.BitmapIcon!.Value.Size.X;
                    newLine(currentLineWidth + iconWidth, ref totalHeight);
                    currentLineWidth += chunk.Width;
                    line.Add(chunk);
                    continue;
                }
                case Chunk.Kind.Glyph: {
                    float glyphWidth = chunkFont.MeasureText(chunk.Text);
                    newLine(currentLineWidth + glyphWidth, ref totalHeight);
                    currentLineWidth += chunk.Width;
                    line.Add(chunk);
                    continue;
                }
            }

            string[] subLines = chunk.Text.Split(["\n"], StringSplitOptions.None);

            for (int i = 0; i < subLines.Length; i++) {
                string   subLine = subLines[i];
                string[] words   = subLine.Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (string word in words) {
                    float  wordWidth  = chunkFont.MeasureText(word);
                    float  chunkWidth = wordWidth;
                    string wordToAdd  = word;

                    newLine(currentLineWidth + wordWidth, ref totalHeight);
                    currentLineWidth += wordWidth;

                    if (currentLineWidth > 0) {
                        wordToAdd        += ' ';
                        chunkWidth       += spaceWidth;
                        currentLineWidth += spaceWidth;
                    }

                    line.Add(new(chunk.Type, wordToAdd, chunk.Color, chunk.EdgeColor, chunkWidth));
                }

                if (i < subLines.Length - 1) {
                    lines.Add(line.ToArray());
                    line.Clear();

                    totalHeight      += (lineHeight * lineHeightFactor);
                    maxLineWidth     =  MathF.Max(maxLineWidth, currentLineWidth);
                    currentLineWidth =  0;
                }
            }
        }

        // Add any remaining text in the buffer as the very last line
        if (line.Count > 0) {
            lines.Add(line.ToArray());
            totalHeight  += (lineHeight * lineHeightFactor);
            maxLineWidth =  MathF.Max(maxLineWidth, currentLineWidth);
        }

        if (lines.Count > 0 && totalHeight <= 0) {
            totalHeight = lineHeight * lineHeightFactor; // Ensure at least one line height
        }

        return new() { Lines = lines.ToArray(), Size = new(lines.Count > 0 ? MathF.Ceiling(MathF.Max(1, maxLineWidth)) : 0, MathF.Ceiling(totalHeight)), LineCount = (uint)lines.Count, };
    }
}