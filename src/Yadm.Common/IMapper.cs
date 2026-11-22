namespace Yadm.Common;

/// <summary>
/// Maps a <typeparamref name="TSource"/> to a <typeparamref name="TTarget"/>, reporting every problem it
/// finds to a <see cref="MappingContext"/>. This is the main contract: implement it once per mapping and
/// run it with <see cref="Mapper.Map{TSource, TTarget}(IMapper{TSource, TTarget}, TSource, MappingOptions?, IServiceProvider?)"/>.
/// </summary>
/// <typeparam name="TSource">The type to map from, such as a request DTO.</typeparam>
/// <typeparam name="TTarget">The type to map to, such as a domain model.</typeparam>
/// <remarks>
/// <para>
/// Mappers should be stateless. A shared lifetime is safe only when their dependencies are also safe to share;
/// otherwise register the mapper with a lifetime compatible with those dependencies.
/// </para>
/// <para>To use a mapper inside another one, call <c>context.Map(src.Child, mapper)</c>: it locates the child's
/// errors under <c>Child</c> and reports a missing child. Calling this method directly maps in the caller's
/// context, with neither.</para>
/// </remarks>
public interface IMapper<in TSource, out TTarget>
{
    /// <summary>Maps <paramref name="source"/>, reporting problems to <paramref name="context"/>.</summary>
    /// <param name="source">The value to map.</param>
    /// <param name="context">Collects errors and knows where in the source the mapping is.</param>
    /// <returns>
    /// The mapped value. Once an error has been reported, the return value is discarded, so return
    /// <see langword="null"/> (or <see langword="default"/>).
    /// </returns>
    TTarget? Map(TSource source, MappingContext context);
}
