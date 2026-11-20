using System.Globalization;
using Shouldly;
using static Yadm.Common.MappingErrorCodes;

namespace Yadm.Common.Test;

public class ValueMappingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void RequiredRejectsNullEmptyAndWhitespaceText(string? street)
    {
        var src = new { Street = street };

        var errors = TestMapping.Errors(context => context.Required(src.Street).ShouldBeNull());

        errors.ShouldHaveSingleItem().ShouldBe(new MappingError("Street", Required, "A value is required.", street));
    }

    [Fact]
    public void RequiredReturnsPresentValuesUnchanged()
    {
        var src = new { Name = " ok ", Age = (int?)0, Tags = new List<string>() };

        TestMapping.Run(context =>
        {
            context.Required(src.Name).ShouldBe(" ok ");
            context.Required(src.Age).ShouldBe(0);
            context.Required(src.Tags).ShouldBeSameAs(src.Tags);
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RequiredReportsMissingNullableValues()
    {
        var src = new { Age = (int?)null };

        TestMapping.Errors(context => context.Required(src.Age).ShouldBe(0))
            .ShouldHaveSingleItem().Path.ShouldBe("Age");
    }

    [Fact]
    public void AnExplicitNameReplacesTheInferredOne()
    {
        var src = new { Street = (string?)null };

        TestMapping.Errors(context => context.Required(src.Street, "Line1"))
            .ShouldHaveSingleItem().Path.ShouldBe("Line1");
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData(" 7 ", 7)]
    [InlineData("-1", -1)]
    public void ParseReadsNumbers(string text, int expected) =>
        TestMapping.Run(context => context.Parse<int>(text).ShouldBe(expected)).IsSuccess.ShouldBeTrue();

    [Fact]
    public void ParseReportsAbsentAndInvalidText()
    {
        var src = new { Missing = (string?)null, Bad = "4x" };

        var errors = TestMapping.Errors(context =>
        {
            context.Parse<int>(src.Missing).ShouldBe(0);
            context.Parse<int>(src.Bad).ShouldBe(0);
        });

        errors.ShouldBe(
        [
            new MappingError("Missing", Required, "A value is required."),
            new MappingError("Bad", Invalid, "'4x' is not a valid Int32.", "4x")
        ]);
    }

    [Fact]
    public void ParseUsesTheInvariantCultureByDefaultWhateverTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            TestMapping.Run(context => context.Parse<decimal>("1.5").ShouldBe(1.5m)).IsSuccess.ShouldBeTrue();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ParseUsesTheConfiguredFormatProvider()
    {
        var options = new MappingOptions { FormatProvider = CultureInfo.GetCultureInfo("fr-FR") };

        TestMapping.Run(context => context.Parse<decimal>("1,5").ShouldBe(1.5m), options).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("12,50")]
    [InlineData("1,234.5")]
    public void FractionalNumbersRejectThousandsSeparatorsInsteadOfMisreadingThem(string text)
    {
        var errors = TestMapping.Errors(context =>
        {
            context.Parse<decimal>(text, "Price");
            context.Parse<double>(text, "Ratio");
        });

        errors.Select(error => error.Code).ShouldBe([Invalid, Invalid]);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void FloatingPointNumbersMustBeFinite(string text) =>
        TestMapping.Errors(context => context.Parse<double>(text, "Ratio")).ShouldHaveSingleItem().Code.ShouldBe(Invalid);

    [Fact]
    public void FractionalNumbersAcceptDecimalPointsAndExponents()
    {
        TestMapping.Run(context =>
        {
            context.Parse<decimal>("12.50").ShouldBe(12.50m);
            context.Parse<double>("-1.5e3").ShouldBe(-1500d);
            context.Parse<float>(" 2.5 ").ShouldBe(2.5f);
        }).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("05/11/2026")]
    [InlineData("11/05/2026")]
    [InlineData("5 Nov 2026")]
    public void DatesMustBeIso8601SoTheyCannotBeMisread(string text)
    {
        var errors = TestMapping.Errors(context =>
        {
            context.Parse<DateTimeOffset>(text, "At");
            context.Parse<DateTime>(text, "At");
            context.Parse<DateOnly>(text, "On");
        });

        errors.Select(error => error.Code).ShouldBe([Invalid, Invalid, Invalid]);
    }

    [Fact]
    public void IsoDatesParseWithOrWithoutATime()
    {
        TestMapping.Run(context =>
        {
            context.Parse<DateOnly>("2026-11-05").ShouldBe(new DateOnly(2026, 11, 5));
            context.Parse<DateTimeOffset>("2026-11-05").ShouldBe(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
            context.Parse<DateTimeOffset>("2026-11-05T10:30:00.123Z").Millisecond.ShouldBe(123);
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AMessagePassedAsTheNameIsRejected()
    {
        var src = new { Email = (string?)null };

        TestMapping.Run(context =>
            Should.Throw<ArgumentException>(() => context.Required(src.Email, "Email is required.")));
    }

    [Fact]
    public void DatesWithoutAnOffsetAreReadAsUtc()
    {
        TestMapping.Run(context =>
        {
            context.Parse<DateTimeOffset>("2026-11-05T10:00:00")
                .ShouldBe(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
            context.Parse<DateTimeOffset>("2026-11-05T10:00:00+02:00").Offset.ShouldBe(TimeSpan.FromHours(2));

            var converted = context.Parse<DateTime>("2026-11-05T10:00:00+02:00");
            converted.Kind.ShouldBe(DateTimeKind.Utc);
            converted.ShouldBe(new DateTime(2026, 11, 5, 8, 0, 0));
            context.Parse<DateTime>("2026-11-05").Kind.ShouldBe(DateTimeKind.Utc);
        }).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseOptionalReturnsNullForAbsentText(string? text)
    {
        TestMapping.Run(context =>
        {
            context.ParseOptional<int>(text).ShouldBeNull();
            context.ParseOptional<OrderType>(text).ShouldBeNull();
            context.ParseOptional<SkuCode>(text).ShouldBeNull();
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ParseOptionalReturnsPresentValuesIncludingZero()
    {
        TestMapping.Run(context =>
        {
            context.ParseOptional<int>("0").ShouldBe(0);
            context.ParseOptional<OrderType>("express").ShouldBe(OrderType.Express);
            context.ParseOptional<SkuCode>("ABC-1").ShouldBe(new SkuCode("ABC-1"));
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ParseOptionalStillReportsInvalidText()
    {
        var src = new { Count = "many", Type = "Huge", Code = "lower" };

        var errors = TestMapping.Errors(context =>
        {
            context.ParseOptional<int>(src.Count).ShouldBeNull();
            context.ParseOptional<OrderType>(src.Type).ShouldBeNull();
            context.ParseOptional<SkuCode>(src.Code).ShouldBeNull();
        });

        errors.Select(error => (error.Path, error.Code)).ShouldBe([("Count", Invalid), ("Type", Invalid), ("Code", Invalid)]);
    }

    [Theory]
    [InlineData("Standard", OrderType.Standard)]
    [InlineData("express", OrderType.Express)]
    [InlineData(" COLLECTION ", OrderType.Collection)]
    [InlineData("1", OrderType.Express)]
    public void ParseReadsDefinedEnumMembers(string text, OrderType expected) =>
        TestMapping.Run(context => context.Parse<OrderType>(text).ShouldBe(expected)).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("unknown")]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("999999999999999999999")]
    public void ParseRejectsUndefinedEnumValuesAndListsTheAcceptedNames(string text)
    {
        var error = TestMapping.Errors(context => context.Parse<OrderType>(text, "Type")).ShouldHaveSingleItem();

        error.ShouldBe(new MappingError("Type", Invalid,
            $"'{text}' is not a valid OrderType. Expected one of: Standard, Express, Collection.", text));
    }

    [Fact]
    public void FlagCombinationsMustBeDeclaredMembers()
    {
        var errors = TestMapping.Errors(context =>
        {
            context.Parse<Access>("Read, Write").ShouldBe(Access.ReadWrite);
            context.Parse<Access>("Read, Execute", "Access");
        });

        errors.ShouldHaveSingleItem().AttemptedValue.ShouldBe("Read, Execute");
    }

    [Fact]
    public void ParseReadsYourOwnIParsableTypes()
    {
        var src = new { Sku = "ABC-1", Bad = "abc" };

        var errors = TestMapping.Errors(context =>
        {
            context.Parse<SkuCode>(src.Sku).ShouldBe(new SkuCode("ABC-1"));
            context.Parse<SkuCode>(src.Bad).ShouldBeNull();
        });

        errors.ShouldHaveSingleItem().ShouldBe(new MappingError("Bad", Invalid, "'abc' is not a valid SkuCode.", "abc"));
    }

    [Fact]
    public void ParseAcceptsACustomParserForOtherFormats()
    {
        var src = new { PlacedOn = "05/11/2026", DueOn = "2026-11-05" };

        var errors = TestMapping.Errors(context =>
        {
            context.Parse(src.PlacedOn, (string text, out DateOnly date) =>
                DateOnly.TryParseExact(text, "dd/MM/yyyy", out date)).ShouldBe(new DateOnly(2026, 11, 5));
            context.Parse(src.DueOn, (string text, out DateOnly date) =>
                DateOnly.TryParseExact(text, "dd/MM/yyyy", out date));
            context.Parse<string, DateOnly>(src.DueOn, DateOnly.TryParse).ShouldBe(new DateOnly(2026, 11, 5));
        });

        errors.ShouldHaveSingleItem()
            .ShouldBe(new MappingError("DueOn", Invalid, "'2026-11-05' is not a valid DateOnly.", "2026-11-05"));
    }

    [Fact]
    public void CustomParsersAreNotCalledForAbsentInput()
    {
        var src = new { Date = (string?)null };
        var calls = 0;

        var errors = TestMapping.Errors(context => context.Parse(src.Date, (string? _, out DateOnly date) =>
        {
            calls++;
            date = default;
            return true;
        }));

        calls.ShouldBe(0);
        errors.ShouldHaveSingleItem().Code.ShouldBe(Required);
    }

    [Fact]
    public void CustomParsersAcceptInputThatIsNotText()
    {
        var src = new { Code = 42, Bad = -1 };

        var errors = TestMapping.Errors(context =>
        {
            context.Parse(src.Code, (int raw, out SkuCode? code) => SkuCode.TryCreate(raw, out code))
                .ShouldBe(new SkuCode("SKU-42"));
            context.Parse(src.Bad, (int raw, out SkuCode? code) => SkuCode.TryCreate(raw, out code));
        });

        errors.ShouldHaveSingleItem().ShouldBe(new MappingError("Bad", Invalid, "'-1' is not a valid SkuCode.", -1));
    }

    [Fact]
    public void CustomParserExceptionsPropagate()
    {
        var fault = new InvalidOperationException("Parser fault");

        Should.Throw<InvalidOperationException>(() =>
                TestMapping.Run(context => context.Parse("x", (string _, out int _) => throw fault)))
            .ShouldBeSameAs(fault);
    }

    [Fact]
    public void LookupFindsEntitiesByKeyAndReportsUnknownKeys()
    {
        var products = new Dictionary<string, Product> { ["SKU-1"] = new("SKU-1", 10m, 5) };
        var line = new { Sku = "SKU-1", Other = "SKU-9" };

        var errors = TestMapping.Errors(context =>
        {
            context.Lookup(line.Sku, products).ShouldBeSameAs(products["SKU-1"]);
            context.Lookup(line.Other, products).ShouldBeNull();
        });

        errors.ShouldHaveSingleItem()
            .ShouldBe(new MappingError("Other", NotFound, "Product 'SKU-9' was not found.", "SKU-9"));
    }

    [Fact]
    public void LookupReportsAbsentKeysAsRequiredAndNullEntriesAsNotFound()
    {
        var customers = new Dictionary<string, Customer?> { ["c1"] = null };
        var src = new { Missing = (string?)null, Empty = "c1" };

        var errors = TestMapping.Errors(context =>
        {
            context.Lookup(src.Missing, customers);
            context.Lookup(src.Empty, customers);
        });

        errors.Select(error => (error.Path, error.Code)).ShouldBe([("Missing", Required), ("Empty", NotFound)]);
    }

    [Fact]
    public void TryHelpersReportLikeTheirTwinsAndSayWhetherTheValueIsUsable()
    {
        var src = new { Name = (string?)null, Age = (int?)7, Qty = "x", Count = "0", Type = "Huge", Sku = "SKU-9" };
        var products = new Dictionary<string, Product>();

        var errors = TestMapping.Errors(context =>
        {
            context.TryRequired(src.Name, out _).ShouldBeFalse();
            context.TryRequired(src.Age, out var age).ShouldBeTrue();
            age.ShouldBe(7);
            context.TryParse(src.Qty, out int _).ShouldBeFalse();
            context.TryParse(src.Count, out int count).ShouldBeTrue();
            count.ShouldBe(0);
            context.TryParse(src.Type, out OrderType _).ShouldBeFalse();
            context.TryLookup(src.Sku, products, out _).ShouldBeFalse();
        });

        errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("Name", Required), ("Qty", Invalid), ("Type", Invalid), ("Sku", NotFound)]);
    }

    [Fact]
    public void TryParseLetsARuleUseAValueWithoutRepeatingItsError()
    {
        var line = new { Quantity = "x", Other = "0" };

        var errors = TestMapping.Errors(context =>
        {
            if (context.TryParse(line.Quantity, out int quantity) && quantity < 1)
                context.Reject(quantity, "OutOfRange", "Must be at least 1.", nameof(line.Quantity));
            if (context.TryParse(line.Other, out int other) && other < 1)
                context.Reject(other, "OutOfRange", "Must be at least 1.", nameof(line.Other));
        });

        errors.Select(error => (error.Path, error.Code)).ShouldBe([("Quantity", Invalid), ("Other", "OutOfRange")]);
    }

    [Flags]
    private enum Access
    {
        Read = 1,
        Write = 2,
        ReadWrite = Read | Write,
        Execute = 4
    }
}
