using System.Globalization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Yadm.Common.Test;

/// <summary>
/// Tests <see cref="OrderMapper"/> composed with substitute repositories. By default the repositories find the
/// customer and product below; a test changes what they find, throw or cancel, then checks the result and which
/// lookups the mapper made.
/// </summary>
public class OrderMapperTests
{
    private static readonly Customer Customer =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), PriceTier.Silver);

    private static readonly Product Product = new("SKU-1", ListPrice: 100m, MaxPerOrder: 10);

    private static readonly OrderInput ValidInput = new(
        CustomerId: Customer.Id.ToString(),
        PlacedAt: "2026-11-05T00:00:00+00:00",
        OrderType: "Standard",
        ShipTo: new AddressInput("1 High Street", "London", "SW1A 1AA"),
        Note: "Leave at the door",
        Lines: [new LineInput(Product.Sku, "2")]);

    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly OrderMapper _mapper;

    public OrderMapperTests()
    {
        CustomerRepositoryFinds(Customer);
        ProductRepositoryFinds(Product);
        _mapper = new OrderMapper(_customers, _products);
    }

    [Fact]
    public async Task MapsAValidOrder()
    {
        var result = await _mapper.MapAsync(ValidInput);

        result.IsSuccess.ShouldBeTrue();
        var order = result.Value;
        order.CustomerId.ShouldBe(Customer.Id);
        order.PlacedAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        order.OrderType.ShouldBe(OrderType.Standard);
        order.ShipTo.ShouldBe(new Address("1 High Street", "London", "SW1A 1AA"));
        order.Note.ShouldBe(ValidInput.Note);
        order.RequestedDeliveryAt.ShouldBeNull();
        order.Lines.ShouldHaveSingleItem().ShouldBe(new OrderLine("SKU-1", 2, 95m));
    }

    [Fact]
    public async Task LooksUpTheCustomerAndTheProductsInOneBatchEach()
    {
        ProductRepositoryFinds(Product, Product with { Sku = "SKU-2" });
        var input = ValidInput with
        {
            Lines = [new LineInput("SKU-1", "2"), new LineInput("SKU-2", "1"), new LineInput("SKU-1", "3")]
        };

        var result = await _mapper.MapAsync(input);

        result.IsSuccess.ShouldBeTrue();
        await _customers.Received(1).FindByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(Customer.Id)),
            Arg.Any<CancellationToken>());
        await _products.Received(1).FindBySkusAsync(
            Arg.Is<IReadOnlyCollection<string>>(skus => skus.Count == 2 && skus.Contains("SKU-1") && skus.Contains("SKU-2")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PriceTier.Bronze, 100)]
    [InlineData(PriceTier.Silver, 95)]
    [InlineData(PriceTier.Gold, 90)]
    public async Task PricesLinesForTheCustomersTier(PriceTier tier, int unitPrice)
    {
        CustomerRepositoryFinds(Customer with { Tier = tier });

        var result = await _mapper.MapAsync(ValidInput);

        result.Value.Lines.ShouldHaveSingleItem().UnitPrice.ShouldBe(unitPrice);
    }

    [Fact]
    public async Task CollectsEveryInputErrorWithItsPathBeforeAnyLookup()
    {
        var input = ValidInput with
        {
            CustomerId = "bad",
            PlacedAt = null,
            OrderType = "Unknown",
            ShipTo = new AddressInput(null, "London", " "),
            Lines = [new LineInput("SKU-1", "2"), new LineInput("SKU-1", "two"), new LineInput(null, "1")],
            RequestedDeliveryAt = "soon"
        };

        var result = await _mapper.MapAsync(input);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
        [
            ("CustomerId", MappingErrorCodes.Invalid),
            ("PlacedAt", MappingErrorCodes.Required),
            ("OrderType", MappingErrorCodes.Invalid),
            ("ShipTo.Street", MappingErrorCodes.Required),
            ("ShipTo.Postcode", MappingErrorCodes.Required),
            ("RequestedDeliveryAt", MappingErrorCodes.Invalid),
            ("Lines[1].Quantity", MappingErrorCodes.Invalid),
            ("Lines[2].Sku", MappingErrorCodes.Required)
        ]);
        result.PartialValue.ShouldBeNull();
        await AssertNothingWasLookedUp();
    }

    [Fact]
    public async Task ErrorsConvertToTheShapeOfAnAspNetCoreValidationProblem()
    {
        var input = ValidInput with { CustomerId = "bad", Lines = [new LineInput(null, "x")] };

        var result = await _mapper.MapAsync(input);

        var errors = result.ToDictionary();
        errors.Keys.ShouldBe(["CustomerId", "Lines[0].Sku", "Lines[0].Quantity"]);
        errors["CustomerId"].ShouldBe(["'bad' is not a valid Guid."]);
        errors["Lines[0].Sku"].ShouldBe(["A value is required."]);
        errors["Lines[0].Quantity"].ShouldBe(["'x' is not a valid Int32."]);
    }

    [Fact]
    public async Task ReadingTheValueOfAFailedMappingThrowsWithEveryError()
    {
        var result = await _mapper.MapAsync(ValidInput with { CustomerId = null, PlacedAt = "bad" });

        var exception = Should.Throw<MappingException>(() => result.Value);
        exception.Errors.ShouldBe(result.Errors);
        exception.Message.ShouldContain("CustomerId: A value is required.");
        exception.Message.ShouldContain("PlacedAt: 'bad' is not a valid DateTimeOffset.");
    }

    [Fact]
    public async Task StopOnFirstErrorReportsOneErrorWithoutEnumeratingLinesOrLookingUp()
    {
        var input = ValidInput with { CustomerId = "bad", PlacedAt = "bad", Lines = new UnusableLines() };

        var result = await _mapper.MapAsync(input, MappingOptions.FailFast);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("CustomerId");
        await AssertNothingWasLookedUp();
    }

    [Fact]
    public async Task StopOnFirstErrorDoesNotAdvancePastAnInvalidLineAndDisposesTheLines()
    {
        var disposed = false;
        IEnumerable<LineInput> Lines()
        {
            try
            {
                yield return new LineInput(null, "1");
                throw new InvalidOperationException("Must not advance after the first error.");
            }
            finally
            {
                // The test reads the flag after the mapper disposes the iterator; IDE0059 misses that.
#pragma warning disable IDE0059
                disposed = true;
#pragma warning restore IDE0059
            }
        }

        var result = await _mapper.MapAsync(ValidInput with { Lines = Lines() }, MappingOptions.FailFast);

        disposed.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Lines[0].Sku");
        await AssertNothingWasLookedUp();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task OptionalDeliveryDateCanBeAbsent(string? raw)
    {
        var result = await _mapper.MapAsync(ValidInput with { RequestedDeliveryAt = raw });

        result.Value.RequestedDeliveryAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("2026-11-07T12:30:00+02:00", 2)]
    [InlineData("2026-11-07T12:30:00", 0)]
    public async Task OptionalDeliveryDateIsParsedAndReadAsUtcWithoutAnOffset(string raw, int offsetHours)
    {
        var result = await _mapper.MapAsync(ValidInput with { RequestedDeliveryAt = raw });

        result.Value.RequestedDeliveryAt.ShouldBe(
            new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.FromHours(offsetHours)));
    }

    [Theory]
    [InlineData("Standard", OrderType.Standard)]
    [InlineData("express", OrderType.Express)]
    [InlineData(" COLLECTION ", OrderType.Collection)]
    public async Task MapsOrderTypeIgnoringCaseAndWhitespace(string raw, OrderType expected)
    {
        var result = await _mapper.MapAsync(ValidInput with { OrderType = raw });

        result.Value.OrderType.ShouldBe(expected);
    }

    [Fact]
    public async Task InvalidOrderTypeListsTheAcceptedNames()
    {
        var result = await _mapper.MapAsync(ValidInput with { OrderType = "999" });

        var error = result.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("OrderType");
        error.AttemptedValue.ShouldBe("999");
        error.Message.ShouldBe("'999' is not a valid OrderType. Expected one of: Standard, Express, Collection.");
    }

    [Fact]
    public async Task MissingAddressAndLinesAreRequired()
    {
        var result = await _mapper.MapAsync(ValidInput with { ShipTo = null, Lines = null });

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("ShipTo", MappingErrorCodes.Required), ("Lines", MappingErrorCodes.Required)]);
        await AssertNothingWasLookedUp();
    }

    [Fact]
    public async Task UnknownCustomerIsReportedWithoutDependentPricingErrors()
    {
        CustomerRepositoryFinds();

        var result = await _mapper.MapAsync(ValidInput);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("CustomerId");
        error.Code.ShouldBe(MappingErrorCodes.NotFound);
        error.Message.ShouldBe($"Customer '{Customer.Id}' was not found.");
    }

    [Fact]
    public async Task UnknownProductsAreReportedPerLine()
    {
        var input = ValidInput with
        {
            Lines = [new LineInput("SKU-1", "2"), new LineInput("missing-1", "1"), new LineInput("missing-2", "1")]
        };

        var result = await _mapper.MapAsync(input);

        result.Errors.Select(error => (error.Path, error.Message)).ShouldBe(
        [
            ("Lines[1].Sku", "Product 'missing-1' was not found."),
            ("Lines[2].Sku", "Product 'missing-2' was not found.")
        ]);
        await _products.ReceivedWithAnyArgs(1)
            .FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
    }

    [Fact]
    public async Task AnOrderWithoutLinesSkipsTheProductLookup()
    {
        var result = await _mapper.MapAsync(ValidInput with { Lines = [] });

        result.Value.Lines.ShouldBeEmpty();
        await _products.DidNotReceiveWithAnyArgs()
            .FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    public async Task QuantityRuleIsReportedAtTheLine(string quantity)
    {
        var result = await _mapper.MapAsync(ValidInput with { Lines = [new LineInput("SKU-1", quantity)] });

        result.Errors.ShouldHaveSingleItem().ShouldBe(new MappingError(
            "Lines[0].Quantity", "OutOfRange", "Quantity must be between 1 and 10.",
            int.Parse(quantity, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task ACustomerLookupFaultPropagatesAndSkipsTheProductLookup()
    {
        var fault = new TransientLookupException("Customer store unavailable.");
        _customers.FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), CancellationToken.None)
            .ThrowsAsyncForAnyArgs(fault);

        var thrown = await Should.ThrowAsync<TransientLookupException>(() => _mapper.MapAsync(ValidInput).AsTask());

        thrown.ShouldBeSameAs(fault);
        await _products.DidNotReceiveWithAnyArgs()
            .FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
    }

    [Fact]
    public async Task AProductLookupFaultPropagatesWithoutBecomingAnError()
    {
        var fault = new TransientLookupException("Product store unavailable.");
        _products.FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None)
            .ThrowsAsyncForAnyArgs(fault);

        var thrown = await Should.ThrowAsync<TransientLookupException>(() => _mapper.MapAsync(ValidInput).AsTask());

        thrown.ShouldBeSameAs(fault);
    }

    [Fact]
    public async Task TheCancellationTokenReachesBothRepositories()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await _mapper.MapAsync(ValidInput, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        await _customers.Received(1).FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), cancellation.Token);
        await _products.Received(1).FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), cancellation.Token);
    }

    [Fact]
    public async Task CancellationPropagatesAndStopsFurtherLookups()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _customers.FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), CancellationToken.None)
            .ThrowsAsyncForAnyArgs(new OperationCanceledException(cancellation.Token));

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => _mapper.MapAsync(ValidInput, cancellation.Token).AsTask());

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        await _products.DidNotReceiveWithAnyArgs()
            .FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
    }

    /// <summary>Makes the customer repository find exactly these customers, whatever it is asked for.</summary>
    private void CustomerRepositoryFinds(params Customer[] customers) =>
        _customers.FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), CancellationToken.None)
            .ReturnsForAnyArgs(customers.ToDictionary(customer => customer.Id));

    /// <summary>Makes the product repository find exactly these products, whatever it is asked for.</summary>
    private void ProductRepositoryFinds(params Product[] products) =>
        _products.FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None)
            .ReturnsForAnyArgs(products.ToDictionary(product => product.Sku));

    private async Task AssertNothingWasLookedUp()
    {
        await _customers.DidNotReceiveWithAnyArgs()
            .FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), CancellationToken.None);
        await _products.DidNotReceiveWithAnyArgs()
            .FindBySkusAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
    }

    private sealed class UnusableLines : IEnumerable<LineInput>
    {
        public IEnumerator<LineInput> GetEnumerator() =>
            throw new InvalidOperationException("Must not start enumerating after the first error.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
