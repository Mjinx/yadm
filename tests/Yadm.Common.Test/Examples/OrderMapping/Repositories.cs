namespace Yadm.Common.Test;

// ── Infrastructure fault: thrown by lookup adapters, never caught by the mapper ──
public sealed class TransientLookupException(string message, Exception? inner = null)
    : Exception(message, inner);

public interface IProductRepository
{
    Task<IReadOnlyDictionary<string, Product>> FindBySkusAsync(IReadOnlyCollection<string> skus, CancellationToken ct);
}

public interface ICustomerRepository
{
    Task<IReadOnlyDictionary<Guid, Customer>> FindByIdsAsync(IReadOnlyCollection<Guid> guids, CancellationToken ct);
}
