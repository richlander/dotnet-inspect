using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Queries;

/// <summary>
/// A source-only API selector and two literal Package endpoints in caller order.
/// Selection never interprets a source overload ordinal at the destination.
/// </summary>
public sealed class ApiCoordinateMatchRequest
{
    public ApiCoordinateMatchRequest(
        string packageId,
        string sourceVersion,
        string destinationVersion,
        string type,
        string? member = null,
        string? targetFramework = null,
        string? library = null,
        bool includeAll = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (!NuGetVersion.TryParse(sourceVersion, out NuGetVersion? before)
            || !NuGetVersion.TryParse(destinationVersion, out NuGetVersion? after))
        {
            throw new ArgumentException(
                "API correspondence requires two literal Package versions.");
        }
        if (TypeMatcher.IsTypeGlobPattern(type))
            throw new ArgumentException("API correspondence requires one Type, not a glob.", nameof(type));
        if (member is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(member);
        if (library is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(library);
        if (targetFramework is not null
            && (string.IsNullOrWhiteSpace(targetFramework)
                || targetFramework.IndexOfAny([',', '*', '?']) >= 0))
        {
            throw new ArgumentException(
                "API correspondence accepts one API target framework.", nameof(targetFramework));
        }

        PackageId = packageId;
        SourceVersion = before.ToNormalizedString();
        DestinationVersion = after.ToNormalizedString();
        Type = type;
        Member = member;
        TargetFramework = targetFramework;
        Library = library;
        IncludeAll = includeAll;
    }

    public string PackageId { get; }
    public string SourceVersion { get; }
    public string DestinationVersion { get; }
    public string Type { get; }
    public string? Member { get; }
    public string? TargetFramework { get; }
    public string? Library { get; }
    public bool IncludeAll { get; }
}
