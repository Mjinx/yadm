using System.Diagnostics.CodeAnalysis;

namespace Yadm.Common;

/// <summary>
/// The outcome of a mapping: the mapped <see cref="Value"/> when <see cref="IsSuccess"/> is true,
/// otherwise every problem found in <see cref="Errors"/>.
/// </summary>
/// <typeparam name="T">The type of the mapped value.</typeparam>
public sealed class MappingResult<T>
{
    private readonly int _errorsFound;

    private MappingResult(T? value, IReadOnlyList<MappingError> errors, int errorsFound) =>
        (PartialValue, Errors, _errorsFound) = (value, errors, errorsFound);

    /// <summary>True when the mapping found no errors, so <see cref="Value"/> can be read.</summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>Every problem the mapping found, in the order found. Empty on success.</summary>
    public IReadOnlyList<MappingError> Errors { get; }

    /// <summary>The mapped value.</summary>
    /// <exception cref="MappingException">The mapping failed; the exception lists the errors.</exception>
    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations",
        Justification =
            "Reading the value of a failed mapping is documented to throw, and the exception lists the errors.")]
    public T Value => IsSuccess ? PartialValue! : throw new MappingException(Errors, _errorsFound);

    /// <summary>
    /// The mapped value, even when the mapping failed: on success it equals <see cref="Value"/>; on failure it is the
    /// value built from the valid items when invalid items were skipped, or <see langword="default"/>. Read it only
    /// when a partial value is acceptable, such as when importing the valid rows of a file.
    /// </summary>
    public T? PartialValue { get; }

    /// <summary>Creates a successful result, for example to stub a service in a test.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is a null reference.</exception>
    public static MappingResult<T> Success(T value)
    {
        if (value is null && !typeof(T).IsValueType)
            throw new ArgumentNullException(nameof(value), "A successful mapping needs a value.");

        return new MappingResult<T>(value, [], 0);
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
            : new MappingResult<T>(default, list, list.Length);
    }

    internal static MappingResult<T> Failure(T? partialValue, IReadOnlyList<MappingError> errors, int errorsFound) =>
        new(partialValue, errors, errorsFound);

    /// <summary>
    /// Groups the error messages by path, the shape ASP.NET Core's <c>Results.ValidationProblem</c>
    /// and <c>ValidationProblemDetails</c> expect.
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
        : $"Failure: {_errorsFound} error(s)";
}
