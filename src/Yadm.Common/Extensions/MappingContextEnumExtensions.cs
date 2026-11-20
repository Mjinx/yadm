using System.Runtime.CompilerServices;

namespace Yadm.Common;

/// <summary>
/// Parses enums with the same <c>Parse</c>, <c>TryParse</c> and <c>ParseOptional</c> calls used for other types:
/// <c>context.Parse&lt;OrderType&gt;(src.Type)</c>.
/// </summary>
/// <remarks>
/// Names are matched case-insensitively and surrounding whitespace is ignored. Numbers are accepted only when they
/// are defined members; flag combinations must be declared members too. Invalid text is reported with the list of
/// accepted names. (Enums are not <see cref="IParsable{TSelf}"/>, so these overloads live in their own class.)
/// </remarks>
public static class MappingContextEnumExtensions
{
    /// <summary>Parses required text into a defined member of <typeparamref name="TEnum"/>.</summary>
    /// <param name="context">The context to report to.</param>
    /// <param name="text">The text to parse.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TEnum Parse<TEnum>(this MappingContext context, string? text, string? name = null,
        [CallerArgumentExpression(nameof(text))] string expression = "") where TEnum : struct, Enum =>
        context.TryParse<TEnum>(text, out var value, name, expression) ? value : default;

    /// <summary>Like <c>Parse</c>, but also tells you whether the text named a defined member.</summary>
    /// <param name="context">The context to report to.</param>
    /// <param name="text">The text to parse.</param>
    /// <param name="result">The parsed member, when parsing succeeded.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static bool TryParse<TEnum>(this MappingContext context, string? text, out TEnum result, string? name = null,
        [CallerArgumentExpression(nameof(text))] string expression = "") where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(context);
        if (MappingRules.IsAbsent(text))
        {
            context.Reject(text, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
            result = default;
            return false;
        }

        if (Enum.TryParse(text, ignoreCase: true, out result) && Enum.IsDefined(result))
            return true;

        context.Reject(text, MappingErrorCodes.Invalid,
            $"{MappingRules.InvalidMessage<TEnum>(text)} Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.",
            name, expression);
        return false;
    }

    /// <summary>Parses optional text into a defined member of <typeparamref name="TEnum"/>; absent text returns null.</summary>
    /// <param name="context">The context to report to.</param>
    /// <param name="text">The text to parse.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TEnum? ParseOptional<TEnum>(this MappingContext context, string? text, string? name = null,
        [CallerArgumentExpression(nameof(text))] string expression = "") where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(context);
        if (MappingRules.IsAbsent(text))
            return null;
        return context.TryParse<TEnum>(text, out var value, name, expression) ? value : null;
    }
}
