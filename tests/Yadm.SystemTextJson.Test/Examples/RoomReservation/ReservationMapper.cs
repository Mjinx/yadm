using Yadm.Common;

namespace Yadm.SystemTextJson.Test;

public sealed class ReservationMapper(IRoomRepository rooms) : IAsyncMapper<ReservationInput, RoomReservation>
{
    public async ValueTask<RoomReservation?> MapAsync(
        ReservationInput source, MappingContext context, CancellationToken cancellationToken)
    {
        var roomCode = context.Required(source.RoomCode);
        var startsAt = context.Parse<DateTimeOffset>(source.StartsAt);
        if (context.HasErrors)
            return null;

        var room = await rooms.FindByCodeAsync(roomCode, cancellationToken);
        if (room is null)
        {
            context.Reject(source.RoomCode, MappingErrorCodes.NotFound, "The meeting room was not found.");
            return null;
        }

        return new RoomReservation(room, startsAt);
    }
}

public interface IRoomRepository
{
    Task<MeetingRoom?> FindByCodeAsync(string code, CancellationToken cancellationToken);
}

public sealed record ReservationInput(string? RoomCode, string? StartsAt);

public sealed record MeetingRoom(string Code, string Name);

public sealed record RoomReservation(MeetingRoom Room, DateTimeOffset StartsAt);
