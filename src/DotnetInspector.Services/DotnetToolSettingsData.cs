namespace DotnetInspector.Services;

/// <summary>
/// A RID-specific payload package referenced by a <c>DotnetToolSettings.xml</c> v2 manifest.
/// </summary>
public sealed record ToolRidPackage(string RuntimeIdentifier, string PackageId);

/// <summary>
/// Parsed contents of a NuGet tool package's <c>DotnetToolSettings.xml</c> manifest.
/// </summary>
public sealed record DotnetToolSettingsData(
    string? Version,
    string? ToolFormat,
    bool IsRidSpecificPointerPackage,
    IReadOnlyList<string>? Commands,
    IReadOnlyList<ToolRidPackage>? RuntimeIdentifierPackages);
