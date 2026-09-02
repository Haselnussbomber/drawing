using Una.Drawing.Font;

namespace Una.Drawing;

public partial class Node
{
    private EdgeSize _textCachedPadding = new();
    private object?  _textCachedNodeValue;
    private uint?    _textCachedFontId;
    private float?   _textCachedFontSize;
    private bool?    _textCachedWordWrap;
    private Size?    _textCachedNodeSize;
    private float?   _textCachedMaxWidth;
    private bool     _mustRecomputeNodeValue = true;

    internal MeasuredText? NodeValueMeasurement { get; private set; }

    internal void ClearTextCache()
    {
        _textCachedPadding      = new();
        _textCachedNodeValue    = null;
        _textCachedFontId       = null;
        _textCachedFontSize     = null;
        _textCachedWordWrap     = null;
        _textCachedNodeSize     = null;
        _textCachedMaxWidth     = null;
        _mustRecomputeNodeValue = true;
    }

    /// <summary>
    /// <para>
    /// Computes the <see cref="NodeBounds.ContentSize"/> of this node based on
    /// the text content of the node and returns a <see cref="Size"/> object
    /// containing the width and height of the content.
    /// </para>
    /// <para>
    /// If the node value is empty, the returned size is zero.
    /// </para>
    /// <para>
    /// This function returns cached values if any style properties that affect
    /// the text content have not changed since the last computation.
    /// </para>
    /// </summary>
    internal Size ComputeContentSizeFromText()
    {
        if (!_mustRecomputeNodeValue && _nodeValue.IsEmpty) {
            return new(0, 0);
        }

        if (!_mustRecomputeNodeValue && !MustRecomputeNodeValue()) {
            return NodeValueMeasurement?.Size ?? new();
        }

        if (_mustRecomputeNodeValue) {
            _mustRecomputeNodeValue = false;
        }
        
        _textCachedNodeValue = _nodeValue;
        _textCachedWordWrap  = ComputedStyle.WordWrap;
        _textCachedPadding   = ComputedStyle.Padding.Copy();
        _textCachedFontId    = ComputedStyle.Font;
        _textCachedFontSize  = ComputedStyle.FontSize;
        _textCachedNodeSize  = ComputedStyle.Size.Copy();
        _textCachedMaxWidth  = ComputedStyle.MaxWidth;

        var font         = FontRegistry.Fonts[ComputedStyle.Font];
        var maxLineWidth = MathF.Max(0, ComputedStyle.Size.Width);

        if (maxLineWidth > 0) maxLineWidth -= ComputedStyle.Padding.HorizontalSize;

        if (maxLineWidth == 0 && ComputedStyle.AutoSize.Horizontal == AutoSize.Grow) {
            maxLineWidth = MathF.Max(0, Bounds.ContentSize.Width);
        }

        NodeValueMeasurement = font.MeasureText(
            NodeValue,
            ComputedStyle.FontSize,
            maxLineWidth,
            ComputedStyle.WordWrap,
            ComputedStyle.TextOverflow,
            ComputedStyle.LineHeight,
            ComputedStyle.MaxWidth
        );

        return NodeValueMeasurement.Value.Size;
    }

    /// <summary>
    /// Returns true if the computed text content and bounding box based on the
    /// node value must be recomputed.
    /// </summary>
    internal bool MustRecomputeNodeValue()
    {
        return !_textCachedFontId.Equals(ComputedStyle.Font)
            || (!_textCachedNodeSize?.Equals(ComputedStyle.Size) ?? true)
            || (!_textCachedFontSize?.Equals(ComputedStyle.FontSize) ?? true)
            || (!_textCachedWordWrap?.Equals(ComputedStyle.WordWrap) ?? true)
            || (!_textCachedNodeValue?.Equals(_nodeValue) ?? true)
            || (!_textCachedMaxWidth?.Equals(ComputedStyle.MaxWidth) ?? true)
            || !_textCachedPadding.Equals(ComputedStyle.Padding);
    }
}
