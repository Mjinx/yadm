using System.Runtime.CompilerServices;

namespace Yadm.Common;

/// <summary>
/// The state of one mapping: where in the source the mapper is, and which errors were found. It plays the
/// role <c>ValidationContext</c> plays for validation, and is passed to every <see cref="IMapper{TSource, TTarget}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The mapping entry points in <see cref="Mapper"/> create the root context. Nested objects and collection
/// items get child contexts, so every error carries its full path, such as <c>Lines[2].Sku</c>. An error in a
/// child also counts as an error of its parents, which is what <see cref="HasErrors"/> reports.
/// </para>
/// <para>The helpers that read values (<c>Required</c>, <c>Parse</c>, <c>Map</c>, ...) are extension methods.
/// Custom helpers can be written the same way, using only the public members of this class.</para>
/// <para>A context is not thread-safe; map items one at a time.</para>
/// </remarks>
public sealed class MappingContext
{
    private readonly MappingContext _root;
    private readonly MappingContext? _parent;
    private readonly List<MappingError>? _errors;
    private readonly string? _member;
    private readonly int _index;
    private int _errorCount;
    private int _errorsFound;
    private bool _skipped;

    internal MappingContext(MappingOptions? options)
    {
        Options = options ?? MappingOptions.Default;
        _root = this;
        _errors = [];
        _index = -1;
    }

    private MappingContext(MappingContext parent, string? member, int index)
    {
        Options = parent.Options;
        _root = parent._root;
        _parent = parent;
        _member = member;
        _index = index;
    }

    /// <summary>The options this mapping runs with.</summary>
    public MappingOptions Options { get; }

    /// <summary>Where this context is in the source, such as <c>Lines[2]</c>. Empty for the root.</summary>
    public string Path => field ??= BuildPath();

    /// <summary>
    /// True when an error was reported in this context or in a context nested in it (except in items that were
    /// skipped), or when the mapping has stopped. Check it before using the values you read, before I/O, and
    /// before calling a constructor that validates its arguments.
    /// </summary>
    public bool HasErrors => _errorCount > 0 || IsStopped;

    /// <summary>
    /// True when <see cref="MappingOptions.StopOnFirstError"/> is set and an error was reported. Nothing else is
    /// recorded, and no further collection items are read.
    /// </summary>
    public bool IsStopped => Options.StopOnFirstError && _root._errorsFound > 0;

    /// <summary>Creates a child context, so errors reported to it are located under <paramref name="member"/>.</summary>
    /// <param name="member">The member name, such as <c>ShipTo</c>.</param>
    public MappingContext For(string member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        return new MappingContext(this, member, -1);
    }

    /// <summary>Creates a child context for one collection item, located at <c>member[index]</c>.</summary>
    /// <param name="member">The collection's member name, such as <c>Lines</c>.</param>
    /// <param name="index">The item's position in the source collection.</param>
    public MappingContext For(string member, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new MappingContext(this, member, index);
    }

    /// <summary>
    /// Creates a child context for an item of the collection this context describes, located at <c>[index]</c>:
    /// for example the rows of a file mapped as a whole, or a collection already located with <c>For("Lines")</c>.
    /// </summary>
    /// <param name="index">The item's position in the source collection.</param>
    public MappingContext For(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new MappingContext(this, null, index);
    }

    /// <summary>
    /// Reports an error about the object this context describes, such as a rule involving several members.
    /// To report a problem with one value, use <see cref="Reject{T}"/>.
    /// </summary>
    /// <param name="code">A stable, machine-readable kind of problem, such as <c>EmptyOrder</c>.</param>
    /// <param name="message">A readable description of the problem.</param>
    public void AddError(string code, string message) => Record(null, code, message, null);

    /// <summary>
    /// Reports that <paramref name="value"/> is not acceptable. The error is located at the member the argument
    /// names: <c>Reject(line.Quantity, ...)</c> reports at <c>Quantity</c> within this context.
    /// </summary>
    /// <param name="value">The rejected value. It is kept as <see cref="MappingError.AttemptedValue"/>.</param>
    /// <param name="code">A stable, machine-readable kind of problem, such as <c>OutOfRange</c>.</param>
    /// <param name="message">A readable description of the problem.</param>
    /// <param name="name">The member name to report, instead of the one taken from the argument.</param>
    /// <param name="expression">Filled in by the compiler; leave it out, or forward it from your own helper.</param>
    public void Reject<T>(T value, string code, string message, string? name = null,
        [CallerArgumentExpression(nameof(value))] string expression = "") =>
        Record(MemberName.From(name, expression), code, message, value);

    /// <summary>
    /// Leaves a failed collection item out: its errors stay in the result, but no longer count as errors of this
    /// context, so the mapping can still produce a partial value. <c>MapEach(..., skipInvalid: true)</c> does this
    /// for you; call it yourself when you map items in your own loop.
    /// </summary>
    /// <param name="item">A context created from this one with <see cref="For(string, int)"/> or <see cref="For(int)"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="item"/> is not a child of this context.</exception>
    public void Skip(MappingContext item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item._parent != this)
            throw new ArgumentException("Only a context created from this one can be skipped.", nameof(item));
        if (item._skipped)
            return;

        item._skipped = true;
        for (var context = this; context is not null; context = context._parent)
        {
            context._errorCount -= item._errorCount;
            if (context._skipped)
                break;
        }
    }

    internal int ErrorsFound => _root._errorsFound;

    /// <summary>Errors reported in this context and the contexts nested in it, including skipped items.</summary>
    internal int ErrorsReportedHere { get; private set; }

    internal MappingContext Child(string? member, int index) => new(this, member, index);

    /// <summary>Turns the mapping's outcome into a result. Called on the root context.</summary>
    internal MappingResult<T> Complete<T>(T? value, object producer)
    {
        if (_errorsFound == 0)
            return MappingResult<T>.Success(MappingRules.Produced(value, producer));

        var errors = _errors!.ToList();
        var leftOut = _errorsFound - errors.Count;
        if (leftOut > 0)
        {
            errors.Add(new MappingError("", MappingErrorCodes.TooManyErrors,
                $"Only the first {errors.Count} errors are listed; {leftOut} more were found."));
        }

        if (!HasErrors && value is null && !typeof(T).IsValueType)
        {
            throw new InvalidOperationException(
                $"{MappingRules.Describe(producer)} returned null although its only errors were in items it skipped. " +
                "Return a value, or report an error that is not skipped.");
        }

        // The value survives only when every error belonged to a skipped item.
        return MappingResult<T>.Failure(HasErrors ? default : value, errors, _errorsFound);
    }

    private void Record(string? member, string code, string message, object? attemptedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (IsStopped)
            return;

        _root._errorsFound++;
        if (_root._errors!.Count < Options.MaxErrors)
            _root._errors.Add(new MappingError(Append(Path, member), code, message, attemptedValue));

        var counted = true;
        for (var context = this; context is not null; context = context._parent)
        {
            context.ErrorsReportedHere++;
            if (counted)
                context._errorCount++;
            if (context._skipped)
                counted = false;
        }
    }

    private string BuildPath()
    {
        if (_parent is null)
            return "";

        var path = Append(_parent.Path, _member);
        return _index < 0 ? path : $"{path}[{_index}]";
    }

    private static string Append(string path, string? member) =>
        member is null ? path
        : path.Length == 0 || member.StartsWith('[') ? path + member
        : $"{path}.{member}";
}
