namespace Yadm.Common;

/// <summary>
/// Maps a <typeparamref name="TSource"/> to a <typeparamref name="TTarget"/> when the mapping needs I/O,
/// such as looking up entities the source refers to. Run it with
/// <see cref="Mapper.MapAsync{TSource, TTarget}(IAsyncMapper{TSource, TTarget}, TSource, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TSource">The type to map from, such as a request DTO.</typeparam>
/// <typeparam name="TTarget">The type to map to, such as a domain model.</typeparam>
/// <remarks>
/// Check the input first and return early when <see cref="MappingContext.HasErrors"/> is true, so invalid
/// input causes no lookups. Then resolve lookups in batches and build the target. To use it inside another
/// mapper, call <c>await context.MapAsync(src.Child, mapper, cancellationToken)</c>.
/// </remarks>
public interface IAsyncMapper<in TSource, TTarget>
{
    /// <summary>Maps <paramref name="source"/>, reporting problems to <paramref name="context"/>.</summary>
    /// <param name="source">The value to map.</param>
    /// <param name="context">Collects errors and knows where in the source the mapping is.</param>
    /// <param name="cancellationToken">Cancels lookups.</param>
    /// <returns>
    /// The mapped value. Once an error has been reported, the return value is discarded, so return
    /// <see langword="null"/> (or <see langword="default"/>).
    /// </returns>
    ValueTask<TTarget?> MapAsync(TSource source, MappingContext context, CancellationToken cancellationToken);
}
