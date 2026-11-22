namespace Yadm;

/// <summary>A mapping error shaped for API responses, without the attempted input value.</summary>
/// <param name="Field">The path of the field that caused the error.</param>
/// <param name="Code">A stable, machine-readable kind of problem.</param>
/// <param name="Message">A readable description of the problem.</param>
public sealed record MappingErrorDetail(string Field, string Code, string Message);
