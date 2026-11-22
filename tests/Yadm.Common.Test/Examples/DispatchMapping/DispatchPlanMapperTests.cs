using Shouldly;

namespace Yadm.Common.Test;

public class DispatchPlanMapperTests
{
    private readonly IMapper<DispatchPlanInput, DispatchPlan> _mapper = new DispatchPlanMapper();

    [Fact]
    public void KeepsDictionaryKeysWhileMappingRouteObjectsAndTheirStops()
    {
        var input = new DispatchPlanInput("2026-11-05", new Dictionary<string, DeliveryRouteInput?>
        {
            ["north"] = new("Driver A", [new("Depot A", "2026-11-05"), new("Depot B", "2026-11-06")]),
            ["south"] = new("Driver B", [new("Depot C", "2026-11-07")])
        });

        var result = _mapper.Map(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DispatchOn.ShouldBe(new DateOnly(2026, 11, 5));
        result.Value.Routes.Keys.ShouldBe(["north", "south"]);
        result.Value.Routes["north"].Driver.ShouldBe("Driver A");
        result.Value.Routes["north"].Stops.ShouldBe(
            [new DeliveryStop("Depot A", new DateOnly(2026, 11, 5)),
                new DeliveryStop("Depot B", new DateOnly(2026, 11, 6))]);
        result.Value.Routes["south"].Stops.ShouldHaveSingleItem()
            .ShouldBe(new DeliveryStop("Depot C", new DateOnly(2026, 11, 7)));
    }

    [Theory]
    [InlineData("2026-11-06", true)]
    [InlineData("2026-11-07", false)]
    public void TheSameStopUsesTheCurrentPlansMappedDate(string dispatchOn, bool succeeds)
    {
        var input = new DispatchPlanInput(dispatchOn, new Dictionary<string, DeliveryRouteInput?>
        {
            ["north"] = new("Driver A", [new("Depot A", "2026-11-06")])
        });

        var result = _mapper.Map(input);

        result.IsSuccess.ShouldBe(succeeds);
        if (!succeeds)
            result.Errors.ShouldHaveSingleItem().Code.ShouldBe("BeforeDispatch");
    }

    [Fact]
    public void ReportsDictionaryKeysAndNestedStopPositionsWithoutCascadingParseErrors()
    {
        var input = new DispatchPlanInput("2026-11-05", new Dictionary<string, DeliveryRouteInput?>
        {
            ["north"] = new(null, [new("Depot A", "2026-11-04"), new(null, "tomorrow")]),
            ["south"] = null
        });

        var result = _mapper.Map(input);

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
        [
            ("Routes[north].Driver", MappingErrorCodes.Required),
            ("Routes[north].Stops[0].DeliverOn", "BeforeDispatch"),
            ("Routes[north].Stops[1].Destination", MappingErrorCodes.Required),
            ("Routes[north].Stops[1].DeliverOn", MappingErrorCodes.Invalid),
            ("Routes[south]", MappingErrorCodes.Required)
        ]);
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void AnInvalidParentDatePreventsDependentStopValidation()
    {
        var input = new DispatchPlanInput("tomorrow", new Dictionary<string, DeliveryRouteInput?>
        {
            ["north"] = new(null, [new(null, "yesterday")])
        });

        var result = _mapper.Map(input);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("DispatchOn");
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void MissingRoutesAreRequired()
    {
        var result = _mapper.Map(new DispatchPlanInput("2026-11-05", null));

        result.Errors.ShouldHaveSingleItem().ShouldBe(
            new MappingError("Routes", MappingErrorCodes.Required, "A value is required."));
    }

    [Fact]
    public void FailFastStopsWithinTheFirstRoute()
    {
        var input = new DispatchPlanInput("2026-11-05", new Dictionary<string, DeliveryRouteInput?>
        {
            ["north"] = new(null, [new(null, "yesterday")]),
            ["south"] = null
        });

        var result = _mapper.Map(input, MappingOptions.FailFast);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Routes[north].Driver");
    }
}
