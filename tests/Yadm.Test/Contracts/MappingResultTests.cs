using Shouldly;

namespace Yadm.Test;

public class MappingResultTests
{
    private static readonly MappingError StreetMissing = new("ShipTo.Street", MappingErrorCodes.Required, "A value is required.");

    [Fact]
    public void ASuccessExposesItsValue()
    {
        var result = MappingResult<string>.Success("mapped");

        result.IsSuccess.ShouldBeTrue();
        result.HasPartialValue.ShouldBeFalse();
        result.ErrorCount.ShouldBe(0);
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
        result.HasPartialValue.ShouldBeFalse();
        result.ErrorCount.ShouldBe(1);
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

        var success = MappingResult<int?>.Success(null);

        success.IsSuccess.ShouldBeTrue();
        success.HasPartialValue.ShouldBeFalse();
        success.ErrorCount.ShouldBe(0);
        success.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void AFailureNeedsAnErrorAndNoNullErrors()
    {
        Should.Throw<ArgumentException>(() => MappingResult<string>.Failure([]));
        Should.Throw<ArgumentException>(() => MappingResult<string>.Failure([StreetMissing, null!]));
    }

    [Fact]
    public void AFailureOwnsItsErrorSnapshotAndKeepsItsOutcome()
    {
        var source = new List<MappingError> { StreetMissing };

        var result = MappingResult<string>.Failure(source);
        source.Clear();

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCount.ShouldBe(1);
        result.Errors.ShouldBe([StreetMissing]);
        Should.Throw<MappingException>(() => result.Value);
        Should.Throw<NotSupportedException>(() => ((ICollection<MappingError>)result.Errors).Clear());
        Should.Throw<NotSupportedException>(() => ((IList<MappingError>)result.Errors)[0] =
            new MappingError("", "Other", "Other."));
    }

    [Fact]
    public void AnExceptionOwnsItsErrorSnapshot()
    {
        var source = new List<MappingError> { StreetMissing };

        var exception = new MappingException(source);
        source.Clear();

        exception.Errors.ShouldBe([StreetMissing]);
        exception.Message.ShouldContain("ShipTo.Street: A value is required.");
        Should.Throw<NotSupportedException>(() => ((ICollection<MappingError>)exception.Errors).Clear());
        Should.Throw<NotSupportedException>(() => ((IList<MappingError>)exception.Errors)[0] =
            new MappingError("", "Other", "Other."));
    }

    [Fact]
    public void AResultValueExceptionReusesTheOwnedSnapshot()
    {
        var result = MappingResult<string>.Failure([StreetMissing]);

        var exception = Should.Throw<MappingException>(() => result.Value);

        exception.Errors.ShouldBeSameAs(result.Errors);
    }

    [Fact]
    public void DictionaryCopiesAreIndependent()
    {
        var result = MappingResult<string>.Failure([StreetMissing]);

        var first = result.ToDictionary();
        first["ShipTo.Street"][0] = "Changed.";
        first.Clear();

        result.ToDictionary()["ShipTo.Street"].ShouldBe(["A value is required."]);
        result.Errors.ShouldBe([StreetMissing]);
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
    public void ToErrorDetailsPreservesEachErrorInOrderWithoutAttemptedValues()
    {
        var result = MappingResult<string>.Failure(
        [
            new MappingError("email", "REQUIRED", "Must not be empty", "submitted-value"),
            new MappingError("email", "INVALID_FORMAT", "Must be a valid email address")
        ]);

        result.ToErrorDetails().ShouldBe(
        [
            new MappingErrorDetail("email", "REQUIRED", "Must not be empty"),
            new MappingErrorDetail("email", "INVALID_FORMAT", "Must be a valid email address")
        ]);
        MappingResult<string>.Success("mapped").ToErrorDetails().ShouldBeEmpty();
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
    public void TheExceptionRejectsANullErrorListBeforeReadingIt()
    {
        var exception = Should.Throw<ArgumentNullException>(
            () => new MappingException((IReadOnlyList<MappingError>)null!));

        exception.ParamName.ShouldBe("errors");
    }

    [Fact]
    public void TheExceptionNeedsAnErrorAndNoNullErrors()
    {
        Should.Throw<ArgumentException>(() => new MappingException([]));
        Should.Throw<ArgumentException>(() => new MappingException([StreetMissing, null!]));
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

    [Fact]
    public void ErrorInitializersAndWithUpdatesPreserveRequiredFieldInvariants()
    {
        Should.Throw<ArgumentNullException>(() => new MappingError("", "Code", "Message.") { Path = null! })
            .ParamName.ShouldBe("Path");
        Should.Throw<ArgumentNullException>(() => new MappingError("", "Code", "Message.") { Code = null! })
            .ParamName.ShouldBe("Code");
        Should.Throw<ArgumentNullException>(() => new MappingError("", "Code", "Message.") { Message = null! })
            .ParamName.ShouldBe("Message");

        Should.Throw<ArgumentNullException>(() => StreetMissing with { Path = null! }).ParamName.ShouldBe("Path");
        Should.Throw<ArgumentNullException>(() => StreetMissing with { Code = null! }).ParamName.ShouldBe("Code");
        Should.Throw<ArgumentNullException>(() => StreetMissing with { Message = null! })
            .ParamName.ShouldBe("Message");
    }

    [Fact]
    public void ValidErrorUpdatesKeepRecordBehavior()
    {
        var updated = StreetMissing with { Path = "", Message = "Updated." };
        var (path, code, message, attemptedValue) = updated;

        path.ShouldBe("");
        code.ShouldBe(MappingErrorCodes.Required);
        message.ShouldBe("Updated.");
        attemptedValue.ShouldBeNull();
        updated.ShouldBe(new MappingError("", MappingErrorCodes.Required, "Updated."));
        updated.GetHashCode().ShouldBe(new MappingError("", MappingErrorCodes.Required, "Updated.").GetHashCode());
        updated.ToString().ShouldBe("Updated.");
        MappingResult<string>.Failure([updated]).ToDictionary()[""].ShouldBe(["Updated."]);
    }
}
