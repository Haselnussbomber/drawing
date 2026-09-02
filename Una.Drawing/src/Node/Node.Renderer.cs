using Dalamud.Interface.Textures.TextureWraps;

using Lumina.Misc;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Una.Drawing.Font;
using Una.Drawing.Texture;

namespace Una.Drawing;

public partial class Node
{
    /// <summary>
    /// <para>
    /// A callback that is invoked before the node is drawn.
    /// </para>
    /// <para>
    /// This method is guaranteed to be called before the node is drawn,
    /// regardless of whether the node is visible or not, or if it needs to
    /// reflow.
    /// </para>
    /// </summary>
    public Action<Node>? BeforeDraw;

    /// <summary>
    /// <para>
    /// A callback that is invoked after the node is drawn.
    /// </para>
    /// <para>
    /// This method is guaranteed to be called before the node is drawn,
    /// regardless of whether the node is visible or not, or if it needs to
    /// reflow.
    /// </para>
    /// </summary>
    public Action<Node>? AfterDraw;

    /// <summary>
    /// Whether children of this node should overflow the bounds of the node.
    /// Defaults to <c>true</c>. Settings this to false will clip children to
    /// the bounds of the node and show a scrollbar if necessary.
    /// </summary>
    /// <remarks>
    /// A side effect of setting this to <c>false</c> is that the node will
    /// render its children in a separate draw list. Child nodes that are not
    /// visible due to them being outside the bounds of the parent node will
    /// be set to "invisible" and will not be drawn. This may cause issues
    /// if the child nodes have their visibility toggled manually.
    /// </remarks>
    public bool Overflow { get; set; } = true;

    /// <summary>
    /// Shows a horizontal scrollbar if the children exceed the width of the
    /// bounds of the node. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Only applicable when <see cref="Overflow"/> is set to <c>false</c>.
    /// </remarks>
    public bool HorizontalScrollbar { get; set; } = false;

    /// <summary>
    /// Represents the current horizontal scroll position of the node.
    /// </summary>
    /// <remarks>
    /// Only applicable when <see cref="Overflow"/> is set to <c>false</c>.
    /// </remarks>
    public float ScrollX { get; private set; }

    /// <summary>
    /// Represents the current vertical scroll position of the node.
    /// </summary>
    /// <remarks>
    /// Only applicable when <see cref="Overflow"/> is set to <c>false</c>.
    /// </remarks>
    public float ScrollY { get; private set; }

    /// <summary>
    /// Represents the current width of the scroll area of the node.
    /// </summary>
    /// <remarks>
    /// Only applicable when <see cref="Overflow"/> is set to <c>false</c>.
    /// </remarks>
    public float ScrollWidth { get; private set; }

    /// <summary>
    /// Represents the current height of the scroll area of the node.
    /// </summary>
    /// <remarks>
    /// Only applicable when <see cref="Overflow"/> is set to <c>false</c>.
    /// </remarks>
    public float ScrollHeight { get; private set; }

    public double DrawDeltaTime { get; private set; }
    public double DrawTotalTime { get; private set; }

    /// <summary>
    /// A deterministic hash code based on the node's value and layout.
    /// Used for caching purposes by the renderer.
    /// </summary>
    internal int RenderHash { get; private set; }

    private int  _previousRenderHash;
    private uint _colorThemeVersion;
    private bool _mustRepaint;

    private          IDalamudTextureWrap? _texture;
    private readonly List<ImDrawListPtr>  _drawLists = [];

    public void Render(ImDrawListPtr drawList, Vector2 position, bool forceSynchronousStyleComputation = false)
    {
        if (IsDisposed) return;

        if (ParentNode is not null)
            throw new InvalidOperationException("Cannot render a node that has a parent or is not a root node.");

        lock (_tagsList) InheritTagsFromParent();

        if (!forceSynchronousStyleComputation && UseThreadedStyleComputation) {
            Task.Run(ComputeStyle);
        } else {
            ComputeStyle();
        }

        if (!_isUpdatingStyle) {
            ComputedStyle = _intermediateStyle;
        }

        if (IsInWindowDrawList(drawList)) {
            position = ImGui.GetCursorScreenPos() + position;
        }

        lock (_lockObject) {
            Reflow(position);
            Draw(drawList);
        }
    }

    /// <summary>
    /// Returns true if this node is visible and its bounds are not empty.
    /// </summary>
    public bool IsVisible => ComputedStyle.IsVisible && !Bounds.PaddingSize.IsZero;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Draw(ImDrawListPtr drawList)
    {
        if (IsDisposed) return;

        if (!_previousSize.Equals(Bounds.ContentSize)) {
            _previousSize = Bounds.ContentSize;
            _mustRepaint  = true;
        }

        if (_causedReflow) {
            _causedReflow = false;
            if (DrawDebugPaintAndReflowBoxes) {
                ImGui.GetForegroundDrawList().AddRect(Bounds.MarginRect.TopLeft, Bounds.MarginRect.BottomRight, 0x80FF00FF, 0f, ImDrawFlags.None, 2.0f);
            }
        }
        
        TrackNodeRef(this);
        CheckDroppableNode();

        if (DrawTotalTime == 0) {
            DrawTotalTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            DrawDeltaTime = 0;
        } else {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            DrawDeltaTime = now - DrawTotalTime;
            DrawTotalTime = now;
        }

        _metricStopwatch.Restart();
        BeforeDraw?.Invoke(this);

        if (Color.ThemeVersion != _colorThemeVersion) {
            _colorThemeVersion = Color.ThemeVersion;
            _mustRepaint       = true;
        }

        if (Style.IsVisible is false) {
            DrawTime = _metricStopwatch.Elapsed.TotalMilliseconds;
            _metricStopwatch.Stop();
            return;
        }

        if (!IsVisible || !_hasComputedStyle) {
            _isVisibleSince = 0;
            DrawTime        = _metricStopwatch.Elapsed.TotalMilliseconds;
            _metricStopwatch.Stop();
            return;
        }

        if (_isVisibleSince == 0) _isVisibleSince = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        PushDrawList(drawList);
        BeginOverflowContainer();
        SetupInteractive(drawList);

        if (UpdateTexture()) RenderShadow(drawList);

        Vector2 topLeft     = Bounds.PaddingRect.TopLeft;
        Vector2 bottomRight = Bounds.PaddingRect.BottomRight;
        Vector2 offset      = new(32, 32);

        if (null != _texture) {
            drawList.AddImage(
                _texture.Handle,
                topLeft - offset,
                bottomRight + offset,
                Vector2.Zero,
                Vector2.One,
                GetRenderColor()
            );
        }

        ImDrawListPtr childDrawList = _drawLists.LastOrDefault();
        if (childDrawList.IsNull) {
            DrawTime = _metricStopwatch.Elapsed.TotalMilliseconds;
            _metricStopwatch.Stop();
            return;
        }

        if (!IsDisposed) {
            OnDraw(childDrawList);

            foreach (var childNode in _childNodes.ToArray()) {
                childNode.Draw(childDrawList);
            }
        }

        EndInteractive();
        EndOverflowContainer();

        AfterDraw?.Invoke(this);
        PopDrawList();

        _previousRenderHash = RenderHash;

        DrawTime = _metricStopwatch.Elapsed.TotalMilliseconds;
        _metricStopwatch.Stop();
        DrawDebugBounds(drawList);
    }

    private bool UpdateTexture()
    {
        if (NodeValue.IsEmpty && !ComputedStyle.HasDrawables()) {
            _consecutiveRedraws = 0;
            return false;
        }

        bool hasDrawables = ComputedStyle.HasDrawables() || !NodeValue.IsEmpty;

        if (_mustRepaint && hasDrawables && Width > 0 && Height > 0) {
            Vector2 padding = new(64, 64); // Optimization point: Only add padding when needed.

            _texture?.Dispose();
            _texture = Renderer.CreateTexture(this, padding);

            _consecutiveRedraws++;

            if (DrawDebugPaintAndReflowBoxes) {
                ImGui.GetForegroundDrawList().AddRect(
                    Bounds.PaddingRect.TopLeft,
                    Bounds.PaddingRect.BottomRight,
                    0xA000FFFF,
                    0f,
                    ImDrawFlags.RoundCornersNone,
                    3.0f
                );
            }
        } else {
            _consecutiveRedraws = 0;
        }

        _mustRepaint = false;

        if (_consecutiveRedraws > 3000) {
            DebugLogger.Log(
                $"WARNING: Node {this} is redrawing on every frame (value={_nodeValue}). Please check for unnecessary state changes."
            );

            _consecutiveRedraws = 0;
        }

        return true;
    }

    private int _consecutiveRedraws;

    /// <summary>
    /// Allows the node to draw custom content on the node's draw list.
    /// </summary>
    protected virtual void OnDraw(ImDrawListPtr drawList) { }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void BeginOverflowContainer()
    {
        if (Overflow) return;

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, ComputedStyle.BorderRadius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ComputedStyle.BorderRadius);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0);

        ImGui.PushStyleColor(ImGuiCol.FrameBg, 0);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, ComputedStyle.ScrollbarTrackColor.ToUInt());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, ComputedStyle.ScrollbarThumbColor.ToUInt());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, ComputedStyle.ScrollbarThumbActiveColor.ToUInt());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, ComputedStyle.ScrollbarThumbHoverColor.ToUInt());

        ImGui.SetCursorScreenPos(Bounds.ContentRect.TopLeft);

        uint frameId = Id != null ? Crc32.Get(Id) : InternalIdCrc32;

        ImGui.BeginChildFrame(
            frameId,
            Bounds.PaddingSize.ToVector2() - new Vector2(0, ComputedStyle.Padding.VerticalSize),
            (HorizontalScrollbar ? ImGuiWindowFlags.HorizontalScrollbar : ImGuiWindowFlags.None)
        );

        PushDrawList(ImGui.GetWindowDrawList());
        ImGui.SetCursorPos(Vector2.Zero);

        var scrollX      = (uint)ImGui.GetScrollX();
        var scrollY      = (uint)ImGui.GetScrollY();
        var scrollWidth  = (uint)ImGui.GetScrollMaxX();
        var scrollHeight = (uint)ImGui.GetScrollMaxY();

        ScrollX      = scrollX;
        ScrollY      = scrollY;
        ScrollWidth  = scrollWidth;
        ScrollHeight = scrollHeight;

        Vector2 pos = ImGui.GetCursorScreenPos();

        var x = pos.X;
        var y = pos.Y;

        foreach (var child in _childNodes.ToArray()) {
            Layout.OverridePositionsOf(child, new Vector2(x, y));

            if (ComputedStyle.Flow == Flow.Vertical) {
                y += child.Bounds.MarginSize.Height + ComputedStyle.Gap;
            } else {
                x += child.Bounds.MarginSize.Width + ComputedStyle.Gap;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void EndOverflowContainer()
    {
        if (Overflow) return;

        (Size total, Size max) = GetTotalChildrenSize();

        Vector2 size = new(
            ComputedStyle.Flow == Flow.Horizontal ? total.Width : max.Width,
            ComputedStyle.Flow == Flow.Vertical ? total.Height : max.Height
        );

        ImGui.SetCursorPos(size);
        ImGui.EndChildFrame();
        ImGui.PopStyleVar(8);
        ImGui.PopStyleColor(5);

        PopDrawList();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private NodeSnapshot CreateSnapshot()
    {
        return new() {
            Width       = OuterWidth,
            Height      = OuterHeight,
            ValueWidth  = NodeValueMeasurement?.Size.Width ?? 0,
            ValueHeight = NodeValueMeasurement?.Size.Height ?? 0,
            Layout      = ComputedStyle.LayoutStyleSnapshot,
            Paint       = ComputedStyle.PaintStyleSnapshot
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private uint GetRenderColor()
    {
        float opacity = ComputedStyle.Opacity;

        Node? parent = ParentNode;

        while (parent is not null
               && opacity > 0.0f) {
            opacity *= parent.ComputedStyle.Opacity;
            parent  =  parent.ParentNode;
        }

        opacity = Math.Clamp(opacity, 0.0f, 1.0f);

        return (uint)(opacity * 255) << 24 | 0x00FFFFFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderShadow(ImDrawListPtr drawList)
    {
        if (ComputedStyle.ShadowSize.IsZero) return;

        uint color = GetRenderColor();
        if (color == 0) return;

        var rect = Bounds.MarginRect.Copy();

        if (ComputedStyle.ShadowInset > 0) rect.Shrink(new(ComputedStyle.ShadowInset));

        rect.X1 += (int)ComputedStyle.ShadowOffset.X;
        rect.Y1 += (int)ComputedStyle.ShadowOffset.Y;
        rect.X2 += (int)ComputedStyle.ShadowOffset.X;
        rect.Y2 += (int)ComputedStyle.ShadowOffset.Y;

        const float uv0 = 0.0f;
        const float uv1 = 0.333333f;
        const float uv2 = 0.666666f;
        const float uv3 = 1.0f;

        ImDrawListPtr dl   = drawList;
        ImTextureID   id   = TextureLoader.GetEmbeddedTexture("Shadow.png").Handle;
        Vector2       p    = rect.TopLeft;
        Vector2       s    = new(rect.Width, rect.Height);
        Vector2       m    = new(p.X + s.X, p.Y + s.Y);
        EdgeSize      side = ComputedStyle.ShadowSize;

        if (IsInWindowDrawList(dl)) dl.PushClipRectFullScreen();

        if (side.Top > 0 || side.Left > 0)
            dl.AddImage(id, new(p.X - side.Left, p.Y - side.Top), new(p.X, p.Y), new(uv0, uv0), new(uv1, uv1), color);

        if (side.Top > 0)
            dl.AddImage(id, p with { Y = p.Y - side.Top }, new(m.X, p.Y), new(uv1, uv0), new(uv2, uv1), color);

        if (side.Top > 0 || side.Right > 0)
            dl.AddImage(
                id,
                m with { Y = p.Y - side.Top },
                p with { X = m.X + side.Right },
                new(uv2, uv0),
                new(uv3, uv1),
                color
            );

        if (side.Left > 0)
            dl.AddImage(id, p with { X = p.X - side.Left }, new(p.X, m.Y), new(uv0, uv1), new(uv1, uv2), color);

        if (side.Right > 0)
            dl.AddImage(id, new(m.X, p.Y), m with { X = m.X + side.Right }, new(uv2, uv1), new(uv3, uv2), color);

        if (side.Bottom > 0 || side.Left > 0)
            dl.AddImage(
                id,
                m with { X = p.X - side.Left },
                p with { Y = m.Y + side.Bottom },
                new(uv0, uv2),
                new(uv1, uv3),
                color
            );

        if (side.Bottom > 0)
            dl.AddImage(id, new(p.X, m.Y), m with { Y = m.Y + side.Bottom }, new(uv1, uv2), new(uv2, uv3), color);

        if (side.Bottom > 0 || side.Right > 0)
            dl.AddImage(
                id,
                new(m.X, m.Y),
                new(m.X + side.Right, m.Y + side.Bottom),
                new(uv2, uv2),
                new(uv3, uv3),
                color
            );

        if (IsInWindowDrawList(drawList)) dl.PopClipRect();
    }

    private void PushDrawList(ImDrawListPtr drawList)
    {
        _drawLists.Add(drawList);
    }

    private void PopDrawList()
    {
        _drawLists.RemoveAt(_drawLists.Count - 1);
    }

    private (Size, Size) GetTotalChildrenSize()
    {
        float totalWidth  = 0;
        float maxWidth    = 0;
        float totalHeight = 0;
        float maxHeight   = 0;

        lock (_childNodes) {
            foreach (var child in _childNodes) {
                Size sz = child.Bounds.MarginSize;

                totalWidth  += sz.Width;
                totalHeight += sz.Height;
                maxWidth    =  MathF.Max(maxWidth, sz.Width);
                maxHeight   =  MathF.Max(maxHeight, sz.Height);
            }

            // Account for the gap between children.
            if (ComputedStyle.Flow == Flow.Horizontal) {
                totalWidth += (_childNodes.Count - 1) * ComputedStyle.Gap;
            } else {
                totalHeight += (_childNodes.Count - 1) * ComputedStyle.Gap;
            }
        }

        return (new(totalWidth, totalHeight), new(maxWidth, maxHeight));
    }

    private void RenderDragGhost()
    {
        if (null == _texture) return;

        Vector2       topLeft     = Bounds.PaddingRect.TopLeft + DragDelta;
        Vector2       bottomRight = Bounds.PaddingRect.BottomRight + DragDelta;
        ImDrawListPtr ptr         = ImGui.GetForegroundDrawList();
        Vector2       offset      = new(32, 32);

        ptr.AddImage(
            _texture.Handle,
            topLeft - offset,
            bottomRight + offset,
            Vector2.Zero,
            Vector2.One,
            0xA0FFFFFF
        );
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct NodeSnapshot
{
    internal float               Width;
    internal float               Height;
    internal float               ValueWidth;
    internal float               ValueHeight;
    internal LayoutStyleSnapshot Layout;
    internal PaintStyleSnapshot  Paint;

    internal bool Equals(ref readonly NodeSnapshot other)
    {
        return MemoryMarshal
              .AsBytes(new ReadOnlySpan<NodeSnapshot>(in this))
              .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<NodeSnapshot>(in other)));
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.AddBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in this), 1)));
        return hash.ToHashCode();
    }
}