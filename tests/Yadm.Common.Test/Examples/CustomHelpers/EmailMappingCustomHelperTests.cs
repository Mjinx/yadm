using Shouldly;

namespace Yadm.Common.Test;

public class EmailMappingCustomHelperTests
{
    private sealed record EmailInput(string? Contact);

    [Fact]
    public void InvalidEmailUsesTheCurrentContextPath()
    {
        // Arrange
        var src = new EmailInput("ann.example.com");
        var mapper = Mapper.Create((EmailInput source, MappingContext context) =>
            context.For("Customer").Email(source.Contact));

        // Act
        var result = mapper.Map(src);

        // Assert
        result.Errors.ShouldHaveSingleItem().ShouldBe(
            new MappingError("Customer.Contact", "Email", "'ann.example.com' is not an email address.", "ann.example.com"));
    }

    [Fact]
    public void MissingEmailUsesTheExplicitPath()
    {
        // Arrange
        var mapper = Mapper.Create((string? email, MappingContext context) =>
            context.Email(email, "BackupEmail"));

        // Act
        var result = mapper.Map(null);

        // Assert
        result.Errors.ShouldHaveSingleItem().ShouldBe(
            new MappingError("BackupEmail", MappingErrorCodes.Required, "A value is required."));
    }

    [Fact]
    public void ValidEmailMapsSuccessfully()
    {
        // Arrange
        var mapper = Mapper.Create((string? email, MappingContext context) => context.Email(email));

        // Act
        var result = mapper.Map("ann@example.com");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ann@example.com");
    }
}
