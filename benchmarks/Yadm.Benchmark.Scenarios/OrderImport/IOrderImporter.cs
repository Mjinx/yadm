namespace Yadm.Benchmark.Scenarios.OrderImportScenario;

public interface IOrderImporter
{
    ValueTask<OrderOutcome> ImportAsync(OrderInput input, CancellationToken cancellationToken = default);
}
