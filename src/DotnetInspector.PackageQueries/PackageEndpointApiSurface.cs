using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Projects one package endpoint's surface participants into the merged API
/// surface of its Library population, the input of the API-changes and
/// Finding-Transitions comparisons.
/// </summary>
/// <remarks>
/// Participants are merged in ordinal package-asset-path order, each
/// extracted from its own metadata without generic-constraint resolution
/// (the comparisons never read the resolved type-parameter kind), and
/// labeled with its package asset path rather than a file path. A participant
/// that cannot be read is recorded as an inspection failure, never dropped.
/// </remarks>
public static class PackageEndpointApiSurface
{
    public static ValueTask<ArtifactRootResult<ApiSurface>> ExtractAsync(
        PackageEndpointScope scope,
        ApiSurfaceScope surfaceScope,
        string? name,
        string? targetFramework,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(surfaceScope))
            throw new ArgumentOutOfRangeException(nameof(surfaceScope));

        return scope.UseSurfaceAsync(
            (group, participants, token) =>
                ValueTask.FromResult(
                    Merge(group, participants, surfaceScope, name, targetFramework, log, token)),
            cancellationToken);
    }

    static ApiSurface Merge(
        AssemblyContextGroup group,
        ImmutableArray<PackageEndpointBoundParticipant> participants,
        ApiSurfaceScope surfaceScope,
        string? name,
        string? targetFramework,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var merged = new ApiSurface
        {
            Name = name,
            Tfm = targetFramework,
        };
        foreach (PackageEndpointBoundParticipant participant in participants
            .OrderBy(participant => participant.Descriptor.Asset.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = participant.Descriptor.Provenance.AssetPath;
            AssemblyContextEntry<AssemblyApiSurface> entry =
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    participant.Participant,
                    surfaceScope);
            switch (entry)
            {
                case AssemblyContextEntry<AssemblyApiSurface>.Available available:
                    ApiSurfacePopulation.Merge(merged, available.Value.Surface, source, log);
                    break;
                case AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected:
                    merged.InspectionFailures.Add(
                        new ApiSurfaceInspectionFailure(
                            "extract API surface",
                            0,
                            MetadataTypeNameFailureMechanism.Metadata,
                            rejected.Failure.Kind.ToString(),
                            rejected.Failure.Detail,
                            participant.Participant.Assembly.Identity)
                        {
                            SourceAssemblyPath = source,
                        });
                    log?.Invoke($"  ! {Path.GetFileName(source)}: {rejected.Failure.Detail}");
                    break;
                case AssemblyContextEntry<AssemblyApiSurface>.Failed failed:
                    merged.InspectionFailures.Add(
                        new ApiSurfaceInspectionFailure(
                            "extract API surface",
                            0,
                            MetadataTypeNameFailureMechanism.Metadata,
                            failed.Error.GetType().Name,
                            failed.Error.Message,
                            participant.Participant.Assembly.Identity)
                        {
                            SourceAssemblyPath = source,
                        });
                    log?.Invoke($"  ! {Path.GetFileName(source)}: {failed.Error.Message}");
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly context participant outcome.");
            }
        }

        ApiSurfacePopulation.Complete(merged);
        return merged;
    }
}
