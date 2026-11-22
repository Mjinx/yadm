using System.Collections.Immutable;

namespace Yadm.Test;

/// <summary>
/// Maps an order request to an order: checks the input, looks up the customer and the products in one batch
/// each, then builds the order.
/// </summary>
public sealed class OrderMapper(ICustomerRepository customers, IProductRepository products)
    : IAsyncMapper<OrderInput, Order>
{
    public async ValueTask<Order?> MapAsync(OrderInput src, MappingContext context, CancellationToken cancellationToken)
    {
        // 1. Check the input. Every problem is collected with its path, such as "Lines[1].Quantity".
        var customerId = context.Parse<Guid>(src.CustomerId);
        var placedAt = context.Parse<DateTimeOffset>(src.PlacedAt);
        var orderType = context.Parse<OrderType>(src.OrderType);
        var shipTo = context.Map(src.ShipTo);
        var requestedDeliveryAt = context.ParseOptional<DateTimeOffset>(src.RequestedDeliveryAt);

        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lines = context.MapEach(src.Lines, (line, c) =>
        {
            var sku = c.Required(line.Sku);
            var quantity = c.Parse<int>(line.Quantity);

            skus.Add(sku);
            return new LineRequest(sku, quantity);
        });
        if (context.HasErrors)
            return null; // Invalid input causes no lookups.

        // 2. Look up what the order refers to, one batch per repository.
        var customerMatches = await customers.FindByIdsAsync([customerId], cancellationToken);

        var productMatches = skus.Count > 0
            ? await products.FindBySkusAsync(skus, cancellationToken)
            : ImmutableDictionary<string, Product>.Empty;

        // 3. Build the order.
        var customer = context.Lookup(customerId, customerMatches, nameof(src.CustomerId));
        if (context.HasErrors)
            return null; // Prices depend on the customer's tier.

        var orderLines = context.MapEach(lines,
            map: (line, ctx) =>
            {
                if (!ctx.TryLookup(line.Sku, productMatches, out var product))
                    return null;

                if (line.Quantity < 1 || line.Quantity > product.MaxPerOrder)
                {
                    ctx.Reject(line.Quantity, "OutOfRange", $"Quantity must be between 1 and {product.MaxPerOrder}.");
                    return null;
                }

                return new OrderLine(line.Sku, line.Quantity, Pricing.UnitPrice(product, customer.Tier));
            },
            name: nameof(src.Lines));

        if (context.HasErrors)
            return null;

        return new Order(customerId, placedAt, orderType, shipTo, requestedDeliveryAt,
            string.IsNullOrWhiteSpace(src.Note) ? null : src.Note, orderLines);
    }

    private sealed record LineRequest(string Sku, int Quantity);
}

public static class Pricing
{
    public static decimal UnitPrice(Product product, PriceTier customerTier) =>
        customerTier switch
        {
            PriceTier.Bronze => product.ListPrice,
            PriceTier.Silver => product.ListPrice * 0.95m,
            PriceTier.Gold => product.ListPrice * 0.90m,
            _ => throw new ArgumentOutOfRangeException(nameof(customerTier), customerTier, "Unknown tier")
        };
}

public sealed record OrderInput(
    string? CustomerId,
    string? PlacedAt,
    string? OrderType,
    AddressInput? ShipTo,
    string? Note,
    IEnumerable<LineInput>? Lines,
    string? RequestedDeliveryAt = null);

/// <summary>Maps itself.</summary>
public sealed record AddressInput(string? Street, string? City, string? Postcode) : IMappableObject<Address>
{
    public Address? Map(MappingContext context)
    {
        var street = context.Required(Street);
        var city = context.Required(City);
        var postcode = context.Required(Postcode);
        return context.HasErrors ? null : new Address(street, city, postcode);
    }
}

public sealed record LineInput(string? Sku, string? Quantity);

public sealed record Order(
    Guid CustomerId,
    DateTimeOffset PlacedAt,
    OrderType OrderType,
    Address ShipTo,
    DateTimeOffset? RequestedDeliveryAt,
    string? Note,
    IReadOnlyList<OrderLine> Lines);

public sealed record Address(string Street, string City, string Postcode);

public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);

public enum OrderType
{
    Standard,
    Express,
    Collection
}

public enum PriceTier
{
    Bronze,
    Silver,
    Gold
}

public sealed record Customer(Guid Id, PriceTier Tier);

public sealed record Product(string Sku, decimal ListPrice, int MaxPerOrder);

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
