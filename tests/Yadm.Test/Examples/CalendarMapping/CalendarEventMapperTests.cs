using Shouldly;

namespace Yadm.Test;

public class CalendarEventMapperTests
{
    private readonly IMapper<CalendarEventInput, ScheduledEvent> _mapper = new CalendarEventMapper();

    [Fact]
    public void MapsAnEventWhoseTimesHaveDifferentOffsets()
    {
        var input = new CalendarEventInput("Customer demo", "2026-11-05T14:00:00+02:00", "2026-11-05T12:30:00Z");

        var result = _mapper.Map(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new ScheduledEvent("Customer demo",
            new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 11, 5, 12, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("2026-11-05T12:00:00Z")]
    [InlineData("2026-11-05T11:59:00Z")]
    public void RejectsAnEndAtOrBeforeTheStart(string endsAt)
    {
        var input = new CalendarEventInput("Customer demo", "2026-11-05T14:00:00+02:00", endsAt);

        var result = _mapper.Map(input);

        result.IsSuccess.ShouldBeFalse();
        result.PartialValue.ShouldBeNull();
        result.Errors.ShouldHaveSingleItem().ShouldBe(new MappingError(
            "EndsAt", "InvalidRange", "The event must end after it starts.", endsAt));
    }

    [Theory]
    [InlineData("not a date", "2026-11-05T12:30:00Z", "StartsAt", MappingErrorCodes.Invalid)]
    [InlineData("2026-11-05T12:00:00Z", null, "EndsAt", MappingErrorCodes.Required)]
    public void ReportsBadDatesWithoutInventingARangeError(
        string startsAt, string? endsAt, string path, string code)
    {
        var result = _mapper.Map(new CalendarEventInput(null, startsAt, endsAt));

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("Title", MappingErrorCodes.Required), (path, code)]);
        result.PartialValue.ShouldBeNull();
    }
}
