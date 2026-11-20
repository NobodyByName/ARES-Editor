namespace Ares.Editor.Core.GameData;

/// <summary>
/// A gameplay tag query of a value loot table entry, decoded from its token stream (ARES-Server
/// <c>Ares.Structs.Net.GameplayTags.GameplayTagQuery</c>): tests "any / all / none of these tags" and groups of such
/// expressions. A query tag matches the tag itself and every tag below it; names compare ignoring case.
/// </summary>
public sealed class TagQuery
{
    private const int MaxDepth = 32;

    private readonly Node? _root;

    private TagQuery(Node? root) => _root = root;

    /// <summary>Whether the query matches an object with these tags; an empty query matches nothing.</summary>
    /// <param name="tags">The object's tags.</param>
    /// <returns>True when it matches.</returns>
    public bool Matches(IReadOnlyCollection<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return _root is not null && _root.Evaluate([.. tags.Select(tag => tag.ToLowerInvariant())]);
    }

    /// <summary>Decodes a query.</summary>
    /// <param name="dictionary">Tags the stream refers to by index.</param>
    /// <param name="tokens">The token stream.</param>
    /// <returns>The query.</returns>
    /// <exception cref="FormatException">The stream is malformed.</exception>
    internal static TagQuery Parse(IReadOnlyList<string> dictionary, IReadOnlyList<int> tokens)
    {
        if (tokens.Count == 0)
        {
            return new TagQuery(null);
        }

        string[] names = [.. dictionary.Select(tag => tag.ToLowerInvariant())];
        int position = 0;
        int depth = 0;
        if (Take() != 0)
        {
            throw new FormatException("Unsupported query token version");
        }

        Node? root = Take() switch
        {
            0 => null,
            1 => Expression(),
            _ => throw new FormatException("Invalid query root marker"),
        };
        return position == tokens.Count ? new TagQuery(root) : throw new FormatException("Trailing query tokens");

        int Take() => position < tokens.Count ? tokens[position++] : throw new FormatException("Truncated query");

        Node Expression()
        {
            if (depth > MaxDepth)
            {
                throw new FormatException("Query nesting too deep");
            }

            depth++;
            try
            {
                return Read();
            }
            finally
            {
                depth--;
            }
        }

        Node Read()
        {
            int kind = Take();
            int count = Take();
            switch (kind)
            {
                case 1 or 2 or 3:
                    string[] tags = new string[count];
                    for (int index = 0; index < count; index++)
                    {
                        int tag = Take();
                        tags[index] = tag < names.Length ? names[tag] : throw new FormatException("Query tag index out of range");
                    }

                    return new Test(kind, tags);
                case 4 or 5 or 6:
                    Node[] children = new Node[count];
                    for (int index = 0; index < count; index++)
                    {
                        children[index] = Expression();
                    }

                    return new Group(kind - 3, children);
                default:
                    throw new FormatException("Unsupported query expression");
            }
        }
    }

    // Kind 1 any, 2 all, 3 none (groups use the same numbers after subtracting 3).
    private abstract record Node(int Kind)
    {
        public abstract bool Evaluate(string[] tags);

        protected bool Combine(IEnumerable<bool> results) => Kind switch
        {
            1 => results.Any(result => result),
            2 => results.All(result => result),
            _ => !results.Any(result => result),
        };
    }

    private sealed record Test(int Kind, string[] Tags) : Node(Kind)
    {
        public override bool Evaluate(string[] tags) =>
            Combine(Tags.Select(needed => tags.Any(tag => tag == needed || tag.StartsWith(needed + ".", StringComparison.Ordinal))));
    }

    private sealed record Group(int Kind, Node[] Children) : Node(Kind)
    {
        public override bool Evaluate(string[] tags) => Combine(Children.Select(child => child.Evaluate(tags)));
    }
}
