using System.Text.Json;
using Shouldly;
using Yadm.Common;

namespace Yadm.SystemTextJson.Test;

public class CalendarJsonTests
{
    private static readonly IMapper<string?, ScheduledEvent> Calendar =
        new JsonInputMapper<CalendarEventInput, ScheduledEvent>(
            CalendarJsonContext.Default.CalendarEventInput, new CalendarEventMapper());

    [Fact]
    public void MapsAFlatCalendarWebhookFromJsonToTheDomainModel()
    {
        var result = Calendar.Map("""
            {
              "title": "Customer demo",
              "startsAt": "2026-11-05T12:00:00Z",
              "endsAt": "2026-11-05T12:30:00Z"
            }
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new ScheduledEvent("Customer demo",
            new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 5, 12, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ValidJsonStillGoesThroughDomainRules()
    {
        var result = Calendar.Map("""
            {"title":"Customer demo","startsAt":"2026-11-05T12:00:00Z","endsAt":"2026-11-05T11:00:00Z"}
            """);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("EndsAt");
        result.Errors[0].Code.ShouldBe("InvalidRange");
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void CallerOptionsCanChangePropertyNamingAndAllowComments()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        var mapper = new JsonInputMapper<CalendarEventInput, ScheduledEvent>(
            new CalendarJsonContext(new JsonSerializerOptions(options)).CalendarEventInput, new CalendarEventMapper());

        var result = mapper.Map("""
            {
              // Calendar provider uses snake_case.
              "title": "Customer demo",
              "starts_at": "2026-11-05T12:00:00Z",
              "ends_at": "2026-11-05T12:30:00Z"
            }
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Customer demo");
        result.Value.StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        options.TypeInfoResolver.ShouldBeNull();
        options.IsReadOnly.ShouldBeFalse();
    }

    [Fact]
    public void AcceptsCallerOptionsWithoutRequiringGeneratedMetadata()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var mapper = new JsonInputMapper<CalendarEventInput, ScheduledEvent>(new CalendarEventMapper(), options);

        var result = mapper.Map("""
            {
              "title": "Customer demo",
              "starts_at": "2026-11-05T12:00:00Z",
              "ends_at": "2026-11-05T12:30:00Z"
            }
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Value.StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        options.TypeInfoResolver.ShouldBeNull();
        options.IsReadOnly.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    public void MalformedOrWrongShapedJsonDoesNotReachTheDomainMapper(string json)
    {
        var result = Calendar.Map(json);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("InvalidJson");
        result.PartialValue.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("null")]
    public void MissingJsonOrAJsonNullIsRequired(string? json)
    {
        var result = Calendar.Map(json);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(MappingErrorCodes.Required);
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void JsonAdapterDoesNotTurnDomainMapperBugsIntoInputErrors()
    {
        var mapper = new JsonInputMapper<CalendarEventInput, ScheduledEvent>(
            CalendarJsonContext.Default.CalendarEventInput,
            Mapper.Create<CalendarEventInput, ScheduledEvent>((_, _) =>
                throw new JsonException("A failure after deserialization.")));

        Should.Throw<JsonException>(() => mapper.Map("{}"));
    }
}
