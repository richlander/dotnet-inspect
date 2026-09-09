using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.QueriesConsumer;

public sealed record PackageRootInventory(
    bool HasAssemblyContexts,
    bool SharesGroup,
    bool HasImplementationGroup,
    int SurfaceParticipantCount,
    int ImplementationParticipantCount,
    AssemblyBindingPolicyVersion? SurfacePolicy,
    AssemblyContextResult<AssemblyTypeInventory>? Surface,
    AssemblyContextResult<AssemblyTypeInventory>? Implementation);

public static class PackageRootQueryConsumer
{
    public static ValueTask<ArtifactRootResult<PackageRootInventory>> QueryAsync(
        InspectionWorkspace workspace,
        PackageArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        AssemblyBindingPolicyVersion? expectedPolicy = null,
        CancellationToken cancellationToken = default) =>
        workspace.ExecutePackageRootQueryAsync(
            correspondence,
            generation,
            static (realization, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new PackageRootInventory(
                    realization.HasAssemblyContexts,
                    realization.SharesGroup,
                    realization.ImplementationGroup is not null,
                    realization.SurfaceParticipants.Length,
                    realization.ImplementationParticipants.Length,
                    realization.HasAssemblyContexts
                        ? realization.SurfaceGroup.BindingPolicyVersion
                        : null,
                    realization.HasAssemblyContexts
                        ? AssemblyContextTypeInventoryQuery.Execute(realization.SurfaceGroup)
                        : null,
                    realization.ImplementationGroup is { } implementation
                        ? AssemblyContextTypeInventoryQuery.Execute(implementation)
                        : null));
            },
            expectedPolicy,
            cancellationToken);
}
