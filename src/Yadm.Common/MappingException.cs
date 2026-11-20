namespace Yadm.Common;

/// <summary>
/// Thrown when the <see cref="MappingResult{T}.Value"/> of a failed mapping is read, the way
/// <c>ValidationException</c> reports failed validation. <see cref="Errors"/> lists every problem found.
/// </summary>
public sealed class MappingException : Exception
{
    private const int ErrorsInMessage = 10;

    /// <summary>Creates an exception without errors.</summary>
    public MappingException()
        : base("Mapping failed.")
    {
    }

    /// <summary>Creates an exception with a message and no errors.</summary>
    public MappingException(string? message)
        : base(message, innerException: null)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception, but no errors. Use <see cref="Errors"/> to report</summary>
    public MappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception describing <paramref name="errors"/>.</summary>
    public MappingException(IReadOnlyList<MappingError> errors)
        : this(errors, errors.Count)
    {
    }

    internal MappingException(IReadOnlyList<MappingError> errors, int errorsFound)
        : base(Describe(errors, errorsFound)) => Errors = errors;

    /// <summary>
    /// Every problem the mapping found, up to <see cref="MappingOptions.MaxErrors"/>; a final
    /// <see cref="MappingErrorCodes.TooManyErrors"/> error says how many more there were.
    /// </summary>
    public IReadOnlyList<MappingError> Errors { get; } = [];

    private static string Describe(IReadOnlyList<MappingError> errors, int errorsFound)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var lines = errors.Take(ErrorsInMessage).Select(error => $"  {error}");
        var more = errors.Count > ErrorsInMessage
            ? [$"  ...and {errors.Count - ErrorsInMessage} more."]
            : Array.Empty<string>();
        return string.Join(Environment.NewLine, [$"Mapping failed with {errorsFound} error(s):", .. lines, .. more]);
    }
}
