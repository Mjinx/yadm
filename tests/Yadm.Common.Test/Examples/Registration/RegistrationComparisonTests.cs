using Shouldly;
using Yadm.Benchmark.Scenarios;
using Yadm.Benchmark.Scenarios.RegistrationScenario;
using RegistrationValue = Yadm.Benchmark.Scenarios.RegistrationScenario.Registration;

namespace Yadm.Common.Test.Examples.Registration;

public sealed class RegistrationComparisonTests
{
    [Theory]
    [InlineData("Alex", "32", 32)]
    [InlineData("Alex", "18", 18)]
    [InlineData("Alex", "120", 120)]
    [InlineData(" Alex ", " +32 ", 32)]
    public void BothPipelinesMapValidRegistrations(string name, string age, int expectedAge)
    {
        var input = new RegistrationInput(name, age);
        var yadm = new YadmRegistrationMapper().Run(input);
        var fluent = new FluentRegistrationMapper().Run(input);

        yadm.Value.ShouldBe(new RegistrationValue(name, expectedAge));
        yadm.Errors.ShouldBeEmpty();
        fluent.Value.ShouldBe(yadm.Value);
        fluent.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, null, "Name", "Required", "Age", "Required")]
    [InlineData(" ", "bad", "Name", "Required", "Age", "Invalid")]
    [InlineData("Alex", "2147483648", "Age", "Invalid", null, null)]
    [InlineData("Alex", "17", "Age", "OutOfRange", null, null)]
    [InlineData("Alex", "121", "Age", "OutOfRange", null, null)]
    public void BothPipelinesReportTheSameErrors(
        string? name, string? age, string path, string code, string? secondPath, string? secondCode)
    {
        var input = new RegistrationInput(name, age);
        var yadm = new YadmRegistrationMapper().Run(input);
        var fluent = new FluentRegistrationMapper().Run(input);
        ImportIssue[] expected = secondPath is null
            ? [new(path, code)]
            : [new(path, code), new(secondPath, secondCode!)];

        yadm.Value.ShouldBeNull();
        fluent.Value.ShouldBeNull();
        yadm.Errors.ShouldBe(expected);
        fluent.Errors.ShouldBe(expected);
    }
}
