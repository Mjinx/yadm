using BenchmarkDotNet.Attributes;
using Yadm.Benchmark.Scenarios.RegistrationScenario;

namespace Yadm.OrderImport.Benchmarks;

[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet requires public benchmark types.")]
public class RegistrationBenchmarks
{
    private readonly YadmRegistrationMapper _yadm = new();
    private readonly FluentRegistrationMapper _fluent = new();
    private RegistrationInput _input = null!;

    [Params("Valid", "Invalid", "OutOfRange")]
    public string Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _input = Scenario switch
        {
            "Valid" => new RegistrationInput("Alex", "32"),
            "Invalid" => new RegistrationInput(" ", "not a number"),
            "OutOfRange" => new RegistrationInput("Alex", "17"),
            _ => throw new ArgumentOutOfRangeException(nameof(Scenario))
        };
        var yadm = _yadm.Run(_input);
        var fluent = _fluent.Run(_input);
        if (yadm.Value != fluent.Value || !yadm.Errors.SequenceEqual(fluent.Errors))
            throw new InvalidOperationException("The registration pipelines produced different results.");
    }

    [Benchmark(Baseline = true)]
    public RegistrationOutcome Yadm() => _yadm.Run(_input);

    [Benchmark]
    public RegistrationOutcome FluentValidationAndMapperly() => _fluent.Run(_input);
}
