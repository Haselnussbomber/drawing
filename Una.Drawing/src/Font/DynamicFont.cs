using Lumina.Text.ReadOnly;
using Una.Drawing.Texture;

namespace Una.Drawing.Font;

internal partial class DynamicFont(SKTypeface textTypeface, SKTypeface glyphTypeface, float sizeOffset) : IFont
{
    private SKTypeface TextTypeface  { get; } = textTypeface;
    private SKTypeface GlyphTypeface { get; } = glyphTypeface;
    private float      SizeOffset    { get; } = sizeOffset;

    public MeasuredText MeasureText(
        ReadOnlySeString text,
        int              fontSize     = 14,
        float?           maxLineWidth = null,
        bool             wordWrap     = false,
        bool             textOverflow = true,
        float            lineHeight   = 1.2f,
        float?           maxWidth     = null
    )
    {
        if (maxWidth > 0) {
            maxLineWidth = maxWidth;
            wordWrap     = false;
            textOverflow = false;
        }

        if (wordWrap == false || maxLineWidth is null or 0) {
            return MeasureSingleLine(text, fontSize, maxLineWidth ?? 0, textOverflow);
        }

        return MeasureMultiLine(text, fontSize, maxLineWidth.Value, lineHeight);
    }

    public void DrawText(SKCanvas canvas, SKPaint paint, SKPoint pos, ComputedStyle style, Chunk[] chunks)
    {
        if (chunks.Length == 0) return;

        float x = pos.X;
        float y = pos.Y;

        foreach (var chunk in chunks) {
            (x, y) = DrawTextChunk(canvas, paint, x, y, style, chunk);
        }
    }

    public SKFontMetrics GetMetrics(int fontSize)
    {
        return GetTextFont(fontSize).Metrics;
    }

    public float GetLineHeight(int fontSize)
    {
        SKFontMetrics metrics = GetTextFont(fontSize).Metrics;
        return metrics.Descent - metrics.Ascent + metrics.Leading;
    }

    public void Dispose()
    {
        foreach (SKFont font in TextFontCache.Values) font.Dispose();
        foreach (SKFont font in GlyphFontCache.Values) font.Dispose();

        TextFontCache.Clear();
        GlyphFontCache.Clear();
    }

    private (float x, float y) DrawTextChunk(SKCanvas canvas, SKPaint paint, float x, float y, ComputedStyle style, Chunk chunk)
    {
        if (chunk.Type == Chunk.Kind.BitmapIcon) {
            using SKPaint gfdPaint = new();

            GfdIcon icon = chunk.BitmapIcon!.Value;

            canvas.DrawImage(
                icon.Texture,
                new(icon.Uv0.X, icon.Uv0.Y, icon.Uv1.X, icon.Uv1.Y),
                new SKRect(
                    x,
                    y - (icon.Size.Y / 2f) - 4,
                    x + icon.Size.X,
                    y + (icon.Size.Y / 2f) - 4
                ),
                gfdPaint
            );

            return (x + chunk.Width, y);
        }

        SKFont font = chunk.Type == Chunk.Kind.Glyph ? GetGlyphFont(style.FontSize) : GetTextFont(style.FontSize);

        Color  textColor    = chunk.Color ?? style.Color;
        Color? outlineColor = chunk.EdgeColor ?? style.OutlineColor;

        x = MathF.Round(x);
        y = MathF.Round(y);
        
        if (style.OutlineSize > 0 && null != outlineColor) {
            paint.ImageFilter = null;
            paint.Color       = Color.ToSkColor(outlineColor);
            paint.Style       = SKPaintStyle.Stroke;
            paint.StrokeWidth = style.OutlineSize;

            if (style.OutlineSize > 1) {
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Solid, style.OutlineSize);
            } else {
                paint.StrokeWidth = 3.0f; // 1.5f on each side.
            }

            canvas.DrawText(chunk.Text, x, y, font, paint);
        }

        if (style.TextShadowSize > 0) {
            paint.ImageFilter = paint.ImageFilter = SKImageFilter.CreateDropShadow(
                0,
                0,
                style.TextShadowSize,
                style.TextShadowSize,
                Color.ToSkColor(style.TextShadowColor ?? new(0xFF000000))
            );
        }

        paint.Color       = Color.ToSkColor(textColor);
        paint.Style       = SKPaintStyle.Fill;
        paint.StrokeWidth = 0;
        paint.MaskFilter  = null;

        canvas.DrawText(chunk.Text, x, y, font, paint);

        return (x + chunk.Width, y);
    }
}