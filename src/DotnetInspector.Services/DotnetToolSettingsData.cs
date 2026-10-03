namespace DotnetInspector.Services;

/// <summary>
/// A RID-specific payload package referenced by a <c>DotnetToolSettings.xml</c> v2 manifest.
/// </summary>
public sealed record ToolRidPackage(string RuntimeIdentifier, string PackageId);

public sealed record DotnetToolCommand(
    string Name,
    string? EntryPoint,
    string? Runner);

/// <summary>
/// Parsed contents of a NuGet tool package's <c>DotnetToolSettings.xml</c> manifest.
/// </summary>
public sealed record DotnetToolSettingsData(
    string? Version,
    string? ToolFormat,
    bool IsRidSpecificPointerPackage,
    IReadOnlyList<string>? Commands,
    IReadOnlyList<ToolRidPackage>? RuntimeIdentifierPackages)
{
    public IReadOnlyList<DotnetToolCommand>? CommandEntries { get; init; }

    public bool IsEntryPoint(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        string assetFileName = FileName(assetPath);
        return CommandEntries?.Any(command =>
            command.EntryPoint is { } entryPoint
            && string.Equals(
                FileName(entryPoint),
                assetFileName,
                StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string FileName(string path) =>
        Path.GetFileName(path.Replace('\\', '/'));
}
