using Yadm.Common;

namespace Yadm.Benchmark.Scenarios.OrderImportScenario;

public sealed class YadmOrderImporter(IOrderLookup lookup) : IOrderImporter
{
    private readonly MapperImpl _mapper = new(lookup);

    public async ValueTask<OrderOutcome> ImportAsync(OrderInput input, CancellationToken cancellationToken = default)
    {
        var result = await _mapper.MapAsync(input, cancellationToken);
        return new OrderOutcome(result.IsSuccess ? result.Value : null,
            result.Errors.Select(error => new ImportIssue(error.Path, error.Code)).ToArray());
    }

    private sealed class MapperImpl(IOrderLookup lookup) : IAsyncMapper<OrderInput, Order>
    {
        public async ValueTask<Order?> MapAsync(OrderInput input, MappingContext ctx, CancellationToken cancellationToken)
        {
            var customerId = ctx.Parse<Guid>(input.CustomerId);
            var placedOn = ctx.Parse<string, DateOnly>(input.PlacedOn, OrderImportPipeline.TryDate);
            var lines = ctx.MapEach(input.Lines, (line, c) =>
                new ParsedLine(c.Required(line.Sku), c.Parse<int>(line.Quantity)));
            if (ctx.HasErrors)
                return null;

            var customer = await lookup.FindCustomerAsync(customerId, cancellationToken);
            var skus = lines.Select(line => line.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var products = skus.Count == 0
                ? new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase)
                : await lookup.FindProductsAsync(skus, cancellationToken);

            if (customer is null)
            {
                ctx.Reject(customerId, "NotFound", "Customer was not found.", nameof(input.CustomerId));
                return null;
            }

            var mappedLines = ctx.MapEach(lines, (line, c) =>
            {
                if (!c.TryLookup(line.Sku, products, out var product))
                    return null;
                if (line.Quantity < 1 || line.Quantity > product.MaxQuantity)
                {
                    c.Reject(line.Quantity, "OutOfRange", "Quantity is outside the product's limit.");
                    return null;
                }

                return new OrderLine(line.Sku, line.Quantity, product.Price * (1 - customer.Discount));
            }, name: nameof(input.Lines));
            return ctx.HasErrors ? null : new Order(customerId, placedOn, mappedLines);
        }
    }
}
