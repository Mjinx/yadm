using NSubstitute;
using Shouldly;
using Yadm;

namespace Yadm.SystemTextJson.Test;

public class ReservationJsonTests
{
    private readonly IRoomRepository _rooms = Substitute.For<IRoomRepository>();

    [Fact]
    public async Task MapsAReservationMessageThenResolvesItsRoom()
    {
        var room = new MeetingRoom("ROOM-1", "Boardroom");
        _rooms.FindByCodeAsync(room.Code, Arg.Any<CancellationToken>()).Returns(room);
        var mapper = new AsyncJsonInputMapper<ReservationInput, RoomReservation>(
            ReservationJsonContext.Default.ReservationInput, new ReservationMapper(_rooms));
        using var cancellation = new CancellationTokenSource();

        var result = await mapper.MapAsync("""
            {"roomCode":"ROOM-1","startsAt":"2026-11-05T12:00:00Z"}
            """, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Room.ShouldBe(room);
        result.Value.StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        await _rooms.Received(1).FindByCodeAsync(room.Code, cancellation.Token);
    }

    [Theory]
    [InlineData("{", JsonMappingErrorCodes.InvalidJson)]
    [InlineData("""{"roomCode":"ROOM-1","startsAt":"bad"}""", MappingErrorCodes.Invalid)]
    public async Task UnreadableOrInvalidMessagesDoNotTriggerLookups(string json, string firstCode)
    {
        var mapper = new AsyncJsonInputMapper<ReservationInput, RoomReservation>(
            ReservationJsonContext.Default.ReservationInput, new ReservationMapper(_rooms));

        var result = await mapper.MapAsync(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors[0].Code.ShouldBe(firstCode);
        await _rooms.DidNotReceiveWithAnyArgs().FindByCodeAsync("", CancellationToken.None);
    }

    [Fact]
    public async Task AnUnknownRoomIsReportedAtTheInputField()
    {
        var mapper = new AsyncJsonInputMapper<ReservationInput, RoomReservation>(
            ReservationJsonContext.Default.ReservationInput, new ReservationMapper(_rooms));

        var result = await mapper.MapAsync("""
            {"roomCode":"unknown","startsAt":"2026-11-05T12:00:00Z"}
            """);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("RoomCode");
        error.Code.ShouldBe(MappingErrorCodes.NotFound);
        result.PartialValue.ShouldBeNull();
    }
}
