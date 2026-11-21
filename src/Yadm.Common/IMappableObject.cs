namespace Yadm.Common;

/// <summary>
/// A source object that maps itself to a <typeparamref name="TTarget"/>, the way
/// <c>IValidatableObject</c> validates itself. Useful for DTOs that always map to the same model.
/// </summary>
/// <typeparam name="TTarget">The type the object maps to.</typeparam>
/// <remarks>
/// Run it with <see cref="Mapper.Map{TTarget}(IMappableObject{TTarget}, MappingOptions?)"/>, or nest it with
/// <see cref="MappingContextNestedExtensions.Map{TTarget}(MappingContext, IMappableObject{TTarget}?, string?, string)"/>.
/// Objects that need asynchronous work can implement <see cref="IAsyncMappableObject{TTarget}"/> instead, or both
/// contracts when both operations are meaningful.
/// </remarks>
public interface IMappableObject<out TTarget>
{
    /// <summary>Maps this object, reporting problems to <paramref name="context"/>.</summary>
    /// <param name="context">Collects errors and knows where in the source the mapping is.</param>
    /// <returns>
    /// The mapped value. Once an error has been reported, the return value is discarded, so return
    /// <see langword="null"/> (or <see langword="default"/>).
    /// </returns>
    TTarget? Map(MappingContext context);
}
