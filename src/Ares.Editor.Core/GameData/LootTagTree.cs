namespace Ares.Editor.Core.GameData;

/// <summary>One segment of the container tag hierarchy (<c>Socket</c>, <c>Socket.LootContainer</c>, ...).</summary>
public sealed class LootTagNode
{
    private readonly List<LootTagNode> _children = [];

    internal LootTagNode(string segment, string tag)
    {
        Segment = segment;
        Tag = tag;
    }

    /// <summary>The last segment of <see cref="Tag"/>.</summary>
    public string Segment { get; }

    /// <summary>The full tag, spelled as the first container type below it spells it.</summary>
    public string Tag { get; }

    /// <summary>The container type whose tag is this one (ignoring case), or null for a tag that only groups others.</summary>
    public LootContainerType? ContainerType { get; private set; }

    /// <summary>The tags directly below this one, ordered by segment (ordinal, ignoring case).</summary>
    public IReadOnlyList<LootTagNode> Children => _children;

    internal void SetType(LootContainerType type) => ContainerType ??= type;

    internal void Add(LootTagNode child)
    {
        int index = _children.FindIndex(other => string.Compare(other.Segment, child.Segment, StringComparison.OrdinalIgnoreCase) > 0);
        _children.Insert(index < 0 ? _children.Count : index, child);
    }
}

/// <summary>
/// The container tag hierarchy of the game data: every container type's tag and the tags above it. A rule or chance for
/// a tag covers that tag and every tag below it. Tags compare without case, as the game compares them.
/// </summary>
public sealed class LootTagTree
{
    private readonly Dictionary<string, LootTagNode> _byTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly LootTagNode _root = new(string.Empty, string.Empty);

    /// <summary>Builds the hierarchy of the container types' tags.</summary>
    /// <param name="types">The container types.</param>
    public LootTagTree(IEnumerable<LootContainerType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        foreach (LootContainerType type in types)
        {
            LootTagNode parent = _root;
            string[] segments = type.Tag.Split('.');
            for (int depth = 0; depth < segments.Length; depth++)
            {
                string tag = string.Join('.', segments, 0, depth + 1);
                if (!_byTag.TryGetValue(tag, out LootTagNode? node))
                {
                    node = new LootTagNode(segments[depth], tag);
                    _byTag[tag] = node;
                    parent.Add(node);
                }

                parent = node;
            }

            parent.SetType(type);
        }
    }

    /// <summary>The top-level tags (normally just <c>Socket</c>).</summary>
    public IReadOnlyList<LootTagNode> Roots => _root.Children;

    /// <summary>
    /// The broad kinds of container: the tags at the first level where the hierarchy branches (below
    /// <c>Socket.LootContainer</c> in the game's data: <c>Commercial</c>, <c>Industrial</c>, ...), in tree order.
    /// </summary>
    public IReadOnlyList<LootTagNode> Categories
    {
        get
        {
            IReadOnlyList<LootTagNode> level = _root.Children;
            while (level is [{ Children.Count: > 0 } single])
            {
                level = single.Children;
            }

            return level;
        }
    }

    /// <summary>The node of a tag, ignoring case, or null when no container type's tag is it or lies below it.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The node, or null.</returns>
    public LootTagNode? Find(string tag) => _byTag.GetValueOrDefault(tag);

    /// <summary>The category (see <see cref="Categories"/>) a tag is or lies below, ignoring case.</summary>
    /// <param name="tag">A container tag.</param>
    /// <returns>The category's node, or null when the tag lies above the categories or outside the hierarchy.</returns>
    public LootTagNode? CategoryOf(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return Categories.FirstOrDefault(category =>
            tag.StartsWith(category.Tag, StringComparison.OrdinalIgnoreCase) && (tag.Length == category.Tag.Length || tag[category.Tag.Length] == '.'));
    }
}
