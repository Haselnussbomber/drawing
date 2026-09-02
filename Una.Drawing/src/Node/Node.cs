using Lumina.Text.ReadOnly;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;

namespace Una.Drawing;

public partial class Node : IDisposable
{
    /// <summary>
    /// Draw list handles registered as passthrough (no-input) host windows.
    /// Nodes rendered into these draw lists skip the window-focus guard and
    /// use top-level ImGui.Begin windows for hit-testing instead of BeginChild.
    /// </summary>
    public static readonly HashSet<nint> PassthroughDrawListHandles = [];

    /// <summary>
    /// Defines the global scale factor across all nodes.
    /// </summary>
    public static float ScaleFactor { get; set; } = 1.0f;

    /// <summary>
    /// Whether the global scale factor should affect the borders of the nodes.
    /// This should typically be disabled when using a scale factor of less than 1
    /// to prevent 1px-borders from becoming invisible.
    /// </summary>
    public static bool ScaleAffectsBorders { get; set; } = true;

    /// <summary>
    /// Defines a unique identifier for the node.
    /// </summary>
    public string? Id {
        get => _id;
        set {
            if (_id == value) return;

            if (string.IsNullOrWhiteSpace(value)) {
                _id = null;
            } else if (!IdentifierNamingRule().Match(value).Success) {
                throw new ArgumentException(
                    $"The given ID \"{value}\" is invalid. Node IDs must match the regex \"{IdentifierNamingRule()}\"."
                );
            } else {
                _id              = value;
                _internalId      = null;
                _internalIdCrc32 = null;
            }

            ClearCachedQuerySelectors();
            OnPropertyChanged?.Invoke("Id", _id);
        }
    }

    /// <summary>
    /// Defines the textual content of this node.
    /// </summary>
    public ReadOnlySeString NodeValue {
        get => _nodeValue;
        set {
            _nodeValue           = value;
            _textCachedNodeValue = null;
            _mustRepaint         = true;
            _mustReflow          = true;
            
            ClearTextCache();

            OnPropertyChanged?.Invoke("NodeValue", _nodeValue);
        }
    }

    /// <summary>
    /// Defines a tooltip text for this node.
    /// </summary>
    /// <remarks>
    /// Defining a tooltip makes this node interactive. This means that some
    /// nodes may no longer be interacted with if this node overlaps them.
    /// </remarks>
    public ReadOnlySeString Tooltip { get; set; }

    /// <summary>
    /// Returns a list of class names applied to this node.
    /// </summary>
    public ObservableHashSet<string> ClassList {
        get => _classList;
        set {
            if (_classList.SequenceEqual(value)) return;

            ClearCachedQuerySelectors();
            _classList.Clear();

            foreach (string v in value) _classList.Add(v);
            OnPropertyChanged?.Invoke("ClassList", _classList);
        }
    }

    /// <summary>
    /// Returns a list of tags applied to this node. Tags are used to denote
    /// certain characteristics of the node, that can be used for querying and
    /// styling purposes.
    /// </summary>
    /// <example>
    /// A node with ID "example" and tags "active" and "hovered" can be queried
    /// using the following query: `example:active:hovered`.
    /// </example>
    public ObservableHashSet<string> TagsList {
        get => _tagsList;
        set {
            if (_tagsList.SetEquals(value)) return;

            ClearCachedQuerySelectors();
            _tagsList.Clear();

            foreach (string v in value) _tagsList.Add(v);
            OnPropertyChanged?.Invoke("TagsList", _tagsList);
        }
    }

    /// <summary>
    /// <para>
    /// Whether to inherit tags from the parent node.
    /// </para>
    /// <para>
    /// This can be useful if the parent node is interactive and children have
    /// style definitions that are affected by the parent's interactivity tags,
    /// such as ":hover", ":active" and ":disabled".
    /// </para>
    /// </summary>
    /// <remarks>
    /// Custom tags are overwritten by the parent's tags for as long as this
    /// option is enabled.
    /// </remarks>
    public bool InheritTags {
        get => _inheritTags;
        set {
            if (_inheritTags.Equals(value)) return;
            _inheritTags = value;
            ClearCachedQuerySelectors();
        }
    }

    /// <summary>
    /// A list of child nodes of this node.
    /// </summary>
    public ObservableCollection<Node> ChildNodes {
        get => _childNodes;
        set {
            if (_childNodes.SequenceEqual(value)) return;

            lock (_childNodes) {
                List<Node> toRemove = _childNodes.ToList();

                foreach (var node in toRemove) node.Remove(true);
                foreach (var node in value.ToImmutableArray()) AppendChild(node);
            }

            OnPropertyChanged?.Invoke("ChildNodes", _childNodes);
        }
    }

    /// <summary>
    /// A reference to the parent node of this node.
    /// </summary>
    public Node? ParentNode { get; private set; }

    /// <summary>
    /// Defines the sort index of this node. Nodes are sorted in ascending
    /// order in the parent's child nodes list based on this value.
    /// </summary>
    public int SortIndex {
        get => _sortIndex;
        set {
            if (_sortIndex == value) return;

            _sortIndex           = value;
            RootNode._mustReflow = true;

            OnSortIndexChanged?.Invoke();
            OnPropertyChanged?.Invoke("SortIndex", _childNodes);
        }
    }

    /// <summary>
    /// A reference to the root node of this node. Returns itself if the node
    /// where this property is accessed from has no parent node.
    /// </summary>
    public Node RootNode => ParentNode?.RootNode ?? this;

    /// <summary>
    /// A reference to the node immediately preceding this node in the parent's
    /// child nodes list. Returns null if this node has no parent node, or if
    /// this node is the first child node of its parent.
    /// </summary>
    public Node? PreviousSibling => ParentNode?.ChildNodes.ElementAtOrDefault(ParentNode.ChildNodes.IndexOf(this) - 1);

    /// <summary>
    /// A reference to the node immediately following this node in the parent's
    /// child nodes list. Returns null if this node has no parent node, or if
    /// this node is the last child node of its parent.
    /// </summary>
    public Node? NextSibling => ParentNode?.ChildNodes.ElementAtOrDefault(ParentNode.ChildNodes.IndexOf(this) + 1);

    /// <summary>
    /// Invoked when one of the node's properties have been modified.
    /// </summary>
    public event Action<string, object?>? OnPropertyChanged;

    /// <summary>
    /// Invoked when a new child node has been added to this node.
    /// </summary>
    public event Action<Node>? OnChildAdded;

    /// <summary>
    /// Invoked when a child node has been removed from this node.
    /// </summary>
    public event Action<Node>? OnChildRemoved;

    /// <summary>
    /// Invoked when a class name has been added to the class list.
    /// </summary>
    public event Action<string>? OnClassAdded;

    /// <summary>
    /// Invoked when a class name has been removed from the class list.
    /// </summary>
    public event Action<string>? OnClassRemoved;

    /// <summary>
    /// Invoked when a tag has been added to the tags list.
    /// </summary>
    public event Action<string>? OnTagAdded;

    /// <summary>
    /// Invoked when a tag has been removed from the tags list.
    /// </summary>
    public event Action<string>? OnTagRemoved;

    /// <summary>
    /// Invoked when the sort index of this node has been changed.
    /// </summary>
    public event Action? OnSortIndexChanged;

    /// <summary>
    /// Whether this node has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    private string?          _id;
    private ReadOnlySeString _nodeValue;
    private bool             _inheritTags;
    private int              _sortIndex = -1;

    private readonly ObservableHashSet<string>   _classList   = [];
    private readonly ObservableHashSet<string>   _tagsList    = [];
    private          ObservableCollection<Node>  _childNodes  = [];
    private readonly Dictionary<string, object?> _attachments = new();
    
    public Node()
    {
        ComputedStyle                     = ComputedStyleFactory.CreateDefault();
        ComputedStyle.LayoutStyleSnapshot = new();
        ComputedStyle.PaintStyleSnapshot  = new();

        _childNodes.CollectionChanged += HandleChildListChanged;

        _classList.ItemAdded += c => {
            ClearCachedQuerySelectors();
            OnClassAdded?.Invoke(c);
            SignalReflow();
        };

        _classList.ItemRemoved += c => {
            ClearCachedQuerySelectors();
            OnClassRemoved?.Invoke(c);
            SignalReflow();
        };

        _tagsList.ItemAdded += t => {
            ClearCachedQuerySelectors();
            OnTagAdded?.Invoke(t);
            SignalReflow();
        };

        _tagsList.ItemRemoved += t => {
            ClearCachedQuerySelectors();
            OnTagRemoved?.Invoke(t);
            SignalReflow();
        };

        FontRegistry.FontChanged += OnFontConfigurationChanged;
    }

    ~Node()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        OnDispose?.Invoke(this);
        OnDisposed();

        lock (_childNodes) {
            foreach (var child in _childNodes.ToArray()) child.Dispose();
        }

        NodeValue = default;
        Tooltip   = default;

        DisposeEventHandlersOf(OnClick);
        DisposeEventHandlersOf(OnDoubleClick);
        DisposeEventHandlersOf(OnMouseDown);
        DisposeEventHandlersOf(OnMouseUp);
        DisposeEventHandlersOf(OnMouseEnter);
        DisposeEventHandlersOf(OnMouseLeave);
        DisposeEventHandlersOf(OnRightClick);
        DisposeEventHandlersOf(OnMiddleClick);
        DisposeEventHandlersOf(OnDelayedMouseEnter);
        DisposeEventHandlersOf(OnDragStart);
        DisposeEventHandlersOf(OnDragEnd);
        DisposeEventHandlersOf(OnDragMove);
        DisposeEventHandlersOf(OnChildAdded);
        DisposeEventHandlersOf(OnChildRemoved);
        DisposeEventHandlersOf(OnClassAdded);
        DisposeEventHandlersOf(OnClassRemoved);
        DisposeEventHandlersOf(OnTagAdded);
        DisposeEventHandlersOf(OnTagRemoved);
        DisposeEventHandlersOf(OnSortIndexChanged);
        DisposeEventHandlersOf(OnPropertyChanged);
        DisposeEventHandlersOf(OnSorted);

        OnClick             = null;
        OnMouseDown         = null;
        OnMouseUp           = null;
        OnMouseEnter        = null;
        OnMouseLeave        = null;
        OnRightClick        = null;
        OnMiddleClick       = null;
        OnDelayedMouseEnter = null;
        OnChildAdded        = null;
        OnChildRemoved      = null;
        OnClassAdded        = null;
        OnClassRemoved      = null;
        OnTagAdded          = null;
        OnTagRemoved        = null;
        OnSortIndexChanged  = null;
        OnPropertyChanged   = null;
        OnDispose           = null;

        BeforeReflow       = null;
        BeforeDraw         = null;
        AfterDraw          = null;
        ComputedStyle      = new();
        _style             = new();
        _intermediateStyle = new();
        _stylesheet        = null;
        _attachments.Clear();
        
        ClearTextCache();
        ClearQuerySelectorCache();
        ClearCachedQuerySelectors();

        _texture?.Dispose();
        _texture = null;

        ParentNode?.ChildNodes.Remove(this);
        ParentNode = null;

        lock (_childNodes) _childNodes = [];
        lock (_classList) _classList.Clear();
        lock (_tagsList) _tagsList.Clear();

        _internalId           = null;
        _internalIdCrc32      = 0;
        _internalIdLastIndex  = 0;
        _internalIdLastParent = null;

        MouseCursor.RemoveMouseOver(this);
        FontRegistry.FontChanged -= OnFontConfigurationChanged;
    }

    /// <summary>
    /// Invoked when the node is disposed. This event is meant to be used by
    /// subscribers to perform any necessary cleanup when the node is disposed.
    /// </summary>
    public event Action<Node>? OnDispose;

    /// <summary>
    /// Lifecycle event that is invoked when the node is disposed. This method
    /// is meant to be overridden by derived classes to perform any necessary
    /// cleanup.
    /// </summary>
    protected virtual void OnDisposed() { }

    /// <summary>
    /// Attach an arbitrary object to this node. This can be useful for storing
    /// additional data related to the node without having to create a derived
    /// class.
    /// </summary>
    /// <remarks>
    /// The attached objects are dereferenced when the node is disposed of. If
    /// you need to dispose objects while the node is still alive, you should
    /// nullify the attachment manually.
    /// </remarks>
    /// <param name="key">A unique identifier for this attachment.</param>
    /// <param name="value">The value to attach.</param>
    /// <typeparam name="T"></typeparam>
    public void Attach<T>(string key, T? value) => _attachments[key] = value;

    /// <summary>
    /// Returns the attached object for the given key, or null if no object is
    /// attached for that key, or if the attached object is not of the requested
    /// type.
    /// </summary>
    /// <param name="key">The unique identifier used to identify the attachment.</param>
    /// <typeparam name="T">The type of object to retrieve.</typeparam>
    /// <returns>The stored attachment or NULL if no such attachment exists of the specified key and type.</returns>
    public T? GetAttachment<T>(string key) => _attachments.TryGetValue(key, out var obj) ? (T?)obj : default;

    /// <summary>
    /// Toggles the given class name in the class list of this node. If the
    /// class name is already present in the class list, it is removed. If it is
    /// not present, it is added. If the `enabled` parameter is set to `true`,
    /// the class name is added to the class list. If it is set to `false`, the
    /// class name is removed from the class list.
    /// </summary>
    public void ToggleClass(string className, bool? enabled = null)
    {
        if (enabled == null) {
            if (_classList.Contains(className)) {
                _classList.Remove(className);
            } else {
                _classList.Add(className);
            }
        } else {
            if (enabled.Value) {
                if (!_classList.Contains(className)) _classList.Add(className);
            } else {
                if (_classList.Contains(className)) _classList.Remove(className);
            }
        }
    }

    /// <summary>
    /// Toggles the given tag in the tags list of this node. If the tag is
    /// already present in the tags list, it is removed. If it is not present,
    /// the tag is added. If the `enabled` parameter is set to `true`, the tag
    /// is added to the tags list. If it is set to `false`, the tag is removed
    /// from the tags list.
    /// </summary>
    public void ToggleTag(string tag, bool? enabled = null)
    {
        if (InheritTags) return;

        if (enabled == null) {
            if (_tagsList.Contains(tag)) {
                RemoveTag(tag);
            } else {
                AddTag(tag);
            }
        } else {
            if (enabled.Value) {
                AddTag(tag);
            } else {
                RemoveTag(tag);
            }
        }
    }

    public bool HasTag(string tag)
    {
        return _tagsList.Contains(tag);
    }

    public void AddTag(string tag)
    {
        if (InheritTags) return;

        if (_tagsList.Contains(tag)) return;
        _tagsList.Add(tag);
    }

    public void RemoveTag(string tag)
    {
        if (InheritTags) return;

        if (!_tagsList.Contains(tag)) return;
        _tagsList.Remove(tag);
    }

    public void Clear()
    {
        lock (_childNodes) {
            foreach (var node in _childNodes.ToArray()) {
                node.Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void InheritTagsFromParent()
    {
        if (IsDisposed) return;
        if (Style.IsVisible is false) return;

        if (_inheritTags && ParentNode is not null && !ParentNode.TagsList.SetEquals(_tagsList)) {
            TagsList = ParentNode.TagsList;
        }

        foreach (Node child in _childNodes.ToArray()) {
            child.InheritTagsFromParent();
        }
    }

    private void OnFontConfigurationChanged()
    {
        _textCachedFontId    = null;
        _textCachedFontSize  = null;
        _textCachedNodeSize  = null;
        _textCachedWordWrap  = null;
        _textCachedNodeValue = null;
        _mustReflow          = true;
        _mustRepaint         = true;
    }

    private void HandleChildListChanged(object? _, NotifyCollectionChangedEventArgs e)
    {
        ClearCachedQuerySelectors();

        switch (e) {
            case { Action: NotifyCollectionChangedAction.Add, NewItems: not null }: {
                foreach (Node node in e.NewItems) OnChildAddedToList(node);
                break;
            }
            case { Action: NotifyCollectionChangedAction.Remove, OldItems: not null }: {
                foreach (Node node in e.OldItems) OnChildRemovedFromList(node);
                break;
            }
            case { Action: NotifyCollectionChangedAction.Replace, OldItems: not null, NewItems: not null }: {
                foreach (Node node in e.OldItems!) OnChildRemovedFromList(node);
                foreach (Node node in e.NewItems!) OnChildAddedToList(node);
                break;
            }
            case { Action: NotifyCollectionChangedAction.Reset, OldItems: not null }: {
                foreach (Node node in e.OldItems) OnChildRemovedFromList(node);
                break;
            }
        }

        SignalReflow();
    }

    /// <summary>
    /// Appends the given node to the child list of this node. Does nothing if
    /// the given node is already a child of this node.
    /// </summary>
    /// <remarks>
    /// Removes the node from its existing parent if it has one.
    /// </remarks>
    /// <param name="node">The node to append.</param>
    public void AppendChild(Node node)
    {
        if (_childNodes.Contains(node)) return;

        node.ParentNode?.RemoveChild(this);

        _childNodes.Add(node);
        node.ParentNode = this;
    }

    public void PrependChild(Node node)
    {
        if (_childNodes.Contains(node)) return;

        node.ParentNode?.RemoveChild(this);

        _childNodes.Insert(0, node);
        node.ParentNode = this;
    }

    /// <summary>
    /// Removes this node from its parent. Does nothing if this node has no
    /// parent node.
    /// </summary>
    public void Remove(bool dispose = false)
    {
        ParentNode?.RemoveChild(this, dispose);
    }

    /// <summary>
    /// Removes the given child node from this node. Does nothing if the given
    /// node is not a child of this node.
    /// </summary>
    /// <param name="node">The node to remove.</param>
    /// <param name="dispose">Whether the node should be disposed.</param>
    public void RemoveChild(Node node, bool dispose = false)
    {
        lock (_childNodes) {
            if (!_childNodes.Contains(node)) return;

            _childNodes.Remove(node);

            if (dispose) node.Dispose();
        }
    }

    /// <summary>
    /// Replaces the given old node with the new node. Does nothing if the old
    /// node is not present in the child nodes list.
    /// </summary>
    /// <remarks>
    /// If the new node is already placed somewhere else, it is removed from
    /// that position prior to being added to this node.
    /// </remarks>
    /// <param name="oldChild">A reference to the old node.</param>
    /// <param name="newChild">A reference to the new node.</param>
    public void ReplaceChild(Node oldChild, Node newChild)
    {
        if (!_childNodes.Contains(oldChild)) return;

        ClearQuerySelectorCache();
        ClearCachedQuerySelectors();

        // Remove the new node from its parent if it has one.
        newChild.ParentNode?.RemoveChild(newChild);

        int index = _childNodes.IndexOf(oldChild);

        _childNodes[index]  = newChild;
        oldChild.ParentNode = null;
        newChild.ParentNode = this;

        OnChildRemoved?.Invoke(oldChild);
        OnChildAdded?.Invoke(newChild);
    }

    /// <summary>
    /// Invoked when a child node has been added to the child nodes list.
    /// </summary>
    /// <param name="node">The added node.</param>
    private void OnChildAddedToList(Node node)
    {
        ClearQuerySelectorCache();
        ClearCachedQuerySelectors();

        node.ParentNode?.RemoveChild(node);
        node.ParentNode = this;

        node.OnSortIndexChanged += SortChildren;

        SortChildren();
        OnChildAdded?.Invoke(node);
    }

    /// <summary>
    /// Invoked when a child node has been removed from the child nodes list.
    /// </summary>
    /// <param name="node">The removed node.</param>
    private void OnChildRemovedFromList(Node node)
    {
        ClearQuerySelectorCache();
        ClearCachedQuerySelectors();

        node.ParentNode         =  null;
        node.OnSortIndexChanged -= SortChildren;

        SortChildren();
        OnChildRemoved?.Invoke(node);
    }

    private void SortChildren()
    {
        if (_childNodes.Count < 2) return;

        // Stable sort based on SortIndex. This code is only executed when
        // nodes are added or removed from the node list, or when the
        // SortIndex of a child node has been changed. This is a relatively
        // rare operation that does not happen on every frame.
        _childNodes.CollectionChanged -= HandleChildListChanged;
        _childNodes                   =  new(_childNodes.OrderBy(n => n.SortIndex));
        _childNodes.CollectionChanged += HandleChildListChanged;

        SignalReflow();
    }

    [GeneratedRegex("^[A-Za-z]{1}[A-Za-z0-9_-]+$")]
    private static partial Regex IdentifierNamingRule();
}