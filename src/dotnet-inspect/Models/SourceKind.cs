namespace DotnetInspector.Models;

/// <summary>
/// Constant values for the Source field, identifying where content was resolved from.
/// </summary>
public static class SourceKind
{
    public const string Platform = "Platform";
    public const string NuGet = "NuGet";
    public const string File = "File";
    public const string Library = "Library";
}
