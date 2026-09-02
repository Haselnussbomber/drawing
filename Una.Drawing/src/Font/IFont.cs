using Lumina.Text.ReadOnly;

namespace Una.Drawing.Font;

public interface IFont
{
    /// <summary>
    /// Returns the size of the given text when rendered with this font.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="fontSize">The size of the text in pixels.</param>
    /// <param name="maxLineWidth">Maximum line width before text starts wrapping or showing an ellipsis.</param>
    /// <param name="wordWrap">Whether to wrap text to the next line if it exceeds the max line width.</param>
    /// <param name="textOverflow">Wether to allow text to be cut off without word-breaks or ellipsis.</param>
    /// <param name="lineHeight">A scale factor that determines the height of lines.</param>
    /// <param name="maxWidth">The maximum width of a single line of text.</param>
    /// <returns>A <see cref="MeasuredText"/> object containing wrapped text.</returns>
    internal MeasuredText MeasureText(
        ReadOnlySeString text,
        int fontSize = 14,
        float? maxLineWidth = null,
        bool wordWrap = false,
        bool textOverflow = true,
        float lineHeight = 1.2f,
        float? maxWidth = null
    );

    /// <summary>
    /// Draws the given text on the canvas using this font.
    /// </summary>
    /// <param name="canvas"></param>
    /// <param name="paint"></param>
    /// <param name="pos">The top-left position to draw the text.</param>
    /// <param name="style">The computed stylesheet.</param>
    /// <param name="text">The measured text to render.</param>
    internal void DrawText(SKCanvas canvas, SKPaint paint, SKPoint pos, ComputedStyle style, Chunk[] text);

    /// <summary>
    /// Returns a <see cref="SKFontMetrics"/> object for the given font size.
    /// </summary>
    /// <param name="fontSize"></param>
    /// <returns></returns>
    internal SKFontMetrics GetMetrics(int fontSize);

    /// <summary>
    /// Returns the height of a single line of text.
    /// </summary>
    internal float GetLineHeight(int fontSize);

    internal void Dispose();
}

internal struct MeasuredText
{
    /// <summary>
    /// The computed size of the text.
    /// </summary>
    public Size Size;

    /// <summary>
    /// The amount of lines in the text.
    /// </summary>
    public uint LineCount;

    /// <summary>
    /// A list of lines in the text.
    /// </summary>
    public Chunk[][] Lines;
}