using Shouldly;
using Yadm.Benchmark.Scenarios;
using Yadm.Benchmark.Scenarios.OrderImportScenario;
using ImportCustomer = Yadm.Benchmark.Scenarios.OrderImportScenario.Customer;
using ImportLine = Yadm.Benchmark.Scenarios.OrderImportScenario.OrderLine;
using ImportProduct = Yadm.Benchmark.Scenarios.OrderImportScenario.Product;
using ImportLineInput = Yadm.Benchmark.Scenarios.OrderImportScenario.LineInput;

namespace Yadm.Common.Test.Examples.OrderImport;

public sealed class OrderImportComparisonTests
{
    [Theory]
    [InlineData("Valid")]
    [InlineData("InvalidInput")]
    [InlineData("InvalidLookup")]
    [InlineData("Mixed")]
    public async Task BothPipelinesProduceTheSameOrdersAndErrors(string scenario)
    {
        var inputs = ImportFixtures.CreateBatch(scenario, 6);
        var yadm = await OrderImportPipeline.ImportBatchAsync(inputs, new YadmOrderImporter(new InMemoryOrderLookup()));
        var fluent = await OrderImportPipeline.ImportBatchAsync(inputs, new FluentOrderImporter(new InMemoryOrderLookup()));

        AssertEquivalent(yadm, fluent);
        if (scenario == "Valid")
        {
            yadm.Orders.Count.ShouldBe(6);
            yadm.Errors.ShouldBeEmpty();
            yadm.Orders[0].Lines.ShouldBe([new ImportLine("SKU-1", 2, 95m), new ImportLine("SKU-2", 3, 19m)]);
        }
        if (scenario == "Mixed")
        {
            yadm.Orders.Count.ShouldBe(2);
            yadm.Errors.ShouldBe([
                new ImportIssue("Orders[1].Lines[0].Quantity", "Invalid"),
                new ImportIssue("Orders[2].Lines[0].Sku", "NotFound"),
                new ImportIssue("Orders[4].Lines[0].Quantity", "Invalid"),
                new ImportIssue("Orders[5].Lines[0].Sku", "NotFound")
            ]);
        }
    }

    [Fact]
    public async Task BothPipelinesCheckInputBeforeLookingUpAndBatchProducts()
    {
        foreach (var create in Importers())
        {
            var lookup = new RecordingLookup();
            var importer = create(lookup);
            var invalid = await importer.ImportAsync(ImportFixtures.Valid() with
            {
                CustomerId = "bad",
                PlacedOn = "bad",
                Lines = [new ImportLineInput(null, "bad")]
            });
            invalid.Errors.Select(e => e.Path).ShouldBe(["CustomerId", "PlacedOn", "Lines[0].Sku", "Lines[0].Quantity"]);
            lookup.CustomerCalls.ShouldBe(0);
            lookup.ProductCalls.ShouldBe(0);

            var valid = await importer.ImportAsync(ImportFixtures.Valid() with
            {
                Lines = [new ImportLineInput("SKU-1", "2"), new ImportLineInput("sku-1", "1"), new ImportLineInput("SKU-2", "3")]
            });
            valid.Errors.ShouldBeEmpty();
            lookup.CustomerCalls.ShouldBe(1);
            lookup.ProductCalls.ShouldBe(1);
            lookup.LastSkus.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task BothPipelinesMatchForMissingValuesAndUnresolvedCustomers()
    {
        var inputs = new[]
        {
            ImportFixtures.Valid() with { CustomerId = null, PlacedOn = " ", Lines = null },
            ImportFixtures.Valid() with { CustomerId = Guid.Empty.ToString() },
            ImportFixtures.Valid() with { Lines = [] },
            ImportFixtures.Valid() with { PlacedOn = "05/11/2026" },
            ImportFixtures.Valid() with { Lines = [new ImportLineInput("missing", "0"), new ImportLineInput("SKU-2", "0")] },
            ImportFixtures.Valid() with { Lines = [null] }
        };
        var yadm = await OrderImportPipeline.ImportBatchAsync(inputs, new YadmOrderImporter(new InMemoryOrderLookup()));
        var fluent = await OrderImportPipeline.ImportBatchAsync(inputs, new FluentOrderImporter(new InMemoryOrderLookup()));
        AssertEquivalent(yadm, fluent);
        yadm.Errors.ShouldBe([
            new ImportIssue("Orders[0].CustomerId", "Required"),
            new ImportIssue("Orders[0].PlacedOn", "Required"),
            new ImportIssue("Orders[0].Lines", "Required"),
            new ImportIssue("Orders[1].CustomerId", "NotFound"),
            new ImportIssue("Orders[3].PlacedOn", "Invalid"),
            new ImportIssue("Orders[4].Lines[0].Sku", "NotFound"),
            new ImportIssue("Orders[4].Lines[1].Quantity", "OutOfRange"),
            new ImportIssue("Orders[5].Lines[0]", "Required")
        ]);
    }

    [Fact]
    public async Task BothPipelinesPropagateLookupFaultsAndCancellation()
    {
        foreach (var create in Importers())
        {
            var fault = new InvalidOperationException("Lookup unavailable");
            var lookup = new RecordingLookup { Fault = fault };
            var importer = create(lookup);
            var thrown = await Should.ThrowAsync<InvalidOperationException>(() => importer.ImportAsync(ImportFixtures.Valid()).AsTask());
            thrown.ShouldBeSameAs(fault);
            lookup.ProductCalls.ShouldBe(0);

            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() =>
                create(new InMemoryOrderLookup()).ImportAsync(ImportFixtures.Valid(), cancellation.Token).AsTask());
        }
    }

    private static Func<IOrderLookup, IOrderImporter>[] Importers() =>
        [lookup => new YadmOrderImporter(lookup), lookup => new FluentOrderImporter(lookup)];

    private static void AssertEquivalent(BatchOutcome expected, BatchOutcome actual)
    {
        actual.Errors.ShouldBe(expected.Errors);
        actual.Orders.Count.ShouldBe(expected.Orders.Count);
        for (var index = 0; index < expected.Orders.Count; index++)
        {
            actual.Orders[index].CustomerId.ShouldBe(expected.Orders[index].CustomerId);
            actual.Orders[index].PlacedOn.ShouldBe(expected.Orders[index].PlacedOn);
            actual.Orders[index].Lines.ShouldBe(expected.Orders[index].Lines);
        }
    }

    private sealed class RecordingLookup : IOrderLookup
    {
        private readonly InMemoryOrderLookup _inner = new();
        public int CustomerCalls { get; private set; }
        public int ProductCalls { get; private set; }
        public IReadOnlyCollection<string> LastSkus { get; private set; } = [];
        public Exception? Fault { get; init; }

        public Task<ImportCustomer?> FindCustomerAsync(Guid id, CancellationToken cancellationToken)
        {
            CustomerCalls++;
            if (Fault is not null)
                throw Fault;
            return _inner.FindCustomerAsync(id, cancellationToken);
        }

        public Task<IReadOnlyDictionary<string, ImportProduct>> FindProductsAsync(
            IReadOnlyCollection<string> skus, CancellationToken cancellationToken)
        {
            ProductCalls++;
            LastSkus = skus;
            return _inner.FindProductsAsync(skus, cancellationToken);
        }
    }
}
