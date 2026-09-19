using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Package;

public sealed record BrowserWorkspacePackageSetCoordinate(
    string PackageId,
    string? Version,
    string? Framework,
    string? RuntimeIdentifier);

public sealed record BrowserWorkspacePackageSetDescriptor(
    string Id,
    string Title,
    string Summary,
    int Order,
    string SourceKind,
    ImmutableArray<BrowserWorkspacePackageSetCoordinate> Coordinates);

public sealed record BrowserWorkspacePackageSetCatalog(
    int Version,
    ImmutableArray<BrowserWorkspacePackageSetDescriptor> PackageSets);

public sealed record BrowserWorkspacePackageSetRealization(
    string Kind,
    string PackageSetId,
    string Title,
    int RequestedPackageCount,
    int AvailableSlots,
    ImmutableArray<BrowserPackageSurface> Packages);

[JsonSerializable(typeof(BrowserWorkspacePackageSetCatalog))]
[JsonSerializable(typeof(BrowserWorkspacePackageSetRealization))]
internal sealed partial class BrowserPackageJsonContext;
