
using System.Linq;

namespace Una.Drawing;

public partial class Node
{
    public event Action<Node>? OnClick;
    public event Action<Node>? OnDoubleClick;
    public event Action<Node>? OnMiddleClick;
    public event Action<Node>? OnRightClick;
    public event Action<Node>? OnMouseEnter;
    public event Action<Node>? OnMouseLeave;
    public event Action<Node>? OnMouseDown;
    public event Action<Node>? OnMouseUp;
    public event Action<Node>? OnDelayedMouseEnter;
    public event Action<Node>? OnDragStart;
    public event Action<Node>? OnDragMove;
    public event Action<Node>? OnDragEnd;

    /// <summary>
    /// True if the element has any interactive event listeners attached to it.
    /// </summary>
    public bool IsInteractive =>
        !IsDisabled && (
            null != OnClick
            || null != OnMiddleClick
            || null != OnRightClick
            || null != OnMouseEnter
            || null != OnDelayedMouseEnter
            || null != OnMouseLeave
            || null != OnMouseDown
            || null != OnMouseUp
            || null != OnDragStart
            || null != OnDragMove
            || null != OnDragEnd
        );

    /// <summary>
    /// True if the element has any drag-related event listeners attached to it.
    /// </summary>
    public bool IsDraggable =>
        !IsDisabled && (
            null != OnDragStart
            || null != OnDragMove
            || null != OnDragEnd
        );

    /// <summary>
    /// Whether the element should have the "hover" tag added when the mouse is over it.
    /// </summary>
    public bool EnableHoverTag { get; set; } = true;

    /// <summary>
    /// True if the element has any primary interaction event listeners attached to it.
    /// </summary>
    public bool HasPrimaryInteraction =>
        !IsDisabled && (IsDraggable || null != OnClick || null != OnMouseUp || null != OnMouseDown);

    /// <summary>
    /// Set to true from an event listener to stop remaining event listeners from being called.
    /// </summary>
    public bool CancelEvent { get; set; }

    /// <summary>
    /// True if the mouse cursor is currently inside the bounding box of the element.
    /// </summary>
    public bool IsMouseOver { get; private set; }

    /// <summary>
    /// True if one of the mouse buttons in held down while the cursor is over the element.
    /// </summary>
    public bool IsMouseDown { get; private set; }

    /// <summary>
    /// True if one of the mouse buttons in held down while the cursor is over a different element.
    /// </summary>
    public bool IsMouseDownOverOtherNode { get; private set; }

    public bool IsMiddleMouseDown { get; private set; }
    public bool IsRightMouseDown  { get; private set; }

    /// <summary>
    /// True if this element currently has focus.
    /// </summary>
    public bool IsFocused { get; private set; }

    /// <summary>
    /// True if this node cannot be interacted with, regardless of bound event
    /// listeners.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// True if this node is currently being dragged.
    /// </summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// The offset of the drag position from the original position of the node.
    /// </summary>
    public Vector2 DragDelta { get; private set; }

    /// <summary>
    /// The node that is currently being dragged by the user, or NULL if there
    /// is no drag operation in progress.
    /// </summary>
    public static Node? DraggedNode { get; private set; }

    /// <summary>
    /// The color that will be used as background when rendering a tooltip.
    /// </summary>
    public static uint TooltipBackgroundColor { get; set; } = 0xFF353535;

    /// <summary>
    /// The color that will be used as text color when rendering a tooltip.
    /// </summary>
    public static uint TooltipTextColor { get; set; } = 0xFFCACACA;
    
    private bool   _isInWindowOrInteractiveParent;
    private bool   _didStartInteractive;
    private bool   _didStartDelayedMouseEnter;
    private double _mouseOverStartTime;
    private double _isVisibleSince;
    private double _lastClickTime;

    private void SetupInteractive(ImDrawListPtr drawList)
    {
        _didStartInteractive = false;

        ToggleTag("disabled", IsDisabled);
        RenderTooltip();
        
        if (!InheritTags && HasTag("hover") && IsDisabled) {
            RemoveTag("hover");
        }

        if (IsDisabled || !IsInteractive || !IsVisible) {
            MouseCursor.RemoveMouseOver(this);
            return;
        }

        bool isPassthrough = IsPassthroughDrawList(drawList);

        // Only allow interaction if the window has focus.
        // TODO: Maybe make an option toggle for this behavior.
        if (!isPassthrough && IsInWindowDrawList(drawList) && !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) {
            MouseCursor.RemoveMouseOver(this);
            IsMouseOver = false;
            ToggleTag("hover", false);
            return;
        }

        if (_isVisibleSince == 0) _isVisibleSince = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        // Debounce the interactive state to prevent unintentional clicks
        // when toggling visibility on elements on the same position.
        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - _isVisibleSince < 100) return;

        _didStartInteractive = true;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);

        string  imGuiId           = InternalId;
        Vector2 boundingBoxSize   = Bounds.PaddingSize.ToVector2();
        Node?   interactiveParent = GetInteractiveParent();
        _isInWindowOrInteractiveParent = (!isPassthrough && IsInWindowDrawList(drawList)) || interactiveParent != null;

        // Disabled to allow for multi-monitor support. Leaving this here in case something breaks.
        // ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);

        if (_isInWindowOrInteractiveParent) {
            ImGui.SetCursorScreenPos(Bounds.PaddingRect.TopLeft);
            ImGui.BeginChild(imGuiId, boundingBoxSize, false, InteractiveWindowFlags);
            ImGui.SetCursorScreenPos(Bounds.PaddingRect.TopLeft);
        } else {
            ImGui.SetNextWindowPos(Bounds.PaddingRect.TopLeft, ImGuiCond.Always);
            ImGui.SetNextWindowSize(boundingBoxSize, ImGuiCond.Always);
            ImGui.Begin(imGuiId, InteractiveWindowFlags);
        }

        bool wasHovered  = IsMouseOver;
        bool wasDragging = IsDragging;

        ImGui.SetCursorScreenPos(Bounds.PaddingRect.TopLeft);
        ImGui.InvisibleButton($"{imGuiId}##Button", Bounds.PaddingSize.ToVector2());
        IsMouseOver = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlapped);
        IsFocused   = ImGui.IsItemFocused();
        IsDragging  = IsDragging || (IsMouseOver && ImGui.IsMouseDragging(ImGuiMouseButton.Left));

        var isHovered = ImGui.IsItemHovered();
        
        if (isHovered && HasPrimaryInteraction && EnableHoverTag) {
            MouseCursor.RegisterMouseOver(this);
        } else {
            MouseCursor.RemoveMouseOver(this);
        }

        if (IsDraggable && IsDragging) {
            if (!wasDragging) {
                RaiseEvent(OnDragStart);
                ToggleTag("dragging", true);
                ToggleTag("hover", false);
                IsMouseOver = false;
                IsDragging  = true;
                DragDelta   = ImGui.GetIO().MouseDelta;
                DraggedNode = this;
            } else {
                DragDelta   += ImGui.GetIO().MouseDelta;
                DraggedNode =  this;
                RaiseEvent(OnDragMove);
                RenderDragGhost();
            }
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && IsDragging) {
            RaiseEvent(OnDragEnd);
            ToggleTag("dragging", false);
            DraggedNode = null;
            IsDragging  = false;
            DragDelta   = Vector2.Zero;
        }

        switch (!IsDragging && IsMouseOver && HasPrimaryInteraction && EnableHoverTag) {
            case true when !HasTag("hover"):
                AddTag("hover");
                break;
            case false when HasTag("hover"):
                RemoveTag("hover");
                break;
        }

        switch (IsFocused) {
            case true when !HasTag("focus"):
                AddTag("focus");
                break;
            case false when HasTag("focus"):
                RemoveTag("focus");
                break;
        }

        switch (isHovered && !IsDragging) {
            case true when !HasTag("active"):
                AddTag("active");
                break;
            case false when HasTag("active"):
                RemoveTag("active");
                break;
        }

        switch (wasHovered) {
            case false when IsMouseOver:
                RaiseEvent(OnMouseEnter);
                _mouseOverStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                break;
            case true when !IsMouseOver:
                RaiseEvent(OnMouseLeave);
                _didStartDelayedMouseEnter = false;
                IsMouseDown                = false;
                IsMiddleMouseDown          = false;
                IsRightMouseDown           = false;
                break;
        }

        if (isHovered) {
            if (HasPrimaryInteraction) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (_mouseOverStartTime < DateTimeOffset.Now.ToUnixTimeMilliseconds() - 50) {
                if (!_didStartDelayedMouseEnter) {
                    RaiseEvent(OnDelayedMouseEnter);
                    _didStartDelayedMouseEnter = true;
                }
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                if (!IsMouseDown) {
                    RaiseEvent(OnMouseDown);
                    IsMouseDown = true;
                }
            } else if (ImGui.IsMouseDown(ImGuiMouseButton.Middle)) {
                if (!IsMiddleMouseDown) {
                    IsMiddleMouseDown = true;
                }
            } else if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                if (!IsRightMouseDown) {
                    IsRightMouseDown = true;
                }
            } else {
                if (IsMouseDown && !IsMouseDownOverOtherNode) {
                    RaiseEvent(OnMouseUp);
                    RaiseEvent(OnClick);
                    IsMouseDown = false;
                    
                    var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    if (_lastClickTime > 0 && now - _lastClickTime < 250) {
                        RaiseEvent(OnDoubleClick);
                    }
                    
                    _lastClickTime = now;
                }

                if (IsMiddleMouseDown) {
                    RaiseEvent(OnMiddleClick);
                    IsMiddleMouseDown = false;
                }

                if (IsRightMouseDown) {
                    RaiseEvent(OnRightClick);
                    IsRightMouseDown = false;
                }

                if (IsMouseDownOverOtherNode) {
                    RaiseEvent(OnMouseUp);
                }
            }
        } else {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                IsMouseDownOverOtherNode = true;
            }
        }

        if (IsMouseDownOverOtherNode && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
            IsMouseDownOverOtherNode = false;
        }
    }

    private void EndInteractive()
    {
        if (!_didStartInteractive) return;

        if (_isInWindowOrInteractiveParent) {
            ImGui.EndChild();
        } else {
            ImGui.End();
        }

        ImGui.PopStyleVar(5);
    }

    /// <summary>
    /// Returns the top-most interactive parent element.
    /// </summary>
    /// <returns></returns>
    private Node? GetInteractiveParent()
    {
        Node? immediateParent   = ParentNode;
        Node? interactiveParent = null;

        while (immediateParent != null) {
            if (immediateParent.IsInteractive) interactiveParent = immediateParent;
            immediateParent = immediateParent.ParentNode;
        }

        return interactiveParent;
    }

    /// <summary>
    /// Returns true if the given drawList is not the foreground or background drawList.
    /// </summary>
    private static unsafe bool IsInWindowDrawList(ImDrawListPtr drawList)
    {
        return
            drawList.Handle != ImGui.GetForegroundDrawList().Handle
            && drawList.Handle != ImGui.GetBackgroundDrawList().Handle;
    }

    private static unsafe bool IsPassthroughDrawList(ImDrawListPtr drawList)
    {
        return PassthroughDrawListHandles.Contains((nint)drawList.Handle);
    }

    private void RaiseEvent(Action<Node>? action)
    {
        if (action == null) return;

        CancelEvent = false;
        foreach (var handler in action.GetInvocationList().Reverse()) {
            if (!CancelEvent) handler.DynamicInvoke(this);
            if (CancelEvent) break;
        }

        CancelEvent = false;
    }

    private void DisposeEventHandlersOf<T>(Action<T>? action)
    {
        if (action == null) return;

        foreach (Delegate handler in action.GetInvocationList()) {
            if (handler is Action<T> del) action -= del;
        }
    }

    private void DisposeEventHandlersOf<T, K>(Action<T, K>? action)
    {
        if (action == null) return;

        foreach (Delegate handler in action.GetInvocationList()) {
            if (handler is Action<T, K> del) action -= del;
        }
    }

    private void DisposeEventHandlersOf(Action? action)
    {
        if (action == null) return;

        foreach (Delegate handler in action.GetInvocationList()) {
            if (handler is Action del) action -= del;
        }
    }

    private double _tooltipHoverStartTime;
    
    private void RenderTooltip()
    {
        if (Tooltip.IsEmpty) return;
        
        if (!IsMouseInNodeBounds(this, new(), false)) {
            _tooltipHoverStartTime = 0;
            return;
        }
        
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (_tooltipHoverStartTime == 0) {
            _tooltipHoverStartTime = now;
        }
        
        if (_tooltipHoverStartTime < now - 500) {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, TooltipBackgroundColor);
            ImGui.PushStyleColor(ImGuiCol.Text, TooltipTextColor);
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(420.0f);
            ImGui.TextUnformatted(Tooltip.ToString());
            ImGui.EndTooltip();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
        }
    }

    private static ImGuiWindowFlags InteractiveWindowFlags =>
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoDocking
        | ImGuiWindowFlags.NoNavFocus
        | ImGuiWindowFlags.NoNavInputs
        | ImGuiWindowFlags.NoFocusOnAppearing;
}