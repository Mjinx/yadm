using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Yadm.Common;

/// <summary>Rules shared by the built-in helpers, so every helper treats absence, parsing and errors alike.</summary>
internal static class MappingRules
{
    public const string RequiredMessage = "A value is required.";

    /// <summary>Null, empty and whitespace strings are absent, as with DataAnnotations' <c>[Required]</c>.</summary>
    public static bool IsAbsent<T>([NotNullWhen(false)] T? value) =>
        value is null ||
        (
            value is string text &&
            string.IsNullOrWhiteSpace(text)
        );

    public static string InvalidMessage<T>(object? value) => $"'{value}' is not a valid {typeof(T).Name}.";

    /// <summary>
    /// Parses with <see cref="IParsable{TSelf}"/>, made strict where .NET's defaults would silently misread input:
    /// fractional numbers accept no thousands separators ("1,5" is not 15) and must be finite, and dates must be
    /// ISO 8601 ("05/11/2026" means different days in different countries). Dates without an offset are UTC.
    /// </summary>
    public static bool TryParse<T>(string text, IFormatProvider provider, [MaybeNullWhen(false)] out T value)
        where T : IParsable<T>
    {
        if (typeof(T) == typeof(decimal))
            return Cast(decimal.TryParse(text, NumberStyles.Float, provider, out var number), number, out value);
        if (typeof(T) == typeof(double))
        {
            return Cast(double.TryParse(text, NumberStyles.Float, provider, out var number) && double.IsFinite(number),
                number, out value);
        }

        if (typeof(T) == typeof(float))
        {
            return Cast(float.TryParse(text, NumberStyles.Float, provider, out var number) && float.IsFinite(number),
                number, out value);
        }

        if (typeof(T) == typeof(Half))
        {
            return Cast(Half.TryParse(text, NumberStyles.Float, provider, out var number) && Half.IsFinite(number),
                number, out value);
        }

        if (typeof(T) == typeof(DateTimeOffset))
        {
            var date = default(DateTimeOffset);
            var parsed = StartsWithIsoDate(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out date);
            return Cast(parsed, date, out value);
        }

        if (typeof(T) == typeof(DateTime))
        {
            var date = default(DateTime);
            var parsed = StartsWithIsoDate(text) && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
            return Cast(parsed, date, out value);
        }

        if (typeof(T) == typeof(DateOnly))
        {
            var date = default(DateOnly);
            var parsed = StartsWithIsoDate(text) &&
                         DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            return Cast(parsed, date, out value);
        }

        return T.TryParse(text, provider, out value);
    }

    /// <summary>Returns a value produced without errors, rejecting a null that no error explains.</summary>
    public static T Produced<T>(T? value, object? producer)
    {
        if (value is null && !typeof(T).IsValueType)
        {
            throw new InvalidOperationException(
                $"{Describe(producer)} returned null without reporting an error. Report an error to the " +
                "MappingContext, or use MapOptional when the value may be absent.");
        }

        return value!;
    }

    public static string Describe(object? producer)
    {
        if (producer is null or Delegate or IMappingFunction)
            return "The mapping function";

        var name = producer.GetType().Name;
        var arity = name.IndexOf('`');
        return arity < 0 ? name : name[..arity];
    }

    private static bool Cast<TParsed, T>(bool parsed, TParsed parsedValue, [MaybeNullWhen(false)] out T value)
    {
        value = parsed ? (T)(object)parsedValue! : default;
        return parsed;
    }

    /// <summary>True when the text starts with an ISO 8601 date, <c>yyyy-MM-dd</c>.</summary>
    private static bool StartsWithIsoDate(string text)
    {
        var date = text.AsSpan().TrimStart();
        return date.Length >= 10
               && char.IsAsciiDigit(date[0]) && char.IsAsciiDigit(date[1]) && char.IsAsciiDigit(date[2])
               && char.IsAsciiDigit(date[3]) && date[4] == '-' && char.IsAsciiDigit(date[5])
               && char.IsAsciiDigit(date[6]) && date[7] == '-' && char.IsAsciiDigit(date[8])
               && char.IsAsciiDigit(date[9]);
    }
}
