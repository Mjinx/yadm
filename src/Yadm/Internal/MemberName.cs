using System.Collections.Concurrent;

namespace Yadm;

/// <summary>Turns a <c>CallerArgumentExpression</c> into the member path used in error paths.</summary>
/// <remarks>
/// <para>Rules, in order:</para>
/// <list type="number">
/// <item>An explicit name is used as given; it may not contain whitespace (that is usually a message passed by
/// mistake).</item>
/// <item>The expression's leading member access is read, including literal indexers: <c>src.Lines[0].Sku</c>.
/// Whitespace, line breaks, <c>?.</c> and <c>!</c> are allowed, and so is a trailing <c>?? fallback</c>.</item>
/// <item>A name starting with an uppercase letter is a member and is kept (<c>Street</c>, <c>ShipTo.Street</c>);
/// anything else is a variable or parameter and adds nothing (<c>src.Street</c> is <c>Street</c>,
/// <c>customerId</c> is nothing, <c>row[1]</c> is <c>[1]</c>).</item>
/// <item>Trailing method calls are ignored (<c>src.Note?.Trim()</c> is <c>Note</c>). For a collection, only calls
/// that keep item positions are (<c>ToList</c>, <c>ToArray</c>, ...): after <c>src.Rows.Skip(1)</c> an item's
/// index is no longer its source position.</item>
/// <item>Anything else is unknown and adds nothing: a member read from a call's result, a computed index, or an
/// operator such as <c>a ? b : c</c>.</item>
/// </list>
/// </remarks>
internal static class MemberName
{
    private const int CacheLimit = 10_000;

    private static readonly ConcurrentDictionary<string, string?> ValueCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string?> CollectionCache = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Keywords =
    [
        "null", "true", "false", "default", "new", "typeof", "nameof", "sizeof",
        "checked", "unchecked", "await", "throw", "stackalloc"
    ];

    private static readonly HashSet<string> PositionKeepingCalls =
        ["ToList", "ToArray", "AsEnumerable", "ToImmutableArray", "ToImmutableList", "AsReadOnly"];

    /// <summary>The member a single value's argument names.</summary>
    public static string? From(string? name, string? expression) =>
        Resolve(name, expression, ValueCache, collection: false);

    /// <summary>The member a collection's argument names, when its item positions are still source positions.</summary>
    public static string? ForCollection(string? name, string? expression) =>
        Resolve(name, expression, CollectionCache, collection: true);

    private static string? Resolve(
        string? name,
        string? expression,
        ConcurrentDictionary<string, string?> cache,
        bool collection)
    {
        if (name is not null)
        {
            if (name.AsSpan().ContainsAny(" \t\r\n"))
            {
                throw new ArgumentException(
                    $"'{name}' is not a member name. To change an error's message, report it with Reject or AddError.",
                    nameof(name));
            }

            return name.Length == 0 ? null : name;
        }

        if (string.IsNullOrWhiteSpace(expression))
            return null;
        if (cache.TryGetValue(expression, out var cached))
            return cached;

        var parsed = Parse(expression, collection);
        if (cache.Count < CacheLimit)
            cache.TryAdd(expression, parsed);
        return parsed;
    }

    private static string? Parse(string text, bool collection)
    {
        List<string> segments = [];
        var i = 0;
        while (true)
        {
            i = SkipSpace(text, i);
            if (i < text.Length && text[i] == '@')
                i++;
            var identifier = ReadIdentifier(text, ref i);
            if (identifier is null)
                return segments.Count == 0 ? null : EndsChain(text, i) ? Join(segments) : null;

            if (TrySkipCall(text, ref i))
            {
                return (!collection || PositionKeepingCalls.Contains(identifier)) &&
                       OnlyCallsFollow(text, i, collection)
                    ? Join(segments)
                    : null;
            }

            var indexers = ReadIndexers(text, ref i);
            if (indexers is null)
                return null;

            segments.Add(identifier + indexers);
            if (!TrySkipSeparator(text, ref i))
                return EndsChain(text, i) ? Join(segments) : null;
        }
    }

    private static string? Join(List<string> segments)
    {
        if (segments.Count == 0)
            return null;

        var root = segments[0];
        var bracket = root.IndexOf('[');
        var rootName = bracket < 0 ? root : root[..bracket];
        if (Keywords.Contains(rootName))
            return null;

        var head = char.IsUpper(rootName[0]) ? root : root[rootName.Length..];
        var tail = string.Join('.', segments.Skip(1));
        if (head.Length == 0)
            return tail.Length == 0 ? null : tail;
        return tail.Length == 0 ? head : $"{head}.{tail}";
    }

    /// <summary>After a call, more calls may follow (<c>src.Name.Trim().ToUpper()</c>) but not a member.</summary>
    private static bool OnlyCallsFollow(string text, int i, bool collection)
    {
        while (TrySkipSeparator(text, ref i))
        {
            var identifier = ReadIdentifier(text, ref i);
            if (identifier is null || !TrySkipCall(text, ref i))
                return false;
            if (collection && !PositionKeepingCalls.Contains(identifier))
                return false;
        }

        return EndsChain(text, i);
    }

    /// <summary>The chain may end the expression, or be followed by <c>?? fallback</c>; any other operator makes it unknown.</summary>
    private static bool EndsChain(string text, int i)
    {
        var j = SkipNullForgiving(text, i);
        return j == text.Length || text.AsSpan(j).StartsWith("??");
    }

    private static string? ReadIdentifier(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            i++;
        return i == start || char.IsDigit(text[start]) ? null : text[start..i];
    }

    /// <summary>Reads literal indexers such as <c>[0]</c>; returns null for any other index.</summary>
    private static string? ReadIndexers(string text, ref int i)
    {
        var indexers = "";
        while (true)
        {
            var j = SkipNullForgiving(text, i);
            if (!StartsIndexer(text, j))
                return indexers;
            if (text[j] == '?')
                j++;

            var close = text.IndexOf(']', j);
            if (close < 0)
                return null;
            var index = text.AsSpan(j + 1, close - j - 1).Trim();
            if (index.IsEmpty || index.ContainsAnyExceptInRange('0', '9'))
                return null;

            indexers += $"[{index}]";
            i = close + 1;
        }
    }

    private static bool TrySkipCall(string text, ref int i)
    {
        var j = i;
        if (j < text.Length && text[j] == '<')
        {
            j = SkipBalanced(text, j, '<', '>');
            if (j < 0)
                return false;
        }

        j = SkipSpace(text, j);
        if (j >= text.Length || text[j] != '(')
            return false;

        j = SkipBalanced(text, j, '(', ')');
        if (j < 0)
            return false;

        i = j;
        return true;
    }

    private static bool TrySkipSeparator(string text, ref int i)
    {
        var j = SkipNullForgiving(text, i);
        if (j < text.Length && text[j] == '.')
            j++;
        else if (j + 1 < text.Length && text[j] == '?' && text[j + 1] == '.')
            j += 2;
        else
            return false;

        i = SkipSpace(text, j);
        return true;
    }

    private static bool StartsIndexer(string text, int i) =>
        i < text.Length && (text[i] == '[' || text[i] == '?' && i + 1 < text.Length && text[i + 1] == '[');

    private static int SkipBalanced(string text, int i, char open, char close)
    {
        var depth = 0;
        for (; i < text.Length; i++)
        {
            if (text[i] is '"' or '\'')
            {
                i = SkipQuoted(text, i);
                if (i < 0)
                    return -1;
            }
            else if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close && --depth == 0)
            {
                return i + 1;
            }
        }

        return -1;
    }

    private static int SkipQuoted(string text, int i)
    {
        var quote = text[i];
        for (i++; i < text.Length; i++)
        {
            if (text[i] == '\\')
                i++;
            else if (text[i] == quote)
                return i;
        }

        return -1;
    }

    private static int SkipSpace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        return i;
    }

    private static int SkipNullForgiving(string text, int i)
    {
        while (i < text.Length && (text[i] == '!' || char.IsWhiteSpace(text[i])))
            i++;
        return i;
    }
}
