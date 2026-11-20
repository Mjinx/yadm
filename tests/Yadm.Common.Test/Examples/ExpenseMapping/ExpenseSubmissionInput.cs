namespace Yadm.Common.Test;

/// <summary>An application-owned form that always maps to the same expense claim.</summary>
public sealed record ExpenseSubmissionInput(
    string? EmployeeId,
    string? Currency,
    IReadOnlyList<ExpenseLineInput>? Lines
) : IMappable<ExpenseClaim>
{
    public ExpenseClaim? Map(MappingContext context)
    {
        var employeeId = context.Parse<Guid>(EmployeeId);
        var currency = context.Parse<ExpenseCurrency>(Currency);
        var lines = context.MapEach(Lines);
        if (Lines is { Count: 0 })
            context.Reject(Lines, "EmptyClaim", "Include at least one expense.");

        return context.HasErrors ? null : new ExpenseClaim(employeeId, currency, lines);
    }
}

public sealed record ExpenseLineInput(
    string? Merchant,
    string? SpentOn,
    string? Amount
) : IMappable<ExpenseLine>
{
    public ExpenseLine? Map(MappingContext context)
    {
        var merchant = context.Required(Merchant);
        var spentOn = context.Parse<DateOnly>(SpentOn);
        if (context.TryParse(Amount, out decimal amount) && amount <= 0)
            context.Reject(Amount, "OutOfRange", "The expense amount must be greater than zero.");

        return context.HasErrors ? null : new ExpenseLine(merchant, spentOn, amount);
    }
}

public enum ExpenseCurrency
{
    GBP,
    EUR,
    USD
}

public sealed record ExpenseClaim(Guid EmployeeId, ExpenseCurrency Currency, IReadOnlyList<ExpenseLine> Lines);

public sealed record ExpenseLine(string Merchant, DateOnly SpentOn, decimal Amount);
