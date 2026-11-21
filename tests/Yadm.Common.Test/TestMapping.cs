using System.Diagnostics.CodeAnalysis;

namespace Yadm.Common.Test;

/// <summary>Runs code inside a mapping, so helper tests need no mapper class.</summary>
internal static class TestMapping
{
    public static MappingResult<bool> Run(Action<MappingContext> body, MappingOptions? options = null) =>
        Mapper.Create((object? _, MappingContext context) =>
        {
            body(context);
            return true;
        }).Map(null, options);

    public static IReadOnlyList<MappingError> Errors(Action<MappingContext> body, MappingOptions? options = null) =>
        Run(body, options).Errors;
}

/// <summary>A value object that parses itself, so <c>Parse&lt;SkuCode&gt;</c> works like <c>Parse&lt;int&gt;</c>.</summary>
public sealed record SkuCode(string Value) : IParsable<SkuCode>
{
    public static SkuCode Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var code) ? code : throw new FormatException($"'{s}' is not a valid SKU.");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
        [MaybeNullWhen(false)] out SkuCode result)
    {
        result = s is { Length: > 0 } && s.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '-') ? new SkuCode(s) : null;
        return result is not null;
    }

    public static bool TryCreate(int number, [MaybeNullWhen(false)] out SkuCode result)
    {
        result = number >= 0 ? new SkuCode($"SKU-{number}") : null;
        return result is not null;
    }
}
