using System.Globalization;
using FluentValidation;
using Riok.Mapperly.Abstractions;

namespace Yadm.Benchmark.Scenarios.OrderImportScenario;

public sealed class FluentOrderImporter(IOrderLookup lookup) : IOrderImporter
{
    private readonly InputValidator _inputValidator = new();
    private readonly OrderDtoMapper _mapper = new();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method",
        Justification = "The validation rules are synchronous and perform no I/O.")]
    public async ValueTask<OrderOutcome> ImportAsync(OrderInput input, CancellationToken cancellationToken = default)
    {
        var validation = await _inputValidator.ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
            return new OrderOutcome(null, validation.Errors.Select(e => new ImportIssue(e.PropertyName, e.ErrorCode)).ToArray());

        var customerId = Guid.Parse(input.CustomerId!);
        OrderImportPipeline.TryDate(input.PlacedOn!, out var placedOn);
        var lines = input.Lines!.Select(line => _mapper.MapLine(line!)).ToArray();
        var customer = await lookup.FindCustomerAsync(customerId, cancellationToken);
        var skus = lines.Select(line => line.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var products = skus.Count == 0
            ? new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase)
            : await lookup.FindProductsAsync(skus, cancellationToken);

        if (customer is null)
            return new OrderOutcome(null, [new ImportIssue(nameof(input.CustomerId), "NotFound")]);

        var resolved = lines.Select(line => new ResolvedLine(
            line.Sku, line.Quantity, products.GetValueOrDefault(line.Sku))).ToArray();
        var resolvedValidation = await ResolvedValidator.Instance.ValidateAsync(new ResolvedOrder(resolved), cancellationToken);
        if (!resolvedValidation.IsValid)
            return new OrderOutcome(null, resolvedValidation.Errors.Select(e => new ImportIssue(e.PropertyName, e.ErrorCode)).ToArray());

        var orderLines = resolved.Select(line =>
            new OrderLine(line.Sku, line.Quantity, line.Product!.Price * (1 - customer.Discount))).ToArray();
        return new OrderOutcome(new Order(customerId, placedOn, orderLines), []);
    }

    private sealed class InputValidator : AbstractValidator<OrderInput>
    {
        public InputValidator()
        {
            RuleFor(x => x.CustomerId).Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode("Required")
                .Must(value => Guid.TryParse(value, out _)).WithErrorCode("Invalid");
            RuleFor(x => x.PlacedOn).Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode("Required")
                .Must(value => OrderImportPipeline.TryDate(value!, out _)).WithErrorCode("Invalid");
            RuleFor(x => x.Lines).NotNull().WithErrorCode("Required");
            RuleForEach(x => x.Lines).NotNull().WithErrorCode("Required").SetValidator(new LineValidator());
        }
    }

    private sealed class LineValidator : AbstractValidator<LineInput?>
    {
        public LineValidator()
        {
            RuleFor(x => x!.Sku).NotEmpty().WithErrorCode("Required");
            RuleFor(x => x!.Quantity).Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode("Required")
                .Must(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .WithErrorCode("Invalid");
        }
    }

    private sealed record ResolvedOrder(IReadOnlyList<ResolvedLine> Lines);
    private sealed record ResolvedLine(string Sku, int Quantity, Product? Product);

    private sealed class ResolvedValidator : AbstractValidator<ResolvedOrder>
    {
        public static ResolvedValidator Instance { get; } = new();

        private ResolvedValidator() => RuleForEach(x => x.Lines).SetValidator(new ResolvedLineValidator());
    }

    private sealed class ResolvedLineValidator : AbstractValidator<ResolvedLine>
    {
        public ResolvedLineValidator()
        {
            RuleFor(x => x.Sku).Must((line, _) => line.Product is not null).WithErrorCode("NotFound");
            RuleFor(x => x.Quantity)
                .Must((line, quantity) => quantity >= 1 && quantity <= line.Product!.MaxQuantity)
                .When(line => line.Product is not null).WithErrorCode("OutOfRange");
        }
    }
}

[Mapper]
public partial class OrderDtoMapper
{
    [MapProperty(nameof(LineInput.Quantity), nameof(ParsedLine.Quantity), Use = nameof(ParseQuantity))]
    public partial ParsedLine MapLine(LineInput input);

    private static int ParseQuantity(string? quantity) =>
        int.Parse(quantity!, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
