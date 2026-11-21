using System.Runtime.CompilerServices;

namespace Yadm.Common;

/// <summary>Adapters that report independent checks through a mapping scope.</summary>
public static class MappingScopeExtensions
{
    extension(MappingScope scope)
    {
        /// <summary>
        /// Reports a check's error using the scope's current path and policy.
        /// Custom checks can use this adapter without adding helpers to MappingScope.
        /// </summary>
        public CheckResult<T> Check<T>(CheckResult<T> result,
            [CallerArgumentExpression(nameof(result))] string expr = "")
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (result.Error is { } error)
                scope.Fail(error, expr);
            return result;
        }

        public T? Require<T>(T? v, [CallerArgumentExpression(nameof(v))] string expr = "") where T : class
        {
            var result = scope.Check(MappingChecks.Require(v), expr);
            return result.HasValue ? result.Value : null;
        }

        public T? Require<T>(T? v, [CallerArgumentExpression(nameof(v))] string expr = "") where T : struct
        {
            var result = scope.Check(MappingChecks.Require(v), expr);
            return result.HasValue ? result.Value : null;
        }

        public string? RequireText(string? v, [CallerArgumentExpression(nameof(v))] string expr = "")
        {
            var result = scope.Check(MappingChecks.RequireText(v), expr);
            return result.HasValue ? result.Value : null;
        }

        public T? ParseRequired<T>(string? raw, [CallerArgumentExpression(nameof(raw))] string expr = "")
            where T : struct, ISpanParsable<T>
        {
            var result = scope.Check(MappingChecks.ParseRequired<T>(raw), expr);
            return result.HasValue ? result.Value : null;
        }

        public T? ParseOptional<T>(string? raw, [CallerArgumentExpression(nameof(raw))] string expr = "")
            where T : struct, ISpanParsable<T>
        {
            var result = scope.Check(MappingChecks.ParseOptional<T>(raw), expr);
            return result.HasValue ? result.Value : null;
        }

        /// <summary>
        /// Parses any input type into a value or reference type using a Func.
        /// For example, (string? text) =&gt; (DateTime.TryParseExact(text, "dd/MM/yyyy",
        /// CultureInfo.InvariantCulture, DateTimeStyles.None, out var date), date).
        /// Inspect HasValue before reading Value; Error contains any reported failure.
        /// </summary>
        public CheckResult<T> ParseRequired<TInput, T>(TInput raw,
            Func<TInput, (bool Success, T Value)> parser,
            Func<TInput, bool>? isAbsent = null,
            [CallerArgumentExpression(nameof(raw))] string expr = "") =>
            scope.Check(MappingChecks.ParseRequired(raw, parser, isAbsent), expr);

        /// <summary>
        /// Parses present input using a Func. Absent input has IsAbsent set and no error;
        /// malformed input has an Error and is reported through the scope.
        /// </summary>
        public CheckResult<T> ParseOptional<TInput, T>(TInput raw,
            Func<TInput, (bool Success, T Value)> parser,
            Func<TInput, bool>? isAbsent = null,
            [CallerArgumentExpression(nameof(raw))] string expr = "") =>
            scope.Check(MappingChecks.ParseOptional(raw, parser, isAbsent), expr);
    }
}
