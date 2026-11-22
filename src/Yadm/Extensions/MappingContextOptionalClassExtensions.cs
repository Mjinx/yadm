using System.Runtime.CompilerServices;

namespace Yadm;

/// <summary>
/// Lets <c>ParseOptional</c> return a nullable reference for <see cref="IParsable{TSelf}"/> classes, such as your own
/// value objects. (C# cannot declare the class and struct versions side by side, so this one lives here.)
/// </summary>
public static class MappingContextOptionalClassExtensions
{
    /// <summary>
    /// Parses optional text into an <see cref="IParsable{TSelf}"/> class. Absent text returns null without an error;
    /// text that is present but invalid reports <see cref="MappingErrorCodes.Invalid"/>.
    /// </summary>
    /// <param name="context">The context to report to.</param>
    /// <param name="text">The text to parse.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static T? ParseOptional<T>(this MappingContext context, string? text, string? name = null,
        [CallerArgumentExpression(nameof(text))] string expression = "") where T : class, IParsable<T>
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsStopped)
            return null;

        if (MappingRules.IsAbsent(text))
            return null;
        return context.TryParse<T>(text, out var value, name, expression) ? value : null;
    }
}
