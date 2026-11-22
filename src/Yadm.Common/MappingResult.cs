using System.Diagnostics.CodeAnalysis;

namespace Yadm.Common;

/// <summary>
/// The outcome of a mapping: the mapped <see cref="Value"/> when <see cref="IsSuccess"/> is true,
/// otherwise every problem found in <see cref="Errors"/>.
/// </summary>
/// <typeparam name="T">The type of the mapped value.</typeparam>
public sealed class MappingResult<T>
{
    private MappingResult(T? value, IReadOnlyList<MappingError> errors, int errorsFound, bool hasPartialValue) =>
        (PartialValue, Errors, ErrorCount, HasPartialValue) = (value, errors, errorsFound, hasPartialValue);

    /// <summary>True when the mapping found no errors, so <see cref="Value"/> can be read.</summary>
    public bool IsSuccess => ErrorCount == 0;

    /// <summary>The errors reported by the mapping, excluding a possible overflow summary. Zero on success.</summary>
    public int ErrorCount { get; }

    /// <summary>Every problem the mapping found, in the order found. Empty on success. The snapshot is read-only.</summary>
    public IReadOnlyList<MappingError> Errors { get; }

    /// <summary>The mapped value.</summary>
    /// <exception cref="MappingException">The mapping failed; the exception lists the errors.</exception>
    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations",
        Justification =
            "Reading the value of a failed mapping is documented to throw, and the exception lists the errors.")]
    public T Value => IsSuccess ? PartialValue! : throw new MappingException(Errors, ErrorCount);

    /// <summary>
    /// The mapped value, even when the mapping failed: on success it equals <see cref="Value"/>; on failure it is the
    /// value built from the valid items when invalid items were skipped, or <see langword="default"/>. Read it only
    /// when a partial value is acceptable, such as when importing the valid rows of a file.
    /// </summary>
    public T? PartialValue { get; }

    /// <summary>
    /// True when a failed mapping accepted and retained <see cref="PartialValue"/>. False on success and on failures
    /// that discarded their value. A partial value can be a default value or null.
    /// </summary>
    public bool HasPartialValue { get; }

    /// <summary>Creates a successful result, for example to stub a service in a test.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is a null reference.</exception>
    public static MappingResult<T> Success(T value)
    {
        if (value is null && !typeof(T).IsValueType)
            throw new ArgumentNullException(nameof(value), "A successful mapping needs a value.");

        return new MappingResult<T>(value, Array.AsReadOnly(Array.Empty<MappingError>()), 0, false);
    }

    /// <summary>Creates a failed result, for example to stub a service in a test.</summary>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or contains null.</exception>
    public static MappingResult<T> Failure(IEnumerable<MappingError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.ToArray();
        return list.Length == 0 || Array.IndexOf(list, null) >= 0
            ? throw new ArgumentException("A failed mapping needs at least one error, and no null errors.",
                nameof(errors))
            : new MappingResult<T>(default, Array.AsReadOnly(list), list.Length, false);
    }

    internal static MappingResult<T> Failure(T? partialValue, List<MappingError> errors, int errorsFound,
        bool hasPartialValue) =>
        new(partialValue, errors.AsReadOnly(), errorsFound, hasPartialValue);

    /// <summary>
    /// Groups the error messages by path: each key is an error path and each value lists its messages.
    /// </summary>
    public IDictionary<string, string[]> ToDictionary() =>
        Errors.GroupBy(error => error.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(error => error.Message).ToArray(),
                StringComparer.Ordinal
            );

    /// <summary>Describes the outcome, such as <c>Success: Order</c> or <c>Failure: 2 error(s)</c>.</summary>
    public override string ToString() => IsSuccess
        ? $"Success: {PartialValue}"
        : $"Failure: {ErrorCount} error(s)";
}
