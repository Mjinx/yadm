namespace Yadm;

/// <summary>Represents a member or indexed item in a mapping error path.</summary>
internal readonly struct MappingPathSegment
{
    private readonly string? _member;
    private readonly int? _index;

    private MappingPathSegment(string? member, int? index) => (_member, _index) = (member, index);

    internal static MappingPathSegment Member(string? member) => new(member, null);
    internal static MappingPathSegment Item(string? member, int index) => new(member, index);

    internal string AppendTo(string path)
    {
        if (_member is not null)
            path = path.Length == 0 || _member.StartsWith('[') ? path + _member : $"{path}.{_member}";

        return _index is null ? path : $"{path}[{_index}]";
    }
}
