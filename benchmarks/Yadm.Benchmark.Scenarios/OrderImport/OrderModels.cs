namespace Yadm.Benchmark.Scenarios.OrderImportScenario;

public sealed record OrderInput(string? CustomerId, string? PlacedOn, IReadOnlyList<LineInput?>? Lines);
public sealed record LineInput(string? Sku, string? Quantity);
public sealed record Customer(Guid Id, decimal Discount);
public sealed record Product(string Sku, decimal Price, int MaxQuantity);
public sealed record Order(Guid CustomerId, DateOnly PlacedOn, IReadOnlyList<OrderLine> Lines);
public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);
public sealed record ParsedLine(string Sku, int Quantity);
public sealed record OrderOutcome(Order? Order, IReadOnlyList<ImportIssue> Errors);
public sealed record BatchOutcome(IReadOnlyList<Order> Orders, IReadOnlyList<ImportIssue> Errors);
