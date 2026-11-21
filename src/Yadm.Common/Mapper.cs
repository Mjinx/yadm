namespace Yadm.Common;

/// <summary>
/// Runs mappings, the way <c>Validator</c> runs validation. Each run gets a new <see cref="MappingContext"/>
/// and returns a <see cref="MappingResult{T}"/>.
/// </summary>
public static class Mapper
{
    /// <summary>Maps <paramref name="source"/> with <paramref name="mapper"/>.</summary>
    /// <param name="mapper">The mapper to run.</param>
    /// <param name="source">The value to map.</param>
    /// <param name="options">The options to run with; <see cref="MappingOptions.Default"/> when omitted.</param>
    /// <exception cref="InvalidOperationException">The mapper returned null without reporting an error.</exception>
    public static MappingResult<TTarget> Map<TSource, TTarget>(this IMapper<TSource, TTarget> mapper,
        TSource source, MappingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        var context = new MappingContext(options);
        return context.Complete(mapper.Map(source, context), mapper);
    }

    /// <summary>Maps this object with its own <see cref="IMappableObject{TTarget}.Map"/> method.</summary>
    /// <param name="source">The object to map.</param>
    /// <param name="options">The options to run with; <see cref="MappingOptions.Default"/> when omitted.</param>
    /// <exception cref="InvalidOperationException">The object returned null without reporting an error.</exception>
    public static MappingResult<TTarget> Map<TTarget>(this IMappableObject<TTarget> source,
        MappingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var context = new MappingContext(options);
        return context.Complete(source.Map(context), source);
    }

    /// <summary>Maps <paramref name="source"/> with <paramref name="mapper"/>, using the default options.</summary>
    /// <param name="mapper">The mapper to run.</param>
    /// <param name="source">The value to map.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <exception cref="InvalidOperationException">The mapper returned null without reporting an error.</exception>
    public static ValueTask<MappingResult<TTarget>> MapAsync<TSource, TTarget>(
        this IAsyncMapper<TSource, TTarget> mapper, TSource source, CancellationToken cancellationToken = default) =>
        mapper.MapAsync(source, MappingOptions.Default, cancellationToken);

    /// <summary>Maps <paramref name="source"/> with <paramref name="mapper"/>.</summary>
    /// <param name="mapper">The mapper to run.</param>
    /// <param name="source">The value to map.</param>
    /// <param name="options">The options to run with; <see cref="MappingOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the mapper's lookups.</param>
    /// <exception cref="InvalidOperationException">The mapper returned null without reporting an error.</exception>
    public static async ValueTask<MappingResult<TTarget>> MapAsync<TSource, TTarget>(
        this IAsyncMapper<TSource, TTarget> mapper, TSource source, MappingOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        cancellationToken.ThrowIfCancellationRequested();
        var context = new MappingContext(options);
        var value = await mapper.MapAsync(source, context, cancellationToken).ConfigureAwait(false);
        return context.Complete(value, mapper);
    }

    /// <summary>Maps this object with its own asynchronous mapping method, using the default options.</summary>
    /// <param name="source">The object to map.</param>
    /// <param name="cancellationToken">Cancels the mapping's lookups.</param>
    /// <exception cref="InvalidOperationException">The object returned null without reporting an error.</exception>
    public static ValueTask<MappingResult<TTarget>> MapAsync<TTarget>(
        this IAsyncMappableObject<TTarget> source, CancellationToken cancellationToken = default) =>
        source.MapAsync(MappingOptions.Default, cancellationToken);

    /// <summary>Maps this object with its own asynchronous mapping method.</summary>
    /// <param name="source">The object to map.</param>
    /// <param name="options">The options to run with; <see cref="MappingOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the mapping's lookups.</param>
    /// <exception cref="InvalidOperationException">The object returned null without reporting an error.</exception>
    public static async ValueTask<MappingResult<TTarget>> MapAsync<TTarget>(
        this IAsyncMappableObject<TTarget> source, MappingOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var context = new MappingContext(options);
        var value = await source.MapAsync(context, cancellationToken).ConfigureAwait(false);
        return context.Complete(value, source);
    }

    /// <summary>
    /// Creates a mapper from a function, for mappings too small for their own class:
    /// <c>Mapper.Create((AddressDto a, MappingContext c) =&gt; new Address(c.Required(a.Street)))</c>.
    /// </summary>
    public static IMapper<TSource, TTarget> Create<TSource, TTarget>(Func<TSource, MappingContext, TTarget?> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new FunctionMapper<TSource, TTarget>(map);
    }

    /// <summary>Creates an asynchronous mapper from a function.</summary>
    public static IAsyncMapper<TSource, TTarget> Create<TSource, TTarget>(
        Func<TSource, MappingContext, CancellationToken, ValueTask<TTarget?>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new AsyncFunctionMapper<TSource, TTarget>(map);
    }

    private sealed class FunctionMapper<TSource, TTarget>(Func<TSource, MappingContext, TTarget?> map)
        : IMapper<TSource, TTarget>, IMappingFunction
    {
        public TTarget? Map(TSource source, MappingContext context) => map(source, context);
    }

    private sealed class AsyncFunctionMapper<TSource, TTarget>(
        Func<TSource, MappingContext, CancellationToken, ValueTask<TTarget?>> map)
        : IAsyncMapper<TSource, TTarget>, IMappingFunction
    {
        public ValueTask<TTarget?> MapAsync(TSource source, MappingContext context, CancellationToken cancellationToken) =>
            map(source, context, cancellationToken);
    }
}
