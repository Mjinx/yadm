using System.Globalization;

namespace Yadm.Common;

/// <summary>Value checks that do not depend on mapping paths or failure policy.</summary>
public static class MappingChecks
{
    public static CheckResult<T> Require<T>(T? v) where T : class =>
        v is null ? CheckResult<T>.Failure(new AbsentValue()) : CheckResult<T>.Success(v);

    public static CheckResult<T> Require<T>(T? v) where T : struct =>
        v is { } value ? CheckResult<T>.Success(value) : CheckResult<T>.Failure(new AbsentValue());

    public static CheckResult<string> RequireText(string? v) =>
        string.IsNullOrWhiteSpace(v)
            ? CheckResult<string>.Failure(new AbsentValue())
            : CheckResult<string>.Success(v);

    public static CheckResult<T> ParseRequired<T>(string? raw) where T : struct, ISpanParsable<T> =>
        ParseRequired(raw, ParseInvariant<T>);

    public static CheckResult<T> ParseOptional<T>(string? raw) where T : struct, ISpanParsable<T> =>
        ParseOptional(raw, ParseInvariant<T>);

    /// <summary>
    /// Checks presence and invokes a supplied parser. By default, null and blank strings
    /// are absent; an optional predicate replaces that rule for domain-specific inputs.
    /// A false parser result produces MalformedValue and discards the returned value.
    /// Parser exceptions propagate rather than being converted to malformed values.
    /// </summary>
    public static CheckResult<T> ParseRequired<TInput, T>(
        TInput raw,
        Func<TInput, (bool Success, T Value)> parser,
        Func<TInput, bool>? isAbsent = null) =>
        Parse(raw, parser, isAbsent, required: true);

    /// <summary>Like ParseRequired, but absent input returns an absent result without an error.</summary>
    public static CheckResult<T> ParseOptional<TInput, T>(
        TInput raw,
        Func<TInput, (bool Success, T Value)> parser,
        Func<TInput, bool>? isAbsent = null) =>
        Parse(raw, parser, isAbsent, required: false);

    private static CheckResult<T> Parse<TInput, T>(
        TInput raw,
        Func<TInput, (bool Success, T Value)> parser,
        Func<TInput, bool>? isAbsent,
        bool required)
    {
        ArgumentNullException.ThrowIfNull(parser);

        if (isAbsent is null ? IsAbsent(raw) : isAbsent(raw))
            return required ? CheckResult<T>.Failure(new AbsentValue()) : default;

        var (success, value) = parser(raw);
        return success
            ? CheckResult<T>.Success(value)
            : CheckResult<T>.Failure(new MalformedValue(raw, typeof(T).Name));
    }

    private static bool IsAbsent<T>(T raw) =>
        raw is null || raw is string text && string.IsNullOrWhiteSpace(text);

    private static (bool Success, T Value) ParseInvariant<T>(string? raw) where T : struct, ISpanParsable<T>
    {
        var success = T.TryParse(raw, CultureInfo.InvariantCulture, out var value);
        return (success, value);
    }
}
