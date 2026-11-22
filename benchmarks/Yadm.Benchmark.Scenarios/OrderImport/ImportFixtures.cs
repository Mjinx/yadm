namespace Yadm.Benchmark.Scenarios.OrderImportScenario;

public interface IOrderLookup
{
    Task<Customer?> FindCustomerAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, Product>> FindProductsAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken);
}

public sealed class InMemoryOrderLookup : IOrderLookup
{
    public static readonly Guid KnownCustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Customer Customer = new(KnownCustomerId, 0.05m);
    private static readonly Dictionary<string, Product> Products =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SKU-1"] = new("SKU-1", 100m, 10),
            ["SKU-2"] = new("SKU-2", 20m, 5)
        };

    public Task<Customer?> FindCustomerAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(id == Customer.Id ? Customer : null);
    }

    public Task<IReadOnlyDictionary<string, Product>> FindProductsAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, Product> matches = skus.Where(Products.ContainsKey)
            .ToDictionary(sku => sku, sku => Products[sku], StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(matches);
    }
}

public static class ImportFixtures
{
    public static OrderInput Valid() => new(InMemoryOrderLookup.KnownCustomerId.ToString(), "2026-11-05",
        [new LineInput("SKU-1", "2"), new LineInput("SKU-2", "3")]);

    public static IReadOnlyList<OrderInput> CreateBatch(string scenario, int count) =>
        Enumerable.Range(0, count).Select(index => scenario switch
        {
            "Valid" => Valid(),
            "InvalidInput" => Valid() with
            {
                CustomerId = "bad",
                Lines = [new LineInput("SKU-1", "bad"), new LineInput(null, "2")]
            },
            "InvalidLookup" => Valid() with
            {
                Lines = [new LineInput("missing", "1"), new LineInput("SKU-2", "6")]
            },
            "Mixed" => (index % 3) switch
            {
                0 => Valid(),
                1 => Valid() with { Lines = [new LineInput("SKU-1", "bad")] },
                _ => Valid() with { Lines = [new LineInput("missing", "1")] }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        }).ToArray();
}
