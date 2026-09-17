using NuGet.Versioning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One source-only Package API selector.</summary>
public sealed class ApiCoordinateSourceSelectionRequest
{
    public ApiCoordinateSourceSelectionRequest(
        string packageId,
        string sourceVersion,
        string type,
        string? member = null,
        string? library = null,
        bool includeAll = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (!NuGetVersion.TryParse(sourceVersion, out NuGetVersion? parsed))
        {
            throw new ArgumentException(
                "API source selection requires one literal Package version.",
                nameof(sourceVersion));
        }
        if (TypeMatcher.IsTypeGlobPattern(type))
        {
            throw new ArgumentException(
                "API source selection requires one Type, not a glob.",
                nameof(type));
        }
        if (member is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(member);
        if (library is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(library);

        PackageId = packageId;
        SourceVersion = parsed.ToNormalizedString();
        Type = type;
        Member = member;
        Library = library;
        IncludeAll = includeAll;
    }

    public string PackageId { get; }
    public string SourceVersion { get; }
    public string Type { get; }
    public string? Member { get; }
    public string? Library { get; }
    public bool IncludeAll { get; }
}
