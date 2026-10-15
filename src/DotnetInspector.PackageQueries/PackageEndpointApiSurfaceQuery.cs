using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One package endpoint's merged API surface: every surface participant's
/// projected surface, in selection order, as one surface
/// (docs/design/package-endpoint-scope.md, adoption step 4 part 2). The
/// API views that read more than one Library, a member target, or the
/// analysis set compare two of these.
/// </summary>
/// <remarks>
/// Each participant is projected within the scope's surface group, whose
/// binding universe is the package's own surface. Generic constraints are
/// not bound against installed runtimes or local package caches, so the
/// merged surface carries no row whose presence depends on the machine.
/// </remarks>
public static class PackageEndpointApiSurfaceQuery
{
    public static async Task<ApiSurface> ExecuteAsync(
        PackageEndpointScope scope,
        string name,
        ApiSurfaceScope apiScope,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Enum.IsDefined(apiScope))
            throw new ArgumentOutOfRangeException(nameof(apiScope));

        ArtifactRootResult<ApiSurface> result = await scope.UseSurfaceAsync(
                (group, participants, token) =>
                {
                    var merged = new ApiSurface
                    {
                        Name = name,
                        Tfm = scope.TargetFramework,
                    };
                    foreach (PackageEndpointBoundParticipant participant in participants)
                    {
                        token.ThrowIfCancellationRequested();
                        Merge(
                            merged,
                            participant.Descriptor.Asset,
                            AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                                group,
                                participant.Participant,
                                apiScope),
                            log);
                    }

                    merged.Types = [.. merged.Types.OrderBy(static type => type.FullName)];
                    return ValueTask.FromResult(merged);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result switch
        {
            ArtifactRootResult<ApiSurface>.Available available => available.Value,
            ArtifactRootResult<ApiSurface>.Rejected rejected =>
                throw new InvalidOperationException(
                    $"The package endpoint {name} rejected its API surface query: {rejected.Failure}"),
            _ => throw new InvalidOperationException("Unknown package Root query outcome."),
        };
    }

    static void Merge(
        ApiSurface merged,
        PackageCompileAsset asset,
        AssemblyContextEntry<AssemblyApiSurface> entry,
        Action<string>? log)
    {
        switch (entry)
        {
            case AssemblyContextEntry<AssemblyApiSurface>.Available available:
                ApiSurface surface = available.Value.Surface;
                surface.SetInspectionSourceAssemblyPath(asset.Path);
                log?.Invoke(
                    $"  + {asset.AssemblySimpleName}: "
                        + $"{surface.PublicTypeCount} types");
                merged.MergeAssemblySurface(surface);
                return;
            case AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected:
                AddFailure(
                    merged,
                    asset,
                    "acquire API surface",
                    rejected.Failure.Kind.ToString(),
                    rejected.Failure.Detail,
                    log);
                return;
            case AssemblyContextEntry<AssemblyApiSurface>.Failed failed:
                AddFailure(
                    merged,
                    asset,
                    "extract API surface",
                    failed.Error.GetType().Name,
                    failed.Error.Message,
                    log);
                return;
            default:
                throw new InvalidOperationException("Unknown assembly-context entry.");
        }
    }

    static void AddFailure(
        ApiSurface merged,
        PackageCompileAsset asset,
        string operation,
        string kind,
        string detail,
        Action<string>? log)
    {
        merged.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                operation,
                0,
                MetadataTypeNameFailureMechanism.Metadata,
                kind,
                detail)
            {
                SourceAssemblyPath = asset.Path,
            });
        log?.Invoke($"  ! {asset.AssemblyName}: {detail}");
    }
}
