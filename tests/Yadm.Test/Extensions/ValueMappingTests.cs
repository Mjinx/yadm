using System.Globalization;
using System.Text.Json;
using Shouldly;
using static Yadm.MappingErrorCodes;

namespace Yadm.Test;

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
            new MappingError("Bad", Invalid, "The value is not a valid Int32.", "4x")
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

    [Theory]
    [InlineData("2026-11-05")]
    [InlineData("2026-11-05T10:30:00")]
    [InlineData("2026-11-05T10:30:00Z")]
    [InlineData("2026-11-05T10:30:00.1234567+02:30")]
    [InlineData("2026-11-05T10:30:00.1234567890123456-14:00")]
    public void IsoDateTimeSyntaxMatchesSystemTextJson(string text)
    {
        var oracle = JsonSerializer.Deserialize<DateTimeOffset>($"\"{text}\"");

        TestMapping.Run(context =>
        {
            var actual = context.Parse<DateTimeOffset>(text);
            oracle.Offset.ShouldBe(actual.Offset);
            actual.Ticks.ShouldBe(oracle.Ticks);
        }).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2026-11-05 10:30:00")]
    [InlineData("2026-11-05 10:30 PM")]
    [InlineData("2026-11-05T10:30:00+2")]
    [InlineData("2026-11-05T10:30:00+14:01")]
    [InlineData("2026-11-05T10:30:00.12345678901234567Z")]
    [InlineData("2026-11-05T10:30:00Z trailing")]
    [InlineData("2026-02-30T10:30:00Z")]
    [InlineData(" 2026-11-05T10:30:00Z")]
    [InlineData("2026-11-05T10:30:00Z ")]
    [InlineData("2026-11-05t10:30:00z")]
    public void DatesRejectAnythingOutsideTheCompleteSupportedIsoSyntax(string text)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>($"\"{text}\""));

        var errors = TestMapping.Errors(context =>
        {
            context.Parse<DateTimeOffset>(text, "Offset");
            context.Parse<DateTime>(text, "DateTime");
        });

        errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("Offset", Invalid), ("DateTime", Invalid)]);
    }

    [Theory]
    [InlineData("2026-11-05T10:30:00Z")]
    [InlineData("2026-11-05 10:30:00")]
    [InlineData("2026-11-05T10:30")]
    public void DateOnlyAcceptsOnlyACompleteDate(string text) =>
        TestMapping.Errors(context => context.Parse<DateOnly>(text, "On"))
            .ShouldHaveSingleItem().Code.ShouldBe(Invalid);

    [Fact]
    public void IsoFractionsAreTruncatedToTicksLikeSystemTextJson()
    {
        var text = "2026-11-05T10:30:00.1234567890123456Z";
        var oracle = JsonSerializer.Deserialize<DateTimeOffset>($"\"{text}\"");

        TestMapping.Run(context => context.Parse<DateTimeOffset>(text).ShouldBe(oracle)).IsSuccess.ShouldBeTrue();
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

    [Fact]
    public void IsoDateParsingIgnoresCurrentCultureAndConfiguredFormatProvider()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var options = new MappingOptions { FormatProvider = CultureInfo.GetCultureInfo("de-DE") };

            var result = TestMapping.Errors(context =>
            {
                context.Parse<DateTimeOffset>("2026-11-05T10:30:00+02:00")
                    .ShouldBe(new DateTimeOffset(2026, 11, 5, 10, 30, 0, TimeSpan.FromHours(2)));
                context.Parse<DateOnly>("05.11.2026", "Localized");
            }, options);

            result.ShouldHaveSingleItem().Code.ShouldBe(Invalid);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
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
            "The value is not a valid OrderType. Expected one of: Standard, Express, Collection.", text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void ParseRejectsAbsentEnumText(string? text)
    {
        var errors = TestMapping.Errors(context =>
        {
            context.Parse<OrderType>(text, "Type").ShouldBe(default);
            context.TryParse<OrderType>(text, out var result, "Type").ShouldBeFalse();
            result.ShouldBe(default);
        });

        errors.ShouldBe([
            new MappingError("Type", Required, "A value is required.", text),
            new MappingError("Type", Required, "A value is required.", text)
        ]);
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

        errors.ShouldHaveSingleItem().ShouldBe(
            new MappingError("Bad", Invalid, "The value is not a valid SkuCode.", "abc"));
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
            .ShouldBe(new MappingError("DueOn", Invalid, "The value is not a valid DateOnly.", "2026-11-05"));
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

        errors.ShouldHaveSingleItem().ShouldBe(
            new MappingError("Bad", Invalid, "The value is not a valid SkuCode.", -1));
    }

    [Fact]
    public void SuccessfulRequiredParsersMustProduceUsableReferenceValues()
    {
        GC.KeepAlive(new NullProducingReference());
        GC.KeepAlive(new NullProducingCode());

        Should.Throw<InvalidOperationException>(() =>
            TestMapping.Run(context => context.Parse<string, NullProducingReference>("x", AlwaysNullParser)));

        TestMapping.Run(context =>
            context.Parse<string, int?>("x", AlwaysNullNullableValueParser).ShouldBeNull()).IsSuccess.ShouldBeTrue();

        Should.Throw<InvalidOperationException>(() =>
            TestMapping.Run(context => context.Parse<NullProducingCode>("x")));
    }

    [Fact]
    public void ParserFailuresAndFalseResultsKeepTheirNormalContract()
    {
        var input = new { Text = "x", Number = 42 };
        var errors = TestMapping.Errors(context =>
        {
            context.Parse(input.Text, (string _, out SkuCode? result) =>
            {
                result = null;
                return false;
            }).ShouldBeNull();
            context.Parse(input.Number, (int _, out int result) =>
            {
                result = 0;
                return false;
            }).ShouldBe(0);
        });

        errors.ShouldAllBe(error => error.Code == Invalid);
        errors.Select(error => error.AttemptedValue).ShouldBe(["x", 42]);
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
            .ShouldBe(new MappingError("Other", NotFound, "Product was not found.", "SKU-9"));
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

    [Fact]
    public void FailFastStopsScalarHelperWorkBeforeTheNextHelperRuns()
    {
        var calls = 0;
        var dictionary = new CountingDictionary<string, Product>();

        var errors = TestMapping.Errors(context =>
        {
            context.Parse<int>("x", "First");

            context.Required<string>(null, "Name").ShouldBeNull();
            context.TryRequired((int?)null, out var required).ShouldBeFalse();
            required.ShouldBe(0);
            context.Parse<int>("1", "Count").ShouldBe(0);
            context.TryParse("1", out int parsed).ShouldBeFalse();
            parsed.ShouldBe(0);
            context.Parse("1", (string _, out int value) =>
            {
                calls++;
                value = 1;
                return true;
            }, "Custom").ShouldBe(0);
            context.TryParse("1", (string _, out int value) =>
            {
                calls++;
                value = 1;
                return true;
            }, out _).ShouldBeFalse();
            context.Parse<OrderType>("Standard", "Type").ShouldBe(default);
            context.ParseOptional<OrderType>("Standard", "OptionalType").ShouldBeNull();
            context.ParseOptional<SkuCode>("ABC-1", "OptionalCode").ShouldBeNull();
            context.Lookup("SKU-1", dictionary, "Product").ShouldBeNull();
            context.TryLookup("SKU-1", dictionary, out var product).ShouldBeFalse();
            product.ShouldBeNull();
        }, MappingOptions.FailFast);

        errors.ShouldHaveSingleItem().Path.ShouldBe("First");
        calls.ShouldBe(0);
        dictionary.Queries.ShouldBe(0);
    }

    [Fact]
    public void BuiltInScalarMessagesDoNotEchoInputButKeepAttemptedValues()
    {
        const string Canary = "SENSITIVE-CANARY-7f2b";
        var products = new Dictionary<string, Product>();

        var result = TestMapping.Run(context =>
        {
            context.Parse<int>(Canary, "Count");
            context.Parse<OrderType>(Canary, "Type");
            context.Lookup(Canary, products, "Product");
        });

        var errors = result.Errors;
        errors.Count.ShouldBe(3);
        errors.Select(error => error.AttemptedValue).ShouldAllBe(value => Equals(value, Canary));
        errors.Select(error => error.Message).ShouldAllBe(message => !message.Contains(Canary));
        errors.Select(error => error.Code).ShouldBe([Invalid, Invalid, NotFound]);
        result.ToDictionary().Values.SelectMany(messages => messages)
            .ShouldAllBe(message => !message.Contains(Canary));
        Should.Throw<MappingException>(() => _ = result.Value).Message.ShouldNotContain(Canary);
    }

    private static bool AlwaysNullParser(string input, out NullProducingReference? result)
    {
        _ = input;
        result = null;
        return true;
    }

    private static bool AlwaysNullNullableValueParser(string input, out int? result)
    {
        _ = input;
        result = null;
        return true;
    }

    [Flags]
    private enum Access
    {
        Read = 1,
        Write = 2,
        ReadWrite = Read | Write,
        Execute = 4
    }

    private sealed class NullProducingReference;

    private sealed class NullProducingCode : IParsable<NullProducingCode>
    {
        public static NullProducingCode Parse(string s, IFormatProvider? provider) =>
            throw new NotSupportedException();

        public static bool TryParse(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s,
            IFormatProvider? provider,
            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out NullProducingCode result)
        {
            result = null!;
            return true;
        }
    }

    private sealed class CountingDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue> where TKey : notnull
    {
        public int Queries { get; private set; }
        public IEnumerable<TKey> Keys => [];
        public IEnumerable<TValue> Values => [];
        public int Count => 0;

        public TValue this[TKey key] => throw new KeyNotFoundException();

        public bool ContainsKey(TKey key) => false;

        public bool TryGetValue(TKey key, out TValue value)
        {
            Queries++;
            value = default!;
            return false;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<TKey, TValue>>().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
