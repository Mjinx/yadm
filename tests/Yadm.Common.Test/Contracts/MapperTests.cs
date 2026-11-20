using Shouldly;

namespace Yadm.Common.Test;

public class MapperTests
{
    private static readonly IMapper<AddressInput, Address> AddressMapper =
        Mapper.Create((AddressInput a, MappingContext c) =>
            new Address(c.Required(a.Street), c.Required(a.City), c.Required(a.Postcode)));

    [Fact]
    public void MapRunsAMapperAndCollectsEveryError()
    {
        var result = AddressMapper.Map(new AddressInput(null, " ", "SW1A 1AA"));

        result.Errors.Select(error => error.Path).ShouldBe(["Street", "City"]);
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void EachRunStartsWithAFreshContext()
    {
        AddressMapper.Map(new AddressInput(null, null, null)).IsSuccess.ShouldBeFalse();

        AddressMapper.Map(new AddressInput("1 High Street", "London", "SW1A 1AA"))
            .Value.ShouldBe(new Address("1 High Street", "London", "SW1A 1AA"));
    }

    [Fact]
    public void TheMapperReceivesTheRootContext()
    {
        var mapper = Mapper.Create((int _, MappingContext context) =>
        {
            context.Path.ShouldBe("");
            context.HasErrors.ShouldBeFalse();
            return context.Options;
        });

        mapper.Map(0).Value.ShouldBeSameAs(MappingOptions.Default);
        mapper.Map(0, MappingOptions.FailFast).Value.ShouldBeSameAs(MappingOptions.FailFast);
    }

    [Fact]
    public void OptionsApplyToTheWholeMapping() =>
        AddressMapper.Map(new AddressInput(null, null, null), MappingOptions.FailFast)
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("Street");

    [Fact]
    public void ObjectsThatMapThemselvesNeedNoMapper()
    {
        new AddressInput("1 High Street", "London", "SW1A 1AA").Map()
            .Value.ShouldBe(new Address("1 High Street", "London", "SW1A 1AA"));

        new AddressInput("1 High Street", null, null).Map(MappingOptions.FailFast)
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("City");
    }

    [Fact]
    public async Task AsynchronousMappersReceiveTheOptionsAndToken()
    {
        using var cancellation = new CancellationTokenSource();
        var mapper = Mapper.Create((string text, MappingContext context, CancellationToken token) =>
            ValueTask.FromResult<(MappingOptions, CancellationToken)?>((context.Options, token)));

        (await mapper.MapAsync("x")).Value.ShouldBe((MappingOptions.Default, CancellationToken.None));
        (await mapper.MapAsync("x", cancellation.Token)).Value.ShouldBe((MappingOptions.Default, cancellation.Token));
        (await mapper.MapAsync("x", MappingOptions.FailFast, cancellation.Token)).Value
            .ShouldBe((MappingOptions.FailFast, cancellation.Token));
    }

    [Fact]
    public async Task AMapperThatReturnsNullWithoutAnErrorIsABug()
    {
        var mapper = Mapper.Create((string _, MappingContext _) => (string?)null);
        var asyncMapper = Mapper.Create((string _, MappingContext _, CancellationToken _) => ValueTask.FromResult<string?>(null));

        Should.Throw<InvalidOperationException>(() => mapper.Map("x"))
            .Message.ShouldStartWith("The mapping function returned null without reporting an error.");
        await Should.ThrowAsync<InvalidOperationException>(() => asyncMapper.MapAsync("x").AsTask());
    }

    [Fact]
    public void ANullableValueTypeMayMapToNullWithoutAnError()
    {
        var mapper = Mapper.Create((string? text, MappingContext context) => context.ParseOptional<int>(text));

        var result = mapper.Map(null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeNull();
    }

    [Fact]
    public void OptionsHaveSafeDefaultsAndRejectAZeroErrorLimit()
    {
        MappingOptions.Default.StopOnFirstError.ShouldBeFalse();
        MappingOptions.Default.MaxErrors.ShouldBe(200);
        MappingOptions.Default.FormatProvider.ShouldBe(System.Globalization.CultureInfo.InvariantCulture);
        MappingOptions.FailFast.StopOnFirstError.ShouldBeTrue();
        (MappingOptions.Default with { MaxErrors = 10 }).MaxErrors.ShouldBe(10);

        Should.Throw<ArgumentOutOfRangeException>(() => new MappingOptions { MaxErrors = 0 });
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Should.Throw<ArgumentNullException>(() => ((IMapper<string, string>)null!).Map("x"));
        Should.Throw<ArgumentNullException>(() => ((IMappable<Address>)null!).Map());
        Should.Throw<ArgumentNullException>(() => Mapper.Create((Func<string, MappingContext, string?>)null!));
    }
}
