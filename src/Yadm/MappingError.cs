namespace Yadm;

/// <summary>
/// One problem found while mapping: where it is (<see cref="Path"/>), what kind of problem it is
/// (<see cref="Code"/>), and a readable explanation (<see cref="Message"/>).
/// </summary>
/// <param name="Path">
/// Where the problem is in the source, such as <c>Lines[2].Quantity</c>. Empty for the source itself.
/// </param>
/// <param name="Code">A stable, machine-readable kind of problem, such as <see cref="MappingErrorCodes.Required"/>.</param>
/// <param name="Message">A readable description of the problem.</param>
/// <param name="AttemptedValue">
/// The source value that could not be mapped, if there was one. It is retained for diagnostics; consumers that expose
/// errors should usually present only path, code and message to avoid logging submitted values.
/// </param>
public sealed record MappingError(string Path, string Code, string Message, object? AttemptedValue = null)
{
    /// <summary>Where the problem is in the source. Empty for the source itself.</summary>
    /// <exception cref="ArgumentNullException">The constructed or updated value is null.</exception>
    public string Path
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Path));
            field = value;
        }
    } = Path ?? throw new ArgumentNullException(nameof(Path));

    /// <summary>A stable, machine-readable kind of problem.</summary>
    /// <exception cref="ArgumentNullException">The constructed or updated value is null.</exception>
    public string Code
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Code));
            field = value;
        }
    } = Code ?? throw new ArgumentNullException(nameof(Code));

    /// <summary>A readable description of the problem.</summary>
    /// <exception cref="ArgumentNullException">The constructed or updated value is null.</exception>
    public string Message
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Message));
            field = value;
        }
    } = Message ?? throw new ArgumentNullException(nameof(Message));

    /// <summary>Returns <c>Path: Message</c>, or only the message when the path is empty.</summary>
    public override string ToString() => Path.Length == 0 ? Message : $"{Path}: {Message}";
}
