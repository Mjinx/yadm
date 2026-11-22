namespace Yadm;

/// <summary>
/// Thrown when the <see cref="MappingResult{T}.Value"/> of a failed mapping is read.
/// <see cref="Errors"/> lists every problem found.
/// </summary>
public sealed class MappingException : Exception
{
    private const int ErrorsInMessage = 10;
    private static readonly IReadOnlyList<MappingError> EmptyErrors =
        Array.AsReadOnly(Array.Empty<MappingError>());

    /// <summary>Creates an exception without errors.</summary>
    public MappingException()
        : base("Mapping failed.") => Errors = EmptyErrors;

    /// <summary>Creates an exception with a message and no errors.</summary>
    public MappingException(string? message)
        : base(message, innerException: null) => Errors = EmptyErrors;

    /// <summary>Creates an exception with a message and an inner exception, but no errors. Use <see cref="Errors"/> to report</summary>
    public MappingException(string message, Exception innerException)
        : base(message, innerException) => Errors = EmptyErrors;

    /// <summary>Creates an exception describing <paramref name="errors"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or contains null.</exception>
    public MappingException(IReadOnlyList<MappingError> errors)
        : this(Snapshot(errors))
    {
    }

    private MappingException(System.Collections.ObjectModel.ReadOnlyCollection<MappingError> snapshot)
        : this(snapshot, snapshot.Count)
    {
    }

    internal MappingException(IReadOnlyList<MappingError> errors, int errorsFound)
        : base(Describe(errors, errorsFound)) => Errors = errors;

    /// <summary>
    /// Every problem the mapping found, up to <see cref="MappingOptions.MaxErrors"/>; a final
    /// <see cref="MappingErrorCodes.TooManyErrors"/> error says how many more there were. The snapshot is read-only.
    /// </summary>
    public IReadOnlyList<MappingError> Errors { get; }

    private static System.Collections.ObjectModel.ReadOnlyCollection<MappingError> Snapshot(
        IReadOnlyList<MappingError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var snapshot = errors.ToArray();
        return snapshot.Length == 0 || Array.IndexOf(snapshot, null) >= 0
            ? throw new ArgumentException(
                "A mapping exception needs at least one error, and no null errors.", nameof(errors))
            : Array.AsReadOnly(snapshot);
    }

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
