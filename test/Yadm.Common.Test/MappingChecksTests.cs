using System.Globalization;

namespace Yadm.Common.Test;

public class MappingChecksTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task RequiredAndOptionalKeepTheirAbsenceSemantics(string? raw)
    {
        var result = await Map(scope =>
        {
            Assert.Null(scope.ParseRequired<int>(raw, expr: "Required"));
            Assert.Null(scope.ParseOptional<int>(raw, expr: "Optional"));
        });

        var issue = Assert.Single(result.Issues);
        Assert.Equal("Required", issue.Path);
        Assert.IsType<AbsentValue>(issue.Error);
        Assert.False(result.Aborted);
    }

    [Fact]
    public async Task DefaultParsersKeepInvariantCultureAndNullableValues()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var id = Guid.NewGuid();
            var result = await Map(scope =>
            {
                Assert.Equal(1.5m, scope.ParseRequired<decimal>("1.5"));
                Assert.Equal(0, scope.ParseOptional<int>("0"));
                Assert.Equal(id, scope.ParseRequired<Guid>(id.ToString()));
                Assert.Equal(
                    new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero),
                    scope.ParseRequired<DateTimeOffset>("2026-11-05T00:00:00+00:00"));
            });

            Assert.True(result.IsComplete);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task CustomDateFormatsUseFuncAndPreserveFieldPaths()
    {
        Func<string?, (bool Success, DateTime Value)> parseDate = raw =>
        {
            var success = DateTime.TryParseExact(
                raw, ["dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date);
            return (success, date);
        };
        var src = new { PlacedAt = "05/11/2026", InvalidDate = "11.05.2026" };
        var result = await Map(scope =>
        {
            using var frame = scope.Enter("Orders", 2);
            Assert.Equal(new DateTime(2026, 11, 5), scope.ParseRequired(src.PlacedAt, parseDate).Value);
            Assert.Equal(new DateTime(2026, 11, 5), MappingChecks.ParseRequired("2026-11-05", parseDate).Value);
            Assert.False(scope.ParseOptional(src.InvalidDate, parseDate).HasValue);
        });

        var issue = Assert.Single(result.Issues);
        Assert.Equal("Orders[2].InvalidDate", issue.Path);
        Assert.Equal(new MalformedValue(src.InvalidDate, nameof(DateTime)), issue.Error);
    }

    [Fact]
    public async Task NonStringInputAndReferenceOutputNeedNoParsingInterface()
    {
        Func<int, (bool Success, ProductCode Value)> parseCode =
            raw => (raw >= 0, new ProductCode($"SKU-{raw}"));
        var src = new { Code = 42, InvalidCode = -1 };
        var result = await Map(scope =>
        {
            Assert.Equal(new ProductCode("SKU-42"), scope.ParseRequired(src.Code, parseCode).Value);
            Assert.False(scope.ParseRequired(src.InvalidCode, parseCode).HasValue);
        });

        var issue = Assert.Single(result.Issues);
        Assert.Equal("InvalidCode", issue.Path);
        Assert.Equal(new MalformedValue(-1, nameof(ProductCode)), issue.Error);
    }

    [Fact]
    public void ComplexInputIsPreservedInMalformedError()
    {
        var raw = new ProductCode("bad");
        var result = MappingChecks.ParseRequired(raw, _ => (false, 0));
        var error = Assert.IsType<MalformedValue>(result.Error);

        Assert.Same(raw, error.Raw);
        Assert.False(result.HasValue);
        Assert.False(result.IsAbsent);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AbsentInputNeverInvokesParser(string? raw)
    {
        Func<string?, (bool Success, int Value)> parser = _ =>
            throw new InvalidOperationException("Must not run for absence.");

        Assert.IsType<AbsentValue>(MappingChecks.ParseRequired(raw, parser).Error);
        var optional = MappingChecks.ParseOptional(raw, parser);
        Assert.True(optional.IsAbsent);
        Assert.Null(optional.Error);
        Assert.Throws<InvalidOperationException>(() => optional.Value);
    }

    [Fact]
    public void NullNonStringInputIsAbsent()
    {
        ProductCode? raw = null;
        Func<ProductCode?, (bool Success, int Value)> parser = _ =>
            throw new InvalidOperationException("Must not run for absence.");

        Assert.IsType<AbsentValue>(MappingChecks.ParseRequired(raw, parser).Error);
        Assert.True(MappingChecks.ParseOptional(raw, parser).IsAbsent);
    }

    [Fact]
    public void PresenceRuleIsReplaceableForDomainInputs()
    {
        Func<int, (bool Success, int Value)> parser = raw => (true, raw);

        Assert.Equal(0, MappingChecks.ParseRequired(0, parser).Value);
        Assert.IsType<AbsentValue>(MappingChecks.ParseRequired(0, parser, raw => raw == 0).Error);
        Assert.True(MappingChecks.ParseOptional(0, parser, raw => raw == 0).IsAbsent);
        Assert.Equal("", MappingChecks.ParseRequired("", raw => (true, raw), _ => false).Value);
    }

    [Fact]
    public async Task ScopeAdaptersHonorCustomPresenceAndExplicitPath()
    {
        var result = await Map(scope =>
        {
            Assert.False(scope.ParseRequired(
                0, raw => (true, raw), raw => raw == 0, "Quantity").HasValue);
            Assert.True(scope.ParseOptional(
                0, raw => (true, raw), raw => raw == 0).IsAbsent);
        });

        var issue = Assert.Single(result.Issues);
        Assert.Equal("Quantity", issue.Path);
        Assert.IsType<AbsentValue>(issue.Error);
    }

    [Theory]
    [InlineData(FailurePolicy.Continue, 2, false)]
    [InlineData(FailurePolicy.FailFast, 1, true)]
    public async Task AllParsingAdaptersRespectFailurePolicy(FailurePolicy policy, int count, bool aborted)
    {
        var src = new { First = "invalid", Second = 7 };
        var result = await Map(scope =>
        {
            using var frame = scope.Enter("Lines", 1);
            Assert.Null(scope.ParseRequired<int>(src.First));
            Assert.False(scope.ParseOptional(src.Second, _ => (false, 0)).HasValue);
        }, policy);

        Assert.Equal(count, result.Issues.Count);
        Assert.Equal(aborted, result.Aborted);
        Assert.Equal("Lines[1].First", result.Issues[0].Path);
        Assert.All(result.Issues, issue => Assert.Equal(aborted, issue.Aborting));
        Assert.Equal(new MalformedValue("invalid", nameof(Int32)), result.Issues[0].Error);
        if (!aborted)
            Assert.Equal("Lines[1].Second", result.Issues[1].Path);
    }

    [Fact]
    public async Task RequiredValueAdaptersPreserveExistingBehaviorAndPaths()
    {
        var src = new { Name = (string?)null, Age = (int?)null, Text = "  " };
        var result = await Map(scope =>
        {
            using var frame = scope.Enter("Customer");
            Assert.Null(scope.Require(src.Name));
            Assert.Null(scope.Require(src.Age));
            Assert.Null(scope.RequireText(src.Text));
            Assert.Equal("ok", scope.Require("ok"));
            Assert.Equal(0, scope.Require((int?)0));
            Assert.Equal(" ok ", scope.RequireText(" ok "));
        });

        Assert.Equal(new[] { "Customer.Name", "Customer.Age", "Customer.Text" },
            result.Issues.Select(issue => issue.Path));
        Assert.All(result.Issues, issue => Assert.IsType<AbsentValue>(issue.Error));
    }

    [Fact]
    public void ChecksCanBeUsedWithoutScope()
    {
        Assert.Equal("ok", MappingChecks.Require("ok").Value);
        Assert.Equal(0, MappingChecks.Require((int?)0).Value);
        Assert.Equal(" ok ", MappingChecks.RequireText(" ok ").Value);
        Assert.IsType<AbsentValue>(MappingChecks.Require<string>(null).Error);
        Assert.IsType<AbsentValue>(MappingChecks.Require<int>(null).Error);
        Assert.IsType<AbsentValue>(MappingChecks.RequireText(" ").Error);
        Assert.IsType<MalformedValue>(MappingChecks.ParseOptional<int>("bad").Error);
    }

    [Fact]
    public async Task CustomChecksCanReportDomainErrorsWithoutChangingScope()
    {
        var src = new { Quantity = -1 };
        var error = new RuleViolation("PositiveQuantity", "Must be positive");
        var result = await Map(scope =>
        {
            using var frame = scope.Enter("Order");
            var checkedValue = scope.Check(
                src.Quantity > 0
                    ? CheckResult<int>.Success(src.Quantity)
                    : CheckResult<int>.Failure(error),
                nameof(src.Quantity));
            Assert.Same(error, checkedValue.Error);
        }, FailurePolicy.FailFast);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("Order.Quantity", issue.Path);
        Assert.Same(error, issue.Error);
        Assert.True(result.Aborted);
    }

    [Fact]
    public void ResultsDistinguishDefaultValueAbsenceAndFailure()
    {
        Assert.Equal(0, CheckResult<int>.Success(0).Value);
        Assert.True(default(CheckResult<int>).IsAbsent);
        Assert.Null(CheckResult<string?>.Success(null).Value);
        Assert.True(CheckResult<string?>.Success(null).HasValue);
        Assert.False(CheckResult<int>.Failure(new AbsentValue()).IsAbsent);
        Assert.Throws<ArgumentNullException>(() => CheckResult<int>.Failure(null!));
    }

    [Fact]
    public void InvalidConfigurationAndParserFaultsAreNotHidden()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MappingChecks.ParseRequired<string?, int>(null, null!));
        Assert.Throws<ArgumentNullException>(() =>
            MappingChecks.ParseOptional<string?, int>(null, null!));

        var exception = new InvalidOperationException("Parser fault");
        Func<int, (bool Success, int Value)> parser = _ => throw exception;
        Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
            MappingChecks.ParseRequired(1, parser)));
        Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
            MappingChecks.ParseOptional(1, parser)));
        Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
            MappingChecks.ParseRequired(1, raw => (true, raw), _ => throw exception)));
    }

    private static ValueTask<MappingResult<bool>> Map(
        Action<MappingScope> check, FailurePolicy policy = FailurePolicy.Continue) =>
        new CheckMapper().MapAsync(check, policy);

    private sealed record ProductCode(string Value);

    private sealed class CheckMapper : Mapper<Action<MappingScope>, bool, bool, bool>
    {
        protected override bool Plan(Action<MappingScope> source, MappingScope scope)
        {
            source(scope);
            return true;
        }

        protected override ValueTask<bool> ResolveAsync(bool plan, CancellationToken ct) =>
            ValueTask.FromResult(true);

        protected override bool Build(Action<MappingScope> source, in bool plan, in bool lookups, MappingScope scope) =>
            true;
    }
}
