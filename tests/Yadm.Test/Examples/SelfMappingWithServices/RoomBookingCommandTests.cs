using Shouldly;

namespace Yadm.Test;

// A command that maps itself and looks up what it refers to through the context, which is an IServiceProvider.
public class RoomBookingCommandTests
{
    private readonly FakeRoomDirectory _rooms = new(new Room("ROOM-1", "Boardroom"));

    [Fact]
    public async Task ResolvesItsDependenciesFromTheServiceProvider()
    {
        var command = new BookRoomCommand("ROOM-1", ["ROOM-1"]);

        var result = await command.MapAsync(MappingOptions.Default, new SingleServiceProvider(_rooms));

        var booking = result.Value;
        booking.Room.ShouldBe(new Room("ROOM-1", "Boardroom"));
        booking.Alternatives.ShouldBe([new Room("ROOM-1", "Boardroom")]);
    }

    [Fact]
    public async Task ErrorsFoundWithTheServicesAreReportedAtTheirOwnPath()
    {
        var command = new BookRoomCommand("ROOM-1", ["ROOM-1", "MISSING"]);

        var result = await command.MapAsync(MappingOptions.Default, new SingleServiceProvider(_rooms));

        var error = result.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("Alternatives[1]");
        error.Code.ShouldBe(MappingErrorCodes.NotFound);
    }

    [Fact]
    public async Task WithoutAProviderTheCommandReportsItsMissingDependencyAsAnError()
    {
        var result = await new BookRoomCommand("ROOM-1", []).MapAsync();

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("ServiceUnavailable");
    }

    [Fact]
    public async Task NestedContextsShareTheProviderEvenWhenItIsInitializedLate()
    {
        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            context.GetService(typeof(IRoomDirectory)).ShouldBeNull();
            context.For("Child").InitializeServiceProvider(type => type == typeof(IRoomDirectory) ? _rooms : null);
            context.For("Other", 0).GetService(typeof(IRoomDirectory)).ShouldBe(_rooms);
            return await _rooms.FindAsync("ROOM-1", token);
        }).MapAsync(null);

        result.Value.ShouldBe(new Room("ROOM-1", "Boardroom"));
    }

    private sealed record BookRoomCommand(string RoomCode, string[] Alternatives) : IAsyncMappableObject<Booking>
    {
        public async ValueTask<Booking?> MapAsync(MappingContext context, CancellationToken cancellationToken)
        {
            if (context.GetService(typeof(IRoomDirectory)) is not IRoomDirectory rooms)
            {
                context.AddError("ServiceUnavailable", "The room directory is not available.");
                return null;
            }

            var room = await rooms.FindAsync(RoomCode, cancellationToken);
            if (room is null)
                context.Reject(RoomCode, MappingErrorCodes.NotFound, "The room was not found.");

            var alternatives = new List<Room>();
            for (var i = 0; i < Alternatives.Length; i++)
            {
                var alternative = await rooms.FindAsync(Alternatives[i], cancellationToken);
                if (alternative is null)
                    context.For(nameof(Alternatives), i).AddError(MappingErrorCodes.NotFound, "The room was not found.");
                else
                    alternatives.Add(alternative);
            }

            return context.HasErrors ? null : new Booking(room!, alternatives);
        }
    }

    private interface IRoomDirectory
    {
        ValueTask<Room?> FindAsync(string code, CancellationToken cancellationToken);
    }

    private sealed class FakeRoomDirectory(params Room[] rooms) : IRoomDirectory
    {
        public ValueTask<Room?> FindAsync(string code, CancellationToken cancellationToken) =>
            ValueTask.FromResult(rooms.FirstOrDefault(room => room.Code == code));
    }

    private sealed class SingleServiceProvider(IRoomDirectory rooms) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IRoomDirectory) ? rooms : null;
    }

    private sealed record Room(string Code, string Name);

    private sealed record Booking(Room Room, IReadOnlyList<Room> Alternatives);
}
