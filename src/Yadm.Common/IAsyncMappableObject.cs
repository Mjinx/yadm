namespace Yadm.Common;

/// <summary>
/// A source object that asynchronously maps itself to a <typeparamref name="TTarget"/> when the mapping needs
/// I/O, such as looking up entities the source refers to.
/// </summary>
/// <typeparam name="TTarget">The type the object maps to.</typeparam>
/// <remarks>
/// This contract is independent of <see cref="IMappableObject{TTarget}"/>: async-only objects need not implement a
/// synchronous method. Run it with
/// <see cref="Mapper.MapAsync{TTarget}(IAsyncMappableObject{TTarget}, CancellationToken)"/>, or nest it with
/// <see cref="MappingContextNestedExtensions.MapAsync{TTarget}(MappingContext, IAsyncMappableObject{TTarget}?,
/// CancellationToken, string)"/>. Callers select the synchronous or asynchronous contract explicitly.
/// </remarks>
public interface IAsyncMappableObject<TTarget>
{
    /// <summary>Asynchronously maps this object, reporting problems to <paramref name="context"/>.</summary>
    /// <param name="context">Collects errors and knows where in the source the mapping is.</param>
    /// <param name="cancellationToken">Cancels lookups.</param>
    /// <returns>
    /// The mapped value. Once an error has been reported, the return value is discarded, so return
    /// <see langword="null"/> (or <see langword="default"/>).
    /// </returns>
    ValueTask<TTarget?> MapAsync(MappingContext context, CancellationToken cancellationToken);
}
