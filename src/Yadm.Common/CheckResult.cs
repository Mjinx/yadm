namespace Yadm.Common;

/// <summary>
/// The outcome of an independent value check: a value, absence, or an error.
/// The default result represents optional absence, not a successful default value.
/// </summary>
public readonly struct CheckResult<T>
{
    private readonly T? _value;

    private CheckResult(T? value, bool hasValue, MappingError? error) =>
        (_value, HasValue, Error) = (value, hasValue, error);

    public bool HasValue { get; }
    public MappingError? Error { get; }
    public bool IsAbsent => !HasValue && Error is null;

    public T Value => HasValue
        ? _value!
        : throw new InvalidOperationException("No checked value; inspect Error or IsAbsent.");

    public static CheckResult<T> Success(T value) => new(value, true, null);

    public static CheckResult<T> Failure(MappingError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, false, error);
    }
}
