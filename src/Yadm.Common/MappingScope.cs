namespace Yadm.Common;

public sealed class MappingScope(FailurePolicy policy)
{
    private List<MappingIssue>? _issues;                 // allocated only on first error
    private (string Name, int Index)[] _path = new (string, int)[8];
    private int _depth;

    public bool IsAborted { get; private set; }

    // Path tracking: a struct frame, so no allocation, and it works across awaits.
    public PathFrame Enter(string name, int index = -1)
    {
        if (_depth == _path.Length) Array.Resize(ref _path, _depth * 2);
        _path[_depth++] = (name, index);
        return new PathFrame(this);
    }
    public readonly struct PathFrame(MappingScope s) : IDisposable
    { public void Dispose() => s._depth--; }

    // Policy-governed: aborts under FailFast, records and continues under Continue.
    public void Fail(MappingError e, string? field = null) => Record(e, field, policy == FailurePolicy.FailFast);
    // Mapping infeasible regardless of policy.
    public void Abort(MappingError e, string? field = null) => Record(e, field, true);

    private void Record(MappingError e, string? field, bool aborting)
    {
        if (IsAborted) return;                           // FailFast: exactly one error
        (_issues ??= []).Add(new(BuildPath(field), e, aborting));
        IsAborted |= aborting;
    }

    // ── Cheap-check helpers. Field names come from CallerArgumentExpression at compile time. ──
    public T? Require<T>(T? v, [CallerArgumentExpression(nameof(v))] string expr = "") where T : class
    { if (v is null) Fail(new AbsentValue(), expr); return v; }

    public T? Require<T>(T? v, [CallerArgumentExpression(nameof(v))] string expr = "") where T : struct
    { if (v is null) Fail(new AbsentValue(), expr); return v; }

    public string? RequireText(string? v, [CallerArgumentExpression(nameof(v))] string expr = "")
    { if (string.IsNullOrWhiteSpace(v)) { Fail(new AbsentValue(), expr); return null; } return v; }

    public T? ParseRequired<T>(string? raw, [CallerArgumentExpression(nameof(raw))] string expr = "")
        where T : struct, ISpanParsable<T>
    {
        if (string.IsNullOrWhiteSpace(raw)) { Fail(new AbsentValue(), expr); return null; }
        if (T.TryParse(raw, CultureInfo.InvariantCulture, out var v)) return v;
        Fail(new MalformedValue(raw, typeof(T).Name), expr); return null;
    }

    public T? ParseOptional<T>(string? raw, [CallerArgumentExpression(nameof(raw))] string expr = "")
        where T : struct, ISpanParsable<T>
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;       // optional + absent is not an error
        if (T.TryParse(raw, CultureInfo.InvariantCulture, out var v)) return v;
        Fail(new MalformedValue(raw, typeof(T).Name), expr); return null;
    }

    internal MappingResult<T> Complete<T>(T? target)
        => new(target, (IReadOnlyList<MappingIssue>?)_issues ?? [], IsAborted);

    private string BuildPath(string? field)
    {
        var sb = new StringBuilder();                          // error path only
        for (var i = 0; i < _depth; i++)
        {
            if (sb.Length > 0) sb.Append('.');
            sb.Append(_path[i].Name);
            if (_path[i].Index >= 0) sb.Append('[').Append(_path[i].Index).Append(']');
        }
        if (field is not null)
        {
            if (sb.Length > 0) sb.Append('.');
            sb.Append(field[(field.LastIndexOf('.') + 1)..]);  // "src.Email" -> "Email"
        }
        return sb.ToString();
    }
}