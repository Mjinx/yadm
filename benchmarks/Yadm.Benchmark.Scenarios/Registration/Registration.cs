using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Riok.Mapperly.Abstractions;
using Yadm.Common;

namespace Yadm.Benchmark.Scenarios.RegistrationScenario;

public sealed record RegistrationInput(string? Name, string? Age);
public sealed record Registration(string Name, int Age);
public sealed record RegistrationOutcome(Registration? Value, IReadOnlyList<ImportIssue> Errors);

public sealed class YadmRegistrationMapper : IMapper<RegistrationInput, Registration>
{
    public Registration? Map(RegistrationInput source, MappingContext context)
    {
        var name = context.Required(source.Name);
        var validAge = context.TryParse(source.Age, out int age);
        if (validAge && (age < 18 || age > 120))
            context.Reject(age, "OutOfRange", "Age must be between 18 and 120.", nameof(source.Age));

        return context.HasErrors ? null : new Registration(name, age);
    }

    public RegistrationOutcome Run(RegistrationInput input)
    {
        var result = this.Map(input);
        return new RegistrationOutcome(result.IsSuccess ? result.Value : null,
            result.Errors.Select(error => new ImportIssue(error.Path, error.Code)).ToArray());
    }
}

public sealed class FluentRegistrationMapper
{
    private readonly RegistrationValidator _validator = new();
    private readonly RegistrationDtoMapper _mapper = new();

    public RegistrationOutcome Run(RegistrationInput input)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return new RegistrationOutcome(null,
                validation.Errors.Select(error => new ImportIssue(error.PropertyName, error.ErrorCode)).ToArray());
        }

        return new RegistrationOutcome(_mapper.Map(input), []);
    }

    private sealed class RegistrationValidator : AbstractValidator<RegistrationInput>
    {
        public RegistrationValidator()
        {
            RuleFor(input => input.Name).NotEmpty().WithErrorCode("Required");
            RuleFor(input => input.Age).Custom((value, context) =>
            {
                string? code = null;
                if (string.IsNullOrWhiteSpace(value))
                    code = "Required";
                else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var age))
                    code = "Invalid";
                else if (age is < 18 or > 120)
                    code = "OutOfRange";

                if (code is not null)
                    context.AddFailure(new ValidationFailure(context.PropertyPath, code) { ErrorCode = code });
            });
        }
    }
}

[Mapper]
public partial class RegistrationDtoMapper
{
    [MapProperty(nameof(RegistrationInput.Age), nameof(Registration.Age), Use = nameof(ParseAge))]
    public partial Registration Map(RegistrationInput input);

    private static int ParseAge(string? age) =>
        int.Parse(age!, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
