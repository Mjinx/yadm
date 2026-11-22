namespace Yadm;

/// <summary>Projects mapping results into common error response shapes.</summary>
public static class MappingResultExtensions
{
    /// <summary>
    /// Groups the error messages by path: each key is an error path and each value lists its messages.
    /// </summary>
    public static IDictionary<string, string[]> ToDictionary<T>(this MappingResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Errors.GroupBy(error => error.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(error => error.Message).ToArray(),
                StringComparer.Ordinal
            );
    }

    /// <summary>Returns each error as a field, code, and message detail for API responses.</summary>
    public static MappingErrorDetail[] ToErrorDetails<T>(this MappingResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Errors.Select(error => new MappingErrorDetail(error.Path, error.Code, error.Message)).ToArray();
    }
}
