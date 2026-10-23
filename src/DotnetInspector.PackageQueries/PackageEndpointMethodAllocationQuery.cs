using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

public abstract record PackageEndpointMethodAllocationResult
{
    private PackageEndpointMethodAllocationResult()
    {
    }

    public sealed record Analyzed(
        AssemblyContextEntry<AssemblyMethodAllocationAnalysis> Entry)
        : PackageEndpointMethodAllocationResult;

    public sealed record Fallback(string Reason)
        : PackageEndpointMethodAllocationResult;
}

/// <summary>
/// Resolves one surface MethodDef to its paired package implementation and
/// runs exact allocation analysis only when that MethodDef has managed IL.
/// </summary>
public static class PackageEndpointMethodAllocationQuery
{
    public static async Task<PackageEndpointMethodAllocationResult>
        ExecuteAsync(
        PackageEndpointScope scope,
        string surfaceAssetPath,
        int surfaceMethodToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceAssetPath);
        cancellationToken.ThrowIfCancellationRequested();

        ArtifactRootResult<PackageEndpointMethodAllocationResult> result =
            await scope.UsePackageAssemblyRolesAsync(
                    (workspace, root, realization, token) =>
                    {
                        PackageAssemblyRoleParticipant[] matches =
                        [
                            .. realization.SurfaceParticipants.Where(
                                participant =>
                                    participant.Asset.Path.ToString().Equals(
                                        surfaceAssetPath,
                                        StringComparison.Ordinal)),
                        ];
                        if (matches.Length != 1)
                        {
                            return ValueTask.FromResult<
                                PackageEndpointMethodAllocationResult>(
                                new PackageEndpointMethodAllocationResult
                                    .Fallback(
                                        "The selected API declaration does "
                                            + "not identify one package "
                                            + "surface participant."));
                        }

                        PackageAssemblyRoleParticipant surface = matches[0];
                        PackageAssemblyRoleParticipant? implementation =
                            realization.ImplementationParticipant(surface);
                        if (implementation is null)
                        {
                            return ValueTask.FromResult<
                                PackageEndpointMethodAllocationResult>(
                                new PackageEndpointMethodAllocationResult
                                    .Fallback(
                                        "The selected API declaration has no "
                                            + "paired implementation "
                                            + "participant."));
                        }

                        AssemblyContextMethodCorrespondenceResult
                            correspondence =
                                AssemblyContextMethodCorrespondenceQuery.Execute(
                                    realization.SurfaceGroup,
                                    surface.Participant,
                                    surfaceMethodToken,
                                    realization.ImplementationGroup!,
                                    implementation.Participant,
                                    token);
                        if (correspondence is not
                            AssemblyContextMethodCorrespondenceResult.Exact
                                exact)
                        {
                            string reason = correspondence switch
                            {
                                AssemblyContextMethodCorrespondenceResult
                                    .Bodyless =>
                                        "The exact implementation MethodDef "
                                            + "has no managed IL body.",
                                AssemblyContextMethodCorrespondenceResult
                                    .Unavailable unavailable =>
                                        unavailable.Reason,
                                _ => throw new InvalidOperationException(
                                    "Unknown MethodDef correspondence outcome."),
                            };
                            return ValueTask.FromResult<
                                PackageEndpointMethodAllocationResult>(
                                new PackageEndpointMethodAllocationResult
                                    .Fallback(reason));
                        }

                        AssemblyContextEntry<AssemblyMethodAllocationAnalysis>
                            entry =
                                AssemblyContextMethodAllocationQuery
                                    .ExecuteParticipant(
                                        realization.ImplementationGroup!,
                                        implementation.Participant,
                                        exact.MethodToken,
                                        token);
                        return ValueTask.FromResult<
                            PackageEndpointMethodAllocationResult>(
                            new PackageEndpointMethodAllocationResult
                                .Analyzed(entry));
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        return result switch
        {
            ArtifactRootResult<PackageEndpointMethodAllocationResult>.Available
                available =>
                    available.Value,
            ArtifactRootResult<PackageEndpointMethodAllocationResult>.Rejected
                rejected =>
                    new PackageEndpointMethodAllocationResult.Fallback(
                        $"The package endpoint rejected exact allocation "
                            + $"analysis: {rejected.Failure}."),
            _ => throw new InvalidOperationException(
                "Unknown package endpoint allocation-query outcome."),
        };
    }
}
