using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>The stable failure classification for Package dependency inspection.</summary>
public enum PackageDependencyInspectionFailureReason
{
    PackageIdentityUnavailable,
    ManifestRejected,
    ContentUnavailable,
}

/// <summary>
/// The host-neutral outcome of inspecting one exact Package manifest's
/// dependency declarations.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(PackageDependencyInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(PackageDependencyInspectionOutcome.NoManifest),
    "no-manifest")]
[JsonDerivedType(
    typeof(PackageDependencyInspectionOutcome.Failed),
    "failed")]
public abstract record PackageDependencyInspectionOutcome
{
    private PackageDependencyInspectionOutcome()
    {
    }

    public sealed record Available(
        PackageSourceCoordinate Coordinate,
        PackageManifestIdentityProvenance IdentityProvenance,
        PackageDependencyGroups Dependencies) :
        PackageDependencyInspectionOutcome;

    public sealed record NoManifest(
        string? PackageId,
        string? PackageVersion) :
        PackageDependencyInspectionOutcome;

    public sealed record Failed(
        string? PackageId,
        string? PackageVersion,
        PackageDependencyInspectionFailureReason Reason) :
        PackageDependencyInspectionOutcome;
}

/// <summary>Selects one exact Package dependency inspection.</summary>
public sealed record PackageDependencyInspectionRequest(
    PackageInspectionInput Input,
    string? RequestedTargetFramework = null,
    bool AllowCompatibleFallbackForRequestedTfm = false,
    InspectionShare? Share = null);

/// <summary>
/// Projects one exact Package manifest into a detached inspection envelope.
/// </summary>
public static class PackageDependencyInspectionOperation
{
    private const string SharePath = "package-dependencies/share";

    public static async Task<
        InspectionEnvelope<PackageDependencyInspectionOutcome>> ExecuteAsync(
        PackageDependencyInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Input);

        InspectionShare share = request.Share
            ?? new InspectionShare.NonProjectable(
                SharePath,
                "Package dependency inspection does not yet have a canonical Workspace Share projection.");
        if (request.Input.PackageId is null
            || request.Input.PackageVersion is null)
        {
            return Failed(
                request.Input,
                PackageDependencyInspectionFailureReason
                    .PackageIdentityUnavailable,
                "package-dependencies.identity-unavailable",
                "Package dependency inspection requires an exact package identity.",
                share);
        }

        PackageDependencyGroupsResult result =
            await PackageDependencyGroupsQuery.ExecuteAsync(
                    request.Input,
                    request.RequestedTargetFramework,
                    cancellationToken,
                    request.AllowCompatibleFallbackForRequestedTfm)
                .ConfigureAwait(false);
        return result switch
        {
            PackageDependencyGroupsResult.Available available =>
                new(
                    new PackageDependencyInspectionOutcome.Available(
                        available.Manifest.Coordinate,
                        available.Manifest.IdentityProvenance,
                        available.Value),
                    share),
            PackageDependencyGroupsResult.NoManifest =>
                new(
                    new PackageDependencyInspectionOutcome.NoManifest(
                        request.Input.PackageId,
                        request.Input.PackageVersion),
                    share),
            PackageDependencyGroupsResult.Failed failed =>
                Failed(
                    request.Input,
                    failed.ManifestFailure is null
                        ? PackageDependencyInspectionFailureReason
                            .ContentUnavailable
                        : PackageDependencyInspectionFailureReason
                            .ManifestRejected,
                    failed.ManifestFailure is null
                        ? "package-dependencies.content-unavailable"
                        : "package-dependencies.manifest-rejected",
                    failed.ManifestFailure is null
                        ? "The package dependency manifest could not be read."
                        : "The package dependency manifest was rejected.",
                    share),
            _ => throw new InvalidOperationException(
                "Unknown package dependency-group result."),
        };
    }

    private static InspectionEnvelope<PackageDependencyInspectionOutcome>
        Failed(
            PackageInspectionInput input,
            PackageDependencyInspectionFailureReason reason,
            string code,
            string summary,
            InspectionShare share) =>
        new(
            new PackageDependencyInspectionOutcome.Failed(
                input.PackageId,
                input.PackageVersion,
                reason),
            share,
            [
                new InspectionDiagnostic(
                    code,
                    InspectionDiagnosticSeverity.Error,
                    summary),
            ]);
}
