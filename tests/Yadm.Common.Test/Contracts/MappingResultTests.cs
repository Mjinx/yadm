using Shouldly;

namespace Yadm.Common.Test;

public class MappingResultTests
{
    private static readonly MappingError StreetMissing = new("ShipTo.Street", MappingErrorCodes.Required, "A value is required.");

    [Fact]
    public void ASuccessExposesItsValue()
    {
        var result = MappingResult<string>.Success("mapped");

        result.IsSuccess.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        result.Value.ShouldBe("mapped");
        result.PartialValue.ShouldBe("mapped");
        result.ToString().ShouldBe("Success: mapped");
    }

    [Fact]
    public void ReadingTheValueOfAFailureThrowsAMappingExceptionWithTheErrors()
    {
        var result = MappingResult<string>.Failure([StreetMissing]);

        result.IsSuccess.ShouldBeFalse();
        result.PartialValue.ShouldBeNull();
        result.ToString().ShouldBe("Failure: 1 error(s)");
        var exception = Should.Throw<MappingException>(() => result.Value);
        exception.Errors.ShouldBe([StreetMissing]);
        exception.Message.ShouldBe(
            $"Mapping failed with 1 error(s):{Environment.NewLine}  ShipTo.Street: A value is required.");
    }

    [Fact]
    public void ASuccessNeedsAValueButANullableValueTypeMayBeNull()
    {
        Should.Throw<ArgumentNullException>(() => MappingResult<string>.Success(null!));

        MappingResult<int?>.Success(null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AFailureNeedsAnErrorAndNoNullErrors()
    {
        Should.Throw<ArgumentException>(() => MappingResult<string>.Failure([]));
        Should.Throw<ArgumentException>(() => MappingResult<string>.Failure([StreetMissing, null!]));
    }

    [Fact]
    public void ToDictionaryGroupsMessagesByPathInTheOrderFound()
    {
        var result = MappingResult<string>.Failure(
        [
            new MappingError("Lines[0].Sku", "Required", "A value is required."),
            new MappingError("", "Rule", "The order is empty."),
            new MappingError("Lines[0].Sku", "NotFound", "Product 'x' was not found.")
        ]);

        var errors = result.ToDictionary();

        errors.Keys.ShouldBe(["Lines[0].Sku", ""]);
        errors["Lines[0].Sku"].ShouldBe(["A value is required.", "Product 'x' was not found."]);
        errors[""].ShouldBe(["The order is empty."]);
    }

    [Fact]
    public void TheExceptionMessageListsTheFirstTenErrors()
    {
        var errors = Enumerable.Range(0, 12).Select(i => new MappingError($"Lines[{i}]", "Code", "Message.")).ToArray();

        var message = new MappingException(errors).Message;

        message.ShouldStartWith("Mapping failed with 12 error(s):");
        message.ShouldContain("Lines[9]: Message.");
        message.ShouldNotContain("Lines[10]");
        message.ShouldEndWith("...and 2 more.");
    }

    [Fact]
    public void AnErrorDescribesItselfAndNeedsAllItsParts()
    {
        StreetMissing.ToString().ShouldBe("ShipTo.Street: A value is required.");
        new MappingError("", "Rule", "The order is empty.").ToString().ShouldBe("The order is empty.");

        Should.Throw<ArgumentNullException>(() => new MappingError(null!, "Code", "Message."));
        Should.Throw<ArgumentNullException>(() => new MappingError("Path", null!, "Message."));
        Should.Throw<ArgumentNullException>(() => new MappingError("Path", "Code", null!));
    }
}
