namespace Yadm;

/// <summary>The codes reported by the built-in helpers. Use your own codes for domain rules.</summary>
public static class MappingErrorCodes
{
    /// <summary>A required value is null, empty or whitespace.</summary>
    public const string Required = nameof(Required);

    /// <summary>A value is present but cannot be parsed or converted.</summary>
    public const string Invalid = nameof(Invalid);

    /// <summary>A key does not match any known entity.</summary>
    public const string NotFound = nameof(NotFound);

    /// <summary>
    /// More errors were found than <see cref="MappingOptions.MaxErrors"/> allows a result to keep; the message says
    /// how many were left out.
    /// </summary>
    public const string TooManyErrors = nameof(TooManyErrors);
}
