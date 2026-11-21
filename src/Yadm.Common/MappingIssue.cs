namespace Yadm.Common;

public readonly record struct MappingIssue(string Path, MappingError Error, bool Aborting);