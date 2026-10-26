using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// The typed outcome of reading one exact Package manifest from retained
/// content.
/// </summary>
public abstract record PackageManifestInspectionResult
{
    private PackageManifestInspectionResult()
    {
    }

    public sealed record Available(
        PackageManifestFacts Value) :
        PackageManifestInspectionResult;

    public sealed record NoManifest :
        PackageManifestInspectionResult;

    public sealed record Failed(
        Exception Error,
        PackageManifestFailure? ManifestFailure = null) :
        PackageManifestInspectionResult;
}

/// <summary>
/// Query facade over package-owned bounded manifest-fact projection.
/// </summary>
public static class PackageManifestFactsQuery
{
    public const int MaxManifestBytes =
        PackageManifestFactsProjection.MaxManifestBytes;
    public const int MaxManifestCharacters =
        PackageManifestFactsProjection.MaxManifestCharacters;
    public const int MaxScalarCharacters =
        PackageManifestFactsProjection.MaxScalarCharacters;
    public const int MaxPackageTypes =
        PackageManifestFactsProjection.MaxPackageTypes;
    public const int MaxDependencyGroups =
        PackageManifestFactsProjection.MaxDependencyGroups;
    public const int MaxDependencies =
        PackageManifestFactsProjection.MaxDependencies;
    public const int MaxFrameworkReferenceGroups =
        PackageManifestFactsProjection.MaxFrameworkReferenceGroups;
    public const int MaxFrameworkReferences =
        PackageManifestFactsProjection.MaxFrameworkReferences;

    public static InspectionQuery<PackageManifestFactsResult> Definition { get; } =
        new("Package manifest facts", InspectionCost.NetworkFree);

    public static PackageManifestFactsResult Execute(
        ReadOnlyMemory<byte> manifestBytes,
        PackageSourceCoordinate expectedCoordinate) =>
        PackageManifestFactsProjection.Execute(
            manifestBytes,
            expectedCoordinate);

    public static PackageManifestFactsResult ExecuteWithSectionFailures(
        ReadOnlyMemory<byte> manifestBytes,
        PackageSourceCoordinate expectedCoordinate) =>
        PackageManifestFactsProjection.ExecuteWithSectionFailures(
            manifestBytes,
            expectedCoordinate);

    public static PackageManifestFactsResult ExecuteSelfAttested(
        ReadOnlyMemory<byte> manifestBytes) =>
        PackageManifestFactsProjection.ExecuteSelfAttested(manifestBytes);

    public static async Task<PackageManifestInspectionResult>
        ExecuteInspectionAsync(
            PackageInspectionInput input,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PackageId is null || input.PackageVersion is null)
        {
            return new PackageManifestInspectionResult.Failed(
                new InvalidOperationException(
                    "Package manifest inspection requires an exact package identity."));
        }

        try
        {
            string? manifestPath =
                PackageManifestContent.FindRootManifest(input.Content);
            if (manifestPath is null)
                return new PackageManifestInspectionResult.NoManifest();

            if (!input.Content.TryOpenEntry(
                    manifestPath,
                    MaxManifestBytes,
                    out Stream? manifestStream))
            {
                return new PackageManifestInspectionResult.Failed(
                    new InvalidDataException(
                        "The selected package manifest is no longer available."));
            }

            byte[] manifestBytes;
            using (manifestStream)
            {
                manifestBytes = await BoundedContentReader.ReadAllBytesAsync(
                        manifestStream,
                        MaxManifestBytes,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            PackageManifestFactsResult facts = ExecuteWithSectionFailures(
                manifestBytes,
                PackageSourceCoordinate.Create(
                    input.PackageId,
                    input.PackageVersion));
            return facts switch
            {
                PackageManifestFactsResult.Available available =>
                    new PackageManifestInspectionResult.Available(
                        available.Value),
                PackageManifestFactsResult.Failed failed =>
                    new PackageManifestInspectionResult.Failed(
                        new InvalidDataException(failed.Failure.Message),
                        failed.Failure),
                _ => throw new InvalidOperationException(
                    "Unknown package manifest facts result."),
            };
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or NuspecParseException)
        {
            return new PackageManifestInspectionResult.Failed(ex);
        }
    }
}
