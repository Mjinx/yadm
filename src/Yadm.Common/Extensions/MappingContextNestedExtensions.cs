using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Yadm.Common;

/// <summary>
/// Maps nested objects and collections with another mapper, a function, or the source's own
/// <see cref="IMappable{TTarget}"/> implementation.
/// </summary>
/// <remarks>
/// <para>Each nested value gets a child <see cref="MappingContext"/>, so its errors carry the full path, such as
/// <c>ShipTo.Street</c> or <c>Lines[2].Sku</c>, and also count as errors of the parent. Report errors to the context
/// the function receives, not to the parent.</para>
/// <para><c>Map</c> and <c>MapEach</c> treat a missing source as <see cref="MappingErrorCodes.Required"/>;
/// <c>MapOptional</c> returns null for it. When a nested mapping fails, the helper returns
/// <see langword="default"/>, and a failed collection item is left out of the list.</para>
/// </remarks>
public static class MappingContextNestedExtensions
{
    /// <summary>Maps a required nested value with <paramref name="mapper"/>.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="mapper">The mapper for the nested value.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TTarget Map<TSource, TTarget>(this MappingContext context, TSource? source,
        IMapper<TSource, TTarget> mapper, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return MapRequired(context, source, mapper, static (m, s, c) => m.Map(s, c), mapper, name, expression);
    }

    /// <summary>Maps a required nested value with a function.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="map">Maps the nested value, reporting problems to the child context it receives.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TTarget Map<TSource, TTarget>(this MappingContext context, TSource? source,
        Func<TSource, MappingContext, TTarget?> map, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(map);
        return MapRequired(context, source, map, static (f, s, c) => f(s, c), map, name, expression);
    }

    /// <summary>Maps a required nested value that maps itself.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TTarget Map<TTarget>(this MappingContext context, IMappable<TTarget>? source, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "") =>
        MapRequired(context, source, source, static (_, s, c) => s.Map(c), source, name, expression);

    /// <summary>Maps an optional nested value with <paramref name="mapper"/>; a missing value returns null.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value, if any.</param>
    /// <param name="mapper">The mapper for the nested value.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TTarget? MapOptional<TSource, TTarget>(this MappingContext context, TSource? source,
        IMapper<TSource, TTarget> mapper, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "") where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return MapOptionalCore(context, source, mapper, static (m, s, c) => m.Map(s, c), mapper, name, expression);
    }

    /// <summary>Maps an optional nested value with a function; a missing value returns null.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value, if any.</param>
    /// <param name="map">Maps the nested value, reporting problems to the child context it receives.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TTarget? MapOptional<TSource, TTarget>(this MappingContext context, TSource? source,
        Func<TSource, MappingContext, TTarget?> map, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "") where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(map);
        return MapOptionalCore(context, source, map, static (f, s, c) => f(s, c), map, name, expression);
    }

    /// <summary>Maps an optional nested value that maps itself; a missing value returns null.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value, if any.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static TTarget? MapOptional<TTarget>(this MappingContext context, IMappable<TTarget>? source,
        string? name = null, [CallerArgumentExpression(nameof(source))] string expression = "")
        where TTarget : class =>
        MapOptionalCore(context, source, source, static (_, s, c) => s.Map(c), source, name, expression);

    /// <summary>Maps every item of a required collection with <paramref name="mapper"/>.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The items. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="mapper">The mapper for one item.</param>
    /// <param name="skipInvalid">
    /// Leave failed items out without failing the parent, for example to import the valid rows of a file. Their
    /// errors are still reported. By default a failed item fails the parent.
    /// </param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    /// <returns>The items that mapped without errors, in source order.</returns>
    public static List<TTarget> MapEach<TSource, TTarget>(this MappingContext context, IEnumerable<TSource?>? source,
        IMapper<TSource, TTarget> mapper, bool skipInvalid = false, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return MapEachCore(context, source, mapper, static (m, s, _, c) => m.Map(s, c), mapper, skipInvalid, name,
            expression);
    }

    /// <summary>Maps every item of a required collection with a function.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The items. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="map">Maps one item, reporting problems to the item's context.</param>
    /// <param name="skipInvalid">
    /// Leave failed items out without failing the parent, for example to import the valid rows of a file. Their
    /// errors are still reported. By default a failed item fails the parent.
    /// </param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    /// <returns>The items that mapped without errors, in source order.</returns>
    public static List<TTarget> MapEach<TSource, TTarget>(this MappingContext context, IEnumerable<TSource?>? source,
        Func<TSource, MappingContext, TTarget?> map, bool skipInvalid = false, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(map);
        return MapEachCore(context, source, map, static (f, s, _, c) => f(s, c), map, skipInvalid, name, expression);
    }

    /// <summary>
    /// Maps every item of a required collection with a function that also receives the item's position, for
    /// example to keep it for a later step: <c>(line, index, c) =&gt; new LineRequest(index, ...)</c>.
    /// </summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The items. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="map">Maps one item at the given position, reporting problems to the item's context.</param>
    /// <param name="skipInvalid">
    /// Leave failed items out without failing the parent. Their errors are still reported.
    /// </param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    /// <returns>The items that mapped without errors, in source order.</returns>
    public static List<TTarget> MapEach<TSource, TTarget>(this MappingContext context, IEnumerable<TSource?>? source,
        Func<TSource, int, MappingContext, TTarget?> map, bool skipInvalid = false, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(map);
        return MapEachCore(context, source, map, static (f, s, i, c) => f(s, i, c), map, skipInvalid, name, expression);
    }

    /// <summary>Maps every item of a required collection of objects that map themselves.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The items. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="skipInvalid">
    /// Leave failed items out without failing the parent, for example to import the valid rows of a file. Their
    /// errors are still reported. By default a failed item fails the parent.
    /// </param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    /// <returns>The items that mapped without errors, in source order.</returns>
    public static List<TTarget> MapEach<TTarget>(this MappingContext context, IEnumerable<IMappable<TTarget>?>? source,
        bool skipInvalid = false, string? name = null,
        [CallerArgumentExpression(nameof(source))] string expression = "") =>
        MapEachCore(context, source, (object?)null, static (_, s, _, c) => s.Map(c), null, skipInvalid, name,
            expression);

    /// <summary>Maps a required nested value with an asynchronous mapper.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="mapper">The mapper for the nested value.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static ValueTask<TTarget> MapAsync<TSource, TTarget>(this MappingContext context, TSource? source,
        IAsyncMapper<TSource, TTarget> mapper, CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(source))] string expression = "") =>
        context.MapAsync(source, mapper, name: null, cancellationToken, expression);

    /// <summary>Maps a required nested value with an asynchronous mapper, reporting under <paramref name="name"/>.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="mapper">The mapper for the nested value.</param>
    /// <param name="name">The member name to report, or null to take it from the argument.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static async ValueTask<TTarget> MapAsync<TSource, TTarget>(this MappingContext context, TSource? source,
        IAsyncMapper<TSource, TTarget> mapper, string? name, CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(source))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mapper);
        if (MappingRules.IsAbsent(source))
        {
            context.Reject(source, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
            return default!;
        }

        if (context.IsStopped)
            return default!;

        var child = context.Child(MemberName.From(name, expression), -1);
        var errorsBefore = context.ErrorsFound;
        var value = await mapper.MapAsync(source, child, cancellationToken).ConfigureAwait(false);
        return Accept(child, value, mapper, errorsBefore);
    }

    /// <summary>Maps an optional nested value with an asynchronous mapper; a missing value returns null.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value, if any.</param>
    /// <param name="mapper">The mapper for the nested value.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static ValueTask<TTarget?> MapOptionalAsync<TSource, TTarget>(this MappingContext context,
        TSource? source, IAsyncMapper<TSource, TTarget> mapper, CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(source))] string expression = "") where TTarget : class =>
        context.MapOptionalAsync(source, mapper, name: null, cancellationToken, expression);

    /// <summary>
    /// Maps an optional nested value with an asynchronous mapper, reporting under <paramref name="name"/>; a missing
    /// value returns null.
    /// </summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The nested value, if any.</param>
    /// <param name="mapper">The mapper for the nested value.</param>
    /// <param name="name">The member name to report, or null to take it from the argument.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public static async ValueTask<TTarget?> MapOptionalAsync<TSource, TTarget>(this MappingContext context,
        TSource? source, IAsyncMapper<TSource, TTarget> mapper, string? name,
        CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(source))] string expression = "")
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mapper);
        if (MappingRules.IsAbsent(source) || context.IsStopped)
            return null;

        var child = context.Child(MemberName.From(name, expression), -1);
        var errorsBefore = context.ErrorsFound;
        var value = await mapper.MapAsync(source, child, cancellationToken).ConfigureAwait(false);
        CheckReportedHere(child, mapper, errorsBefore);
        return child.HasErrors ? null : value;
    }

    /// <summary>Maps every item of a required collection with an asynchronous mapper, one item at a time.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The items. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="mapper">The mapper for one item.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    /// <returns>The items that mapped without errors, in source order.</returns>
    public static ValueTask<List<TTarget>> MapEachAsync<TSource, TTarget>(this MappingContext context,
        IEnumerable<TSource?>? source, IAsyncMapper<TSource, TTarget> mapper,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(source))] string expression = "") =>
        context.MapEachAsync(source, mapper, skipInvalid: false, name: null, cancellationToken, expression);

    /// <summary>
    /// Maps every item of a required collection with an asynchronous mapper, one item at a time, reporting under
    /// <paramref name="name"/>.
    /// </summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The items. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="mapper">The mapper for one item.</param>
    /// <param name="name">The member name to report, or null to take it from the argument.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    /// <returns>The items that mapped without errors, in source order.</returns>
    public static ValueTask<List<TTarget>> MapEachAsync<TSource, TTarget>(this MappingContext context,
        IEnumerable<TSource?>? source, IAsyncMapper<TSource, TTarget> mapper, string? name,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(source))] string expression = "") =>
        context.MapEachAsync(source, mapper, skipInvalid: false, name, cancellationToken, expression);

    /// <summary>Maps every item of a required collection with an asynchronous mapper, one item at a time.</summary>
    /// <param name="context">The parent context.</param>
    /// <param name="source">The items. Null reports <see cref="MappingErrorCodes.Required"/>.</param>
    /// <param name="mapper">The mapper for one item.</param>
    /// <param name="skipInvalid">
    /// Leave failed items out without failing the parent. Their errors are still reported.
    /// </param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    /// <returns>The items that mapped without errors, in source order.</returns>
    public static async ValueTask<List<TTarget>> MapEachAsync<TSource, TTarget>(this MappingContext context,
        IEnumerable<TSource?>? source, IAsyncMapper<TSource, TTarget> mapper, bool skipInvalid, string? name = null,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(source))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mapper);
        var mapped = new List<TTarget>();
        if (!CanStart(context, source, name, expression))
            return mapped;

        var member = MemberName.ForCollection(name, expression);
        using var items = source.GetEnumerator();
        for (var index = 0; items.MoveNext(); index++)
        {
            var child = context.Child(member, index);
            var item = items.Current;
            if (MappingRules.IsAbsent(item))
            {
                child.AddError(MappingErrorCodes.Required, MappingRules.RequiredMessage);
            }
            else
            {
                var errorsBefore = context.ErrorsFound;
                var value = await mapper.MapAsync(item, child, cancellationToken).ConfigureAwait(false);
                var accepted = Accept(child, value, mapper, errorsBefore);
                if (!child.HasErrors)
                    mapped.Add(accepted);
            }

            if (!Continue(context, child, skipInvalid))
                break;
        }

        return mapped;
    }

    private static TTarget MapRequired<TState, TSource, TTarget>(MappingContext context, TSource? source, TState state,
        Func<TState, TSource, MappingContext, TTarget?> map, object? producer, string? name, string expression)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (MappingRules.IsAbsent(source))
        {
            context.Reject(source, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
            return default!;
        }

        if (context.IsStopped)
            return default!;

        var child = context.Child(MemberName.From(name, expression), -1);
        var errorsBefore = context.ErrorsFound;
        return Accept(child, map(state, source, child), producer, errorsBefore);
    }

    private static TTarget? MapOptionalCore<TState, TSource, TTarget>(MappingContext context, TSource? source,
        TState state, Func<TState, TSource, MappingContext, TTarget?> map, object? producer, string? name,
        string expression) where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(context);
        if (MappingRules.IsAbsent(source) || context.IsStopped)
            return null;

        var child = context.Child(MemberName.From(name, expression), -1);
        var errorsBefore = context.ErrorsFound;
        var value = map(state, source, child);
        CheckReportedHere(child, producer, errorsBefore);
        return child.HasErrors ? null : value;
    }

    private static List<TTarget> MapEachCore<TState, TSource, TTarget>(MappingContext context,
        IEnumerable<TSource?>? source, TState state, Func<TState, TSource, int, MappingContext, TTarget?> map,
        object? producer, bool skipInvalid, string? name, string expression)
    {
        ArgumentNullException.ThrowIfNull(context);
        var mapped = new List<TTarget>();
        if (!CanStart(context, source, name, expression))
            return mapped;

        var member = MemberName.ForCollection(name, expression);
        using var items = source.GetEnumerator();
        for (var index = 0; items.MoveNext(); index++)
        {
            var child = context.Child(member, index);
            var item = items.Current;
            if (MappingRules.IsAbsent(item))
            {
                child.AddError(MappingErrorCodes.Required, MappingRules.RequiredMessage);
            }
            else
            {
                var errorsBefore = context.ErrorsFound;
                var value = map(state, item, index, child);
                var accepted = Accept(child, value, producer ?? item, errorsBefore);
                if (!child.HasErrors)
                    mapped.Add(accepted);
            }

            if (!Continue(context, child, skipInvalid))
                break;
        }

        return mapped;
    }

    /// <summary>Returns a nested value, rejecting a null that no error in its own context explains.</summary>
    private static TTarget Accept<TTarget>(MappingContext child, TTarget? value, object? producer, int errorsBefore)
    {
        CheckReportedHere(child, producer, errorsBefore);
        if (child.HasErrors)
            return default!;
        if (value is null && !typeof(TTarget).IsValueType && child.ErrorsReportedHere > 0)
        {
            throw new InvalidOperationException(
                $"{MappingRules.Describe(producer)} returned null although its only errors were in items it skipped. " +
                "Return a value, or report an error that is not skipped.");
        }

        return MappingRules.Produced(value, producer);
    }

    /// <summary>Catches a function that reported to the parent's context (<c>ctx</c>) instead of its own (<c>c</c>).</summary>
    private static void CheckReportedHere(MappingContext child, object? producer, int errorsBefore)
    {
        var elsewhere = child.ErrorsFound - errorsBefore - child.ErrorsReportedHere;
        if (elsewhere > 0)
        {
            throw new InvalidOperationException(
                $"{MappingRules.Describe(producer)} reported {elsewhere} error(s) to a different MappingContext. " +
                "Report them to the context it receives, so they are located under it.");
        }
    }

    /// <summary>Reports a missing collection, and refuses to start enumerating once the mapping has stopped.</summary>
    private static bool CanStart<TSource>(MappingContext context, [NotNullWhen(true)] IEnumerable<TSource?>? source,
        string? name, string expression)
    {
        if (source is null)
        {
            context.Reject(source, MappingErrorCodes.Required, MappingRules.RequiredMessage, name, expression);
            return false;
        }

        return !context.IsStopped;
    }

    /// <summary>Decides, after an item, whether to read the next one.</summary>
    private static bool Continue(MappingContext context, MappingContext item, bool skipInvalid)
    {
        if (context.IsStopped)
            return false;
        if (skipInvalid && item.HasErrors)
            context.Skip(item);
        return true;
    }
}
