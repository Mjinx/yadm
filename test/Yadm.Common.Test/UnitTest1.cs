namespace Yadm.Common.Test;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
    }
}


// ── Infrastructure fault: thrown by lookup adapters, never caught by the mapper ──
public sealed class TransientLookupException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed record OrderInput(string? CustomerId, string? PlacedAt, string? Street, string? City,
    string? Postcode, string? Note, IReadOnlyList<LineInput>? Lines);
public sealed record LineInput(string? Sku, string? Quantity);

public sealed record Order(Guid CustomerId, DateTimeOffset PlacedAt, Address ShipTo, string? Note,
    IReadOnlyList<OrderLine> Lines);                         // new field => compile error in Build
public sealed record Address(string Street, string City, string Postcode);
public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record Customer(Guid Id, PriceTier Tier);
public sealed record Product(string Sku, decimal ListPrice, int MaxPerOrder);

public readonly record struct PlannedLine(int Index, string Sku, int Quantity);
public readonly record struct OrderPlan(Guid? CustomerId, DateTimeOffset? PlacedAt, Address? ShipTo,
    List<PlannedLine> Lines, HashSet<string> Skus);


public readonly record struct OrderLookups(IReadOnlyDictionary<Guid, Customer> Customers,
    IReadOnlyDictionary<string, Product> Products);


public sealed class OrderMapper(
    ICustomerRepository customers,
    IProductRepository products)
    : Mapper<OrderInput, OrderPlan, OrderLookups, Order>
{
    protected override OrderPlan Plan(OrderInput src, MappingScope scope)
    {
        var customerId = scope.ParseRequired<Guid>(src.CustomerId);
        var placedAt = scope.ParseRequired<DateTimeOffset>(src.PlacedAt);
        var shipTo = PlanAddress(src, scope);

        var srcLines = scope.Require(src.Lines);
        var lines = new List<PlannedLine>(srcLines?.Count ?? 0);
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < (srcLines?.Count ?? 0); i++)
        {
            if (scope.IsAborted) break; // fail-fast stops the collection
            using var _ = scope.Enter("Lines", i);
            var line = srcLines![i];
            var sku = scope.RequireText(line.Sku);
            var qty = scope.ParseRequired<int>(line.Quantity);
            if (sku is not null && qty is { } q)
            {
                lines.Add(new(i, sku, q));
                skus.Add(sku);
            }
        }

        return new(customerId, placedAt, shipTo, lines, skus);
    }

    private static Address? PlanAddress(OrderInput s, MappingScope scope)
    {
        if (s.Street is { Length: > 0 } st && s.City is { Length: > 0 } c && s.Postcode is { Length: > 0 } p)
            return new(st, c, p);
        List<string> missing = []; // only built on failure
        if (string.IsNullOrWhiteSpace(s.Street)) missing.Add(nameof(s.Street));
        if (string.IsNullOrWhiteSpace(s.City)) missing.Add(nameof(s.City));
        if (string.IsNullOrWhiteSpace(s.Postcode)) missing.Add(nameof(s.Postcode));
        scope.Fail(new IncompleteComposite(missing), "ShipTo");
        return null;
    }

    protected override async ValueTask<OrderLookups> ResolveAsync(OrderPlan plan, CancellationToken ct)
    {
        var cust = plan.CustomerId is { } id
            ? await customers.FindByIdsAsync([id], ct)
            : EmptyMap<Guid, Customer>.Value;
        var prod = plan.Skus.Count > 0
            ? await products.FindBySkusAsync(plan.Skus, ct)
            : EmptyMap<string, Product>.Value;
        return new(cust, prod);
    }

    protected override Order Build(OrderInput src, in OrderPlan plan, in OrderLookups look, MappingScope scope)
    {
        // Step 1: customer. The tier is needed by pricing, so the dependency is explicit.
        Customer? customer = null;
        if (plan.CustomerId is { } cid && !look.Customers.TryGetValue(cid, out customer))
            scope.Fail(new MissingLookup("Customer", cid.ToString()), "CustomerId");

        // Step 2: lines. If the customer failed, pricing is infeasible. We skip it without
        // adding a second error for the same root cause (the first is already in the result).
        var lines = new List<OrderLine>(plan.Lines.Count);
        if (customer is not null)
            foreach (var pl in plan.Lines)
            {
                if (scope.IsAborted) break;
                using var _ = scope.Enter("Lines", pl.Index);

                if (!look.Products.TryGetValue(pl.Sku, out var product))
                {
                    scope.Fail(new MissingLookup("Product", pl.Sku), "Sku");
                    continue;
                }

                if (pl.Quantity < 1 || pl.Quantity > product.MaxPerOrder)
                {
                    scope.Fail(new RuleViolation("QuantityWithinLimit", $"1..{product.MaxPerOrder}"), "Quantity");
                    continue;
                }

                lines.Add(new(pl.Sku, pl.Quantity, Pricing.UnitPrice(product, customer.Tier)));
            }

        // `!` is confined to this one construction site. Callers can't reach a partial
        // value via Value, only via Partial after seeing the issues.
        return new(plan.CustomerId.GetValueOrDefault(), plan.PlacedAt.GetValueOrDefault(),
            plan.ShipTo!, string.IsNullOrWhiteSpace(src.Note) ? null : src.Note, lines);
    }
}