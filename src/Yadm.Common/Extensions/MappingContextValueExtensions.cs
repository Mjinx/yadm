using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Yadm.Common;

/// <summary>
/// Parses <typeparamref name="TInput"/> in a way <see cref="IParsable{TSelf}"/> does not cover, such as a custom
/// date format. Same shape as the .NET <c>TryParse</c> methods, so those can be passed directly:
/// <c>(string s, out DateOnly d) =&gt; DateOnly.TryParseExact(s, "dd/MM/yyyy", out d)</c>.
/// </summary>
/// <returns>True when <paramref name="input"/> could be parsed; <paramref name="result"/> then holds the value.</returns>
public delegate bool TryParser<in TInput, TOutput>(TInput input, out TOutput? result);

/// <summary>
/// Reads single values: required values, parsed text and lookups.
/// </summary>
/// <remarks>
/// <para>Each helper reports a problem at the member its argument names, so
/// <c>context.Parse&lt;int&gt;(line.Quantity)</c> reports at <c>Quantity</c>. Pass <c>name</c> to choose another
/// name. Null, empty and whitespace strings count as absent, as with DataAnnotations' <c>[Required]</c>.</para>
/// <para>After reporting a problem a helper returns <see langword="default"/>. Read values into locals, check
/// <see cref="MappingContext.HasErrors"/>, then use them. When a rule needs one value before you check, use the
/// <c>Try</c> twin: it reports the same problem and tells you whether the value is usable.</para>
/// </remarks>
public static class MappingContextValueExtensions
{
    /// <param name="context">The context to report to.</param>
    extension(MappingContext context)
    {
        /// <summary>Returns <paramref name="value"/>, or reports <see cref="MappingErrorCodes.Required"/> when it is absent.</summary>
        /// <param name="value">The value that must be present. Empty or whitespace strings count as absent.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public T Required<T>(
            T? value,
            string? name = null,
            [CallerArgumentExpression(nameof(value))]
            string expression = ""
        ) where T : class =>
            context.TryRequired(value, out var present, name, expression) ? present : null!;

        /// <summary>Returns <paramref name="value"/>, or reports <see cref="MappingErrorCodes.Required"/> when it is null.</summary>
        /// <param name="value">The value that must be present.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public T Required<T>(
            T? value,
            string? name = null,
            [CallerArgumentExpression(nameof(value))]
            string expression = ""
        ) where T : struct =>
            context.TryRequired(value, out var present, name, expression) ? present : default;

        /// <summary>Like <c>Required</c>, but also tells you whether <paramref name="value"/> was present.</summary>
        /// <param name="value">The value that must be present. Empty or whitespace strings count as absent.</param>
        /// <param name="result">The value, when present.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public bool TryRequired<T>(
            T? value,
            [NotNullWhen(true)] out T? result,
            string? name = null,
            [CallerArgumentExpression(nameof(value))]
            string expression = ""
        ) where T : class
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!MappingRules.IsAbsent(value))
            {
                result = value;
                return true;
            }

            context.Reject(value, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
            result = null;
            return false;
        }

        /// <summary>Like <c>Required</c>, but also tells you whether <paramref name="value"/> was present.</summary>
        /// <param name="value">The value that must be present.</param>
        /// <param name="result">The value, when present.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public bool TryRequired<T>(
            T? value,
            out T result,
            string? name = null,
            [CallerArgumentExpression(nameof(value))]
            string expression = ""
        ) where T : struct
        {
            ArgumentNullException.ThrowIfNull(context);
            result = value.GetValueOrDefault();
            if (value.HasValue)
                return true;

            context.Reject(value, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
            return false;
        }

        /// <summary>
        /// Parses required text into any <see cref="IParsable{TSelf}"/> type, such as <see cref="int"/>,
        /// <see cref="Guid"/>, <see cref="DateTimeOffset"/>, or your own value objects. Enums are supported too.
        /// </summary>
        /// <remarks>
        /// Parsing uses <see cref="MappingOptions.FormatProvider"/>, made strict where .NET's defaults would misread
        /// input: fractional numbers accept no thousands separators and must be finite, and dates must be ISO 8601
        /// (read as UTC without an offset). Absent text reports <see cref="MappingErrorCodes.Required"/>; unparseable
        /// text reports <see cref="MappingErrorCodes.Invalid"/>. For other formats, pass your own parser.
        /// </remarks>
        /// <param name="text">The text to parse.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public T Parse<T>(
            string? text,
            string? name = null,
            [CallerArgumentExpression(nameof(text))]
            string expression = ""
        ) where T : IParsable<T> =>
            context.TryParse<T>(text, out var value, name, expression) ? value : default!;

        /// <summary>
        /// Like <see cref="Parse{T}(MappingContext, string?, string?, string)"/>, but also tells you whether the text
        /// could be parsed, so a rule can use the value: <c>if (ctx.TryParse(src.Qty, out int qty) &amp;&amp; qty &lt; 1)</c>.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <param name="result">The parsed value, when parsing succeeded.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public bool TryParse<T>(
            string? text,
            [MaybeNullWhen(false)] out T result,
            string? name = null,
            [CallerArgumentExpression(nameof(text))]
            string expression = ""
        ) where T : IParsable<T>
        {
            ArgumentNullException.ThrowIfNull(context);
            if (MappingRules.IsAbsent(text))
            {
                context.Reject(text, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
                result = default;
                return false;
            }

            if (MappingRules.TryParse(text, context.Options.FormatProvider, out result))
                return true;

            context.Reject(text, MappingErrorCodes.Invalid, MappingRules.InvalidMessage<T>(text), name, expression);
            return false;
        }

        /// <summary>
        /// Parses optional text like <see cref="Parse{T}(MappingContext, string?, string?, string)"/>, but absent
        /// text returns <see langword="null"/> without an error. Text that is present but invalid is still reported.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public T? ParseOptional<T>(
            string? text,
            string? name = null,
            [CallerArgumentExpression(nameof(text))]
            string expression = ""
        ) where T : struct, IParsable<T>
        {
            ArgumentNullException.ThrowIfNull(context);
            if (MappingRules.IsAbsent(text))
                return null;
            return context.TryParse<T>(text, out var value, name, expression) ? value : null;
        }

        /// <summary>
        /// Parses a required value with your own parser, such as
        /// <c>(string s, out DateOnly d) =&gt; DateOnly.TryParseExact(s, "dd/MM/yyyy", out d)</c>.
        /// </summary>
        /// <remarks>
        /// An absent input reports <see cref="MappingErrorCodes.Required"/> without calling the parser; a parser
        /// returning false reports <see cref="MappingErrorCodes.Invalid"/>. Exceptions thrown by the parser propagate.
        /// </remarks>
        /// <param name="input">The value to parse; it need not be a string.</param>
        /// <param name="parser">Parses a present input.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public TOutput Parse<TInput, TOutput>(
            TInput? input,
            TryParser<TInput, TOutput> parser,
            string? name = null,
            [CallerArgumentExpression(nameof(input))]
            string expression = ""
        ) =>
            context.TryParse(input, parser, out var value, name, expression) ? value : default!;

        /// <summary>Like <c>Parse</c> with your own parser, but also tells you whether the input could be parsed.</summary>
        /// <param name="input">The value to parse; it need not be a string.</param>
        /// <param name="parser">Parses a present input.</param>
        /// <param name="result">The parsed value, when parsing succeeded.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public bool TryParse<TInput, TOutput>(
            TInput? input,
            TryParser<TInput, TOutput> parser,
            [MaybeNullWhen(false)] out TOutput result,
            string? name = null,
            [CallerArgumentExpression(nameof(input))]
            string expression = ""
        )
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parser);
            if (MappingRules.IsAbsent(input))
            {
                context.Reject(input, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
                result = default;
                return false;
            }

            if (parser(input, out var parsed))
            {
                result = parsed!;
                return true;
            }

            result = default;
            context.Reject(input, MappingErrorCodes.Invalid, MappingRules.InvalidMessage<TOutput>(input), name,
                expression);
            return false;
        }

        /// <summary>
        /// Finds the entity <paramref name="key"/> refers to, or reports <see cref="MappingErrorCodes.NotFound"/>.
        /// Load the dictionary in one batch, then look up each key.
        /// </summary>
        /// <param name="key">The key to look up. An absent key reports <see cref="MappingErrorCodes.Required"/>.</param>
        /// <param name="values">The entities found, by key.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public TValue Lookup<TKey, TValue>(
            TKey? key,
            IReadOnlyDictionary<TKey, TValue> values,
            string? name = null,
            [CallerArgumentExpression(nameof(key))]
            string expression = ""
        ) where TKey : notnull =>
            context.TryLookup(key, values, out var value, name, expression) ? value : default!;

        /// <summary>Like <c>Lookup</c>, but also tells you whether the entity was found.</summary>
        /// <param name="key">The key to look up. An absent key reports <see cref="MappingErrorCodes.Required"/>.</param>
        /// <param name="values">The entities found, by key.</param>
        /// <param name="result">The entity, when found.</param>
        /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
        /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
        public bool TryLookup<TKey, TValue>(
            TKey? key,
            IReadOnlyDictionary<TKey, TValue> values,
            [MaybeNullWhen(false)] out TValue result,
            string? name = null,
            [CallerArgumentExpression(nameof(key))]
            string expression = ""
        ) where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(values);
            if (MappingRules.IsAbsent(key))
            {
                context.Reject(key, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
                result = default;
                return false;
            }

            if (values.TryGetValue(key, out result) && result is not null)
                return true;

            context.Reject(key, MappingErrorCodes.NotFound, $"{typeof(TValue).Name} '{key}' was not found.", name,
                expression);
            return false;
        }
    }
}