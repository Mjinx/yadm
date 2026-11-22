namespace Yadm.Common;

internal sealed class MappingRun
{
    private readonly List<MappingError> _errors = [];

    internal MappingRun(MappingOptions? options, IServiceProvider? serviceProvider)
    {
        Options = options ?? MappingOptions.Default;
        if (serviceProvider is not null)
            ServiceProvider = serviceProvider.GetService;
    }

    internal MappingOptions Options { get; }
    internal Func<Type, object?>? ServiceProvider { get; set; }
    internal int ErrorsFound { get; private set; }
    internal bool IsStopped => Options.StopOnFirstError && ErrorsFound > 0;

    internal void Record(MappingContext context, string? member, string code, string message, object? attemptedValue)
    {
        ErrorsFound++;
        if (_errors.Count < Options.MaxErrors)
            _errors.Add(new MappingError(context.ErrorPath(member), code, message, attemptedValue));
    }

    internal MappingResult<T> Complete<T>(T? value, object producer, bool hasErrors)
    {
        if (ErrorsFound == 0)
            return MappingResult<T>.Success(MappingRules.Produced(value, producer));

        var errors = _errors.ToList();
        var leftOut = ErrorsFound - errors.Count;
        if (leftOut > 0)
        {
            errors.Add(new MappingError("", MappingErrorCodes.TooManyErrors,
                $"Only the first {errors.Count} errors are listed; {leftOut} more were found."));
        }

        if (!hasErrors && value is null && !typeof(T).IsValueType)
        {
            throw new InvalidOperationException(
                $"{MappingRules.Describe(producer)} returned null although its only errors were in items it skipped. " +
                "Return a value, or report an error that is not skipped.");
        }

        var hasPartialValue = !hasErrors;
        return MappingResult<T>.Failure(hasPartialValue ? value : default, errors, ErrorsFound, hasPartialValue);
    }
}
