namespace Yadm;

/// <summary>
/// A source object that asynchronously maps itself to a <typeparamref name="TTarget"/> when the mapping needs
/// I/O, such as looking up entities the source refers to. Its dependencies are reached through the context, which
/// is an <see cref="IServiceProvider"/> supplied when the mapping is run (see <see cref="MappingContext.GetService"/>).
/// </summary>
/// <typeparam name="TTarget">The type the object maps to.</typeparam>
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
