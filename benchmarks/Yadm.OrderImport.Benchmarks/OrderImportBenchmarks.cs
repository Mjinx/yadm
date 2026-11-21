using BenchmarkDotNet.Attributes;
using Yadm.Benchmark.Scenarios.OrderImportScenario;

namespace Yadm.OrderImport.Benchmarks;

[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet requires public benchmark types.")]
public class OrderImportBenchmarks
{
    private IReadOnlyList<OrderInput> _inputs = null!;
    private IOrderImporter _yadm = null!;
    private IOrderImporter _fluent = null!;

    [Params("Valid", "InvalidInput", "InvalidLookup", "Mixed")]
    public string Scenario { get; set; } = null!;

    [Params(10, 100)]
    public int OrderCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _inputs = ImportFixtures.CreateBatch(Scenario, OrderCount);
        _yadm = new YadmOrderImporter(new InMemoryOrderLookup());
        _fluent = new FluentOrderImporter(new InMemoryOrderLookup());

        var yadm = await OrderImportPipeline.ImportBatchAsync(_inputs, _yadm).ConfigureAwait(false);
        var fluent = await OrderImportPipeline.ImportBatchAsync(_inputs, _fluent).ConfigureAwait(false);
        if (!yadm.Errors.SequenceEqual(fluent.Errors) || yadm.Orders.Count != fluent.Orders.Count ||
            yadm.Orders.Zip(fluent.Orders).Any(pair =>
                pair.First.CustomerId != pair.Second.CustomerId ||
                pair.First.PlacedOn != pair.Second.PlacedOn ||
                !pair.First.Lines.SequenceEqual(pair.Second.Lines)))
        {
            throw new InvalidOperationException("The import pipelines produced different results.");
        }
    }

    [Benchmark(Baseline = true)]
    public Task<BatchOutcome> Yadm() => OrderImportPipeline.ImportBatchAsync(_inputs, _yadm);

    [Benchmark]
    public Task<BatchOutcome> FluentValidationAndMapperly() => OrderImportPipeline.ImportBatchAsync(_inputs, _fluent);
}