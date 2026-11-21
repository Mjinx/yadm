using System.Globalization;

namespace Yadm.Common;

/// <summary>Settings for running a mapping. The defaults suit most applications.</summary>
public sealed record MappingOptions
{
    /// <summary>Collects every error (keeping up to 200) and parses with the invariant culture.</summary>
    public static MappingOptions Default { get; } = new();

    /// <summary>Stops at the first error, so a failed result holds exactly one error.</summary>
    public static MappingOptions FailFast { get; } = new() { StopOnFirstError = true };

    /// <summary>
    /// Stops the mapping at the first error: built-in scalar and nested helpers do no further work, no further
    /// collection items are read, and <see cref="MappingContext.HasErrors"/> becomes true everywhere. Caller argument
    /// evaluation, arbitrary mapper statements, loops and I/O still require explicit <see cref="MappingContext.IsStopped"/>
    /// checks. Defaults to false, which collects every error.
    /// </summary>
    public bool StopOnFirstError { get; init; }

    /// <summary>
    /// The number of errors a result lists. Mapping continues past it: further errors are counted but not listed,
    /// and one final <see cref="MappingErrorCodes.TooManyErrors"/> error says how many were left out. Defaults to 200,
    /// the limit ASP.NET Core applies to model validation, so hostile input cannot produce huge error lists.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxErrors
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 200;

    /// <summary>
    /// The culture used to parse numbers and dates. Defaults to <see cref="CultureInfo.InvariantCulture"/>,
    /// so results do not depend on the server's settings.
    /// </summary>
    /// <exception cref="ArgumentNullException">The assigned provider is null.</exception>
    public IFormatProvider FormatProvider
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(FormatProvider));
            field = value;
        }
    } = CultureInfo.InvariantCulture;
}
