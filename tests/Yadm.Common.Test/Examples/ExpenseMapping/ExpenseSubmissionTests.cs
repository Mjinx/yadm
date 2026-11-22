using Shouldly;

namespace Yadm.Common.Test;

public class ExpenseSubmissionTests
{
    private static readonly ExpenseSubmissionInput ValidInput = new(
        "11111111-1111-1111-1111-111111111111", "gbp",
        [new ExpenseLineInput("Rail operator", "2026-11-05", "42.50"),
            new ExpenseLineInput("Station cafe", "2026-11-05", "6.25")]);

    [Fact]
    public void MapsAFormAndItsLinesWithoutSeparateMapperClasses()
    {
        var result = ValidInput.Map();

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmployeeId.ShouldBe(Guid.Parse(ValidInput.EmployeeId!));
        result.Value.Currency.ShouldBe(ExpenseCurrency.GBP);
        result.Value.Lines.ShouldBe(
        [
            new ExpenseLine("Rail operator", new DateOnly(2026, 11, 5), 42.50m),
            new ExpenseLine("Station cafe", new DateOnly(2026, 11, 5), 6.25m)
        ]);
    }

    [Fact]
    public void ReturnsFieldErrorsForTheFormWithoutAcceptingOnlySomeExpenses()
    {
        var input = ValidInput with
        {
            Currency = "unknown",
            Lines =
            [
                new ExpenseLineInput("Rail operator", "2026-11-05", "42.50"),
                new ExpenseLineInput(null, "yesterday", "free"),
                new ExpenseLineInput("Station cafe", "2026-11-05", "0")
            ]
        };

        var result = input.Map();

        result.IsSuccess.ShouldBeFalse();
        result.PartialValue.ShouldBeNull();
        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
        [
            ("Currency", MappingErrorCodes.Invalid),
            ("Lines[1].Merchant", MappingErrorCodes.Required),
            ("Lines[1].SpentOn", MappingErrorCodes.Invalid),
            ("Lines[1].Amount", MappingErrorCodes.Invalid),
            ("Lines[2].Amount", "OutOfRange")
        ]);
        result.ToDictionary()["Lines[2].Amount"].ShouldBe(["The expense amount must be greater than zero."]);
    }

    [Fact]
    public void AnEmptyClaimIsDifferentFromAMissingExpenseList()
    {
        var empty = (ValidInput with { Lines = [] }).Map();
        var missing = (ValidInput with { Lines = null }).Map();

        empty.Errors.ShouldHaveSingleItem().ShouldBe(new MappingError(
            "Lines", "EmptyClaim", "Include at least one expense.", Array.Empty<ExpenseLineInput>()));
        missing.Errors.ShouldHaveSingleItem().Code.ShouldBe(MappingErrorCodes.Required);
        missing.Errors[0].Path.ShouldBe("Lines");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    public void RejectsNonPositiveAmounts(string amount)
    {
        var input = new ExpenseLineInput("Station cafe", "2026-11-05", amount);

        var result = input.Map();

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("OutOfRange");
        result.PartialValue.ShouldBeNull();
    }
}
