using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Yadm;

/// <summary>Rules shared by the built-in helpers, so every helper treats absence, parsing and errors alike.</summary>
internal static class MappingRules
{
    public const string RequiredMessage = "A value is required.";

    /// <summary>Null, empty and whitespace strings are absent.</summary>
    public static bool IsAbsent<T>([NotNullWhen(false)] T? value) =>
        value is null ||
        (
            value is string text &&
            string.IsNullOrWhiteSpace(text)
        );

    public static string InvalidMessage<T>() => $"The value is not a valid {typeof(T).Name}.";

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
            var parsed = TryParseIsoDateTime(text, out var date);
            return Cast(parsed, date, out value);
        }

        if (typeof(T) == typeof(DateTime))
        {
            var parsed = TryParseIsoDateTime(text, out var offset);
            var date = parsed ? offset.UtcDateTime : default;
            return Cast(parsed, date, out value);
        }

        if (typeof(T) == typeof(DateOnly))
        {
            var date = default(DateOnly);
            var parsed = text.Length == 10 &&
                         DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                             DateTimeStyles.None, out date);
            return Cast(parsed, date, out value);
        }

        var succeeded = T.TryParse(text, provider, out value);
        if (succeeded)
            value = ParserResult(value);
        return succeeded;
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

    /// <summary>Returns a successful parser result, rejecting a null reference that violates TryParse's contract.</summary>
    public static T ParserResult<T>(T? value)
    {
        if (value is null && !typeof(T).IsValueType)
        {
            throw new InvalidOperationException(
                "A parser returned true with a null reference result. Return false for invalid input, " +
                "or return a usable value for a required parse.");
        }

        return value!;
    }

    public static string Describe(object? producer)
    {
        if (producer is null or Delegate or IMappingFunction)
        {
            return "The mapping function";
        }

        var name = producer.GetType().Name;
        var arity = name.IndexOf('`');
        return arity < 0 ? name : name[..arity];
    }

    private static bool Cast<TParsed, T>(bool parsed, TParsed parsedValue, [MaybeNullWhen(false)] out T value)
    {
        value = parsed ? (T)(object)parsedValue! : default;
        return parsed;
    }

    private static bool TryParseIsoDateTime(string text, out DateTimeOffset value)
    {
        value = default;
        var input = text.AsSpan();
        if (input.Length != 10 && (input.Length < 19 || input[10] != 'T'))
        {
            return false;
        }

        if (input.Length < 10 || input[4] != '-' || input[7] != '-'
                              || !DateTime.TryParseExact(input[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out var date))
        {
            return false;
        }

        if (input.Length == 10)
        {
            value = new DateTimeOffset(date, TimeSpan.Zero);
            return true;
        }

        if (input[13] != ':' || input[16] != ':' ||
            !TryReadTwoDigits(input, 11, out var hour) ||
            !TryReadTwoDigits(input, 14, out var minute) ||
            !TryReadTwoDigits(input, 17, out var second) ||
            hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        long fractionTicks = 0;
        var index = 19;
        if (index < input.Length && input[index] == '.')
        {
            index++;
            var fractionStart = index;
            while (index < input.Length && char.IsAsciiDigit(input[index]))
                index++;
            var digits = index - fractionStart;
            if (digits is < 1 or > 16)
            {
                return false;
            }

            var scale = 1_000_000L;
            for (var position = fractionStart; position < fractionStart + Math.Min(digits, 7); position++)
            {
                fractionTicks += (input[position] - '0') * scale;
                scale /= 10;
            }
        }

        var offset = TimeSpan.Zero;
        if (index < input.Length)
        {
            var suffix = input[index];
            if (suffix == 'Z')
            {
                if (index + 1 != input.Length)
                {
                    return false;
                }
            }
            else
            {
                if ((suffix != '+' && suffix != '-') || input.Length != index + 6 || input[index + 3] != ':' ||
                    !TryReadTwoDigits(input, index + 1, out var offsetHour) ||
                    !TryReadTwoDigits(input, index + 4, out var offsetMinute) ||
                    offsetHour > 14 || offsetMinute > 59 || (offsetHour == 14 && offsetMinute != 0))
                {
                    return false;
                }

                offset = new TimeSpan(offsetHour, offsetMinute, 0);
                if (suffix == '-')
                {
                    offset = -offset;
                }
            }
        }

        var localTicks = date.Ticks + new TimeSpan(hour, minute, second).Ticks + fractionTicks;
        var utcTicks = localTicks - offset.Ticks;
        if (utcTicks < DateTimeOffset.MinValue.UtcTicks || utcTicks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        value = new DateTimeOffset(localTicks, offset);
        return true;
    }

    private static bool TryReadTwoDigits(ReadOnlySpan<char> input, int index, out int value)
    {
        value = 0;
        if (index + 1 >= input.Length || !char.IsAsciiDigit(input[index]) ||
            !char.IsAsciiDigit(input[index + 1]))
        {
            return false;
        }

        value = (input[index] - '0') * 10 + input[index + 1] - '0';
        return true;
    }
}
