using System.Runtime.CompilerServices;

namespace Yadm;

/// <summary>
/// The state of one mapping: where in the source the mapper is, and which errors were found. It is
/// passed to every <see cref="IMapper{TSource, TTarget}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The mapping entry points in <see cref="Mapper"/> create the root context. Nested objects and collection
/// items get child contexts, so every error carries its full path, such as <c>Lines[2].Sku</c>. An error in a
/// child also counts as an error of its parents, which is what <see cref="HasErrors"/> reports.
/// </para>
/// <para>The helpers that read values (<c>Required</c>, <c>Parse</c>, <c>Map</c>, ...) are extension methods.
/// Custom helpers can be written the same way, using only the public members of this class. Built-in helpers stop
/// doing their own work after a fail-fast error, but caller argument evaluation and arbitrary mapper code are outside
/// that boundary.</para>
/// <para>The context is an <see cref="IServiceProvider"/>. Supplying one to the
/// mapping entry points is optional; without it <see cref="GetService"/> returns <see langword="null"/>.</para>
/// <para>A context is not thread-safe; map items one at a time.</para>
/// </remarks>
public sealed class MappingContext : IServiceProvider
{
    private readonly MappingRun _run;
    private readonly MappingContext? _parent;
    private readonly MappingPathSegment _segment;
    private int _activeErrorCount;
    private bool _skipped;

    internal MappingContext(MappingOptions? options, IServiceProvider? serviceProvider = null)
    {
        _run = new MappingRun(options, serviceProvider);
    }

    private MappingContext(MappingContext parent, MappingPathSegment segment)
    {
        _run = parent._run;
        _parent = parent;
        _segment = segment;
    }

    /// <summary>The options this mapping runs with.</summary>
    public MappingOptions Options => _run.Options;

    /// <summary>Where this context is in the source, such as <c>Lines[2]</c>. Empty for the root.</summary>
    public string Path => field ??= BuildPath();

    /// <summary>
    /// True when an error was reported in this context or in a context nested in it (except in items that were
    /// skipped), or when the mapping has stopped. Check it before using the values you read, before I/O, and
    /// before calling a constructor that validates its arguments.
    /// </summary>
    public bool HasErrors => _activeErrorCount > 0 || IsStopped;

    /// <summary>
    /// True when <see cref="MappingOptions.StopOnFirstError"/> is set and an error was reported. Nothing else is
    /// recorded, and built-in helpers and collection mappings do no further work.
    /// </summary>
    public bool IsStopped => _run.IsStopped;

    /// <summary>
    /// Initializes the mapping with a service provider that <see cref="GetService"/> uses. The provider is shared by this context and every
    /// context nested in it, whichever of them it is called on.
    /// </summary>
    /// <param name="serviceProvider">Returns the service for a type, or <see langword="null"/> when it has none.</param>
    public void InitializeServiceProvider(Func<Type, object?> serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _run.ServiceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets a service, so a source that maps itself (<see cref="IMappableObject{TTarget}"/>,
    /// <see cref="IAsyncMappableObject{TTarget}"/>) can reach its dependencies. See
    /// <see cref="IServiceProvider.GetService(Type)"/>.
    /// </summary>
    /// <param name="serviceType">The type of the service needed.</param>
    /// <returns>The service, or <see langword="null"/> when no provider was supplied or it has no such service.</returns>
    public object? GetService(Type serviceType) => _run.ServiceProvider?.Invoke(serviceType);

    /// <summary>Creates a child context, so errors reported to it are located under <paramref name="member"/>.</summary>
    /// <param name="member">The member name, such as <c>ShipTo</c>.</param>
    public MappingContext For(string member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        return Child(member);
    }

    /// <summary>Creates a child context for one collection item, located at <c>member[index]</c>.</summary>
    /// <param name="member">The collection's member name, such as <c>Lines</c>.</param>
    /// <param name="index">The item's position in the source collection.</param>
    public MappingContext For(string member, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return Child(member, index);
    }

    /// <summary>
    /// Creates a child context for an item of the collection this context describes, located at <c>[index]</c>:
    /// for example the rows of a file mapped as a whole, or a collection already located with <c>For("Lines")</c>.
    /// </summary>
    /// <param name="index">The item's position in the source collection.</param>
    public MappingContext For(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return Child(null, index);
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
            context._activeErrorCount -= item._activeErrorCount;
            if (context._skipped)
                break;
        }
    }

    internal int ErrorsFound => _run.ErrorsFound;

    /// <summary>Errors reported in this context and the contexts nested in it, including skipped items.</summary>
    internal int ErrorsReportedHere { get; private set; }

    internal MappingContext Child(string? member) => new(this, MappingPathSegment.Member(member));

    internal MappingContext Child(string? member, int index) => new(this, MappingPathSegment.Item(member, index));

    /// <summary>Turns the mapping's outcome into a result. Called on the root context.</summary>
    internal MappingResult<T> Complete<T>(T? value, object producer) => _run.Complete(value, producer, HasErrors);

    private void Record(string? member, string code, string message, object? attemptedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (IsStopped)
            return;

        _run.Record(this, member, code, message, attemptedValue);

        var counted = true;
        for (var context = this; context is not null; context = context._parent)
        {
            context.ErrorsReportedHere++;
            if (counted)
                context._activeErrorCount++;
            if (context._skipped)
                counted = false;
        }
    }

    internal string ErrorPath(string? member) => MappingPathSegment.Member(member).AppendTo(Path);

    private string BuildPath() => _parent is null ? "" : _segment.AppendTo(_parent.Path);
}
