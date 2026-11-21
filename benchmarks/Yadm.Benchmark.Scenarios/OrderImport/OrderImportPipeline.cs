using System.Globalization;

namespace Yadm.Benchmark.Scenarios.OrderImportScenario;

public static class OrderImportPipeline
{
    public static async Task<BatchOutcome> ImportBatchAsync(
        IReadOnlyList<OrderInput> inputs, IOrderImporter importer, CancellationToken cancellationToken = default)
    {
        List<Order> orders = [];
        List<ImportIssue> errors = [];
        for (var index = 0; index < inputs.Count; index++)
        {
            var result = await importer.ImportAsync(inputs[index], cancellationToken);
            if (result.Order is not null)
                orders.Add(result.Order);
            foreach (var error in result.Errors)
                errors.Add(error with { Path = $"Orders[{index}].{error.Path}" });
        }

        return new BatchOutcome(orders, errors);
    }

    public static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
