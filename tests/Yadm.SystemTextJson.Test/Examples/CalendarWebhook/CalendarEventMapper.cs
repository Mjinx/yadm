using Yadm.Common;

namespace Yadm.SystemTextJson.Test;

public sealed class CalendarEventMapper : IMapper<CalendarEventInput, ScheduledEvent>
{
    public ScheduledEvent? Map(CalendarEventInput source, MappingContext context)
    {
        var title = context.Required(source.Title);
        var hasStart = context.TryParse(source.StartsAt, out DateTimeOffset startsAt);
        var hasEnd = context.TryParse(source.EndsAt, out DateTimeOffset endsAt);
        if (hasStart && hasEnd && endsAt <= startsAt)
            context.Reject(source.EndsAt, "InvalidRange", "The event must end after it starts.");

        return context.HasErrors ? null : new ScheduledEvent(title, startsAt, endsAt);
    }
}

public sealed record CalendarEventInput(string? Title, string? StartsAt, string? EndsAt);

public sealed record ScheduledEvent(string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
