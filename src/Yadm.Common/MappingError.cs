namespace Yadm.Common;

/// <summary>
/// One problem found while mapping: where it is (<see cref="Path"/>), what kind of problem it is
/// (<see cref="Code"/>), and a readable explanation (<see cref="Message"/>).
/// </summary>
/// <param name="Path">
/// Where the problem is in the source, such as <c>Lines[2].Quantity</c>. Empty for the source itself.
/// </param>
/// <param name="Code">A stable, machine-readable kind of problem, such as <see cref="MappingErrorCodes.Required"/>.</param>
/// <param name="Message">A readable description of the problem.</param>
/// <param name="AttemptedValue">The source value that could not be mapped, if there was one.</param>
public sealed record MappingError(string Path, string Code, string Message, object? AttemptedValue = null)
{
    /// <summary>Where the problem is in the source. Empty for the source itself.</summary>
    public string Path { get; init; } = Path ?? throw new ArgumentNullException(nameof(Path));

    /// <summary>A stable, machine-readable kind of problem.</summary>
    public string Code { get; init; } = Code ?? throw new ArgumentNullException(nameof(Code));

    /// <summary>A readable description of the problem.</summary>
    public string Message { get; init; } = Message ?? throw new ArgumentNullException(nameof(Message));

    /// <summary>Returns <c>Path: Message</c>, or only the message when the path is empty.</summary>
    public override string ToString() => Path.Length == 0 ? Message : $"{Path}: {Message}";
}
