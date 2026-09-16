using System.Collections.Immutable;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal abstract record RetainedAssemblyContextGroup
{
    private protected RetainedAssemblyContextGroup() { }

    internal sealed record Ready(AssemblyContextGroup Group) : RetainedAssemblyContextGroup;
    internal sealed record Rejected(int AssemblyIndex, CandidateOpenFailure Failure)
        : RetainedAssemblyContextGroup;

    internal static RetainedAssemblyContextGroup Create(
        InspectionWorkspace workspace,
        ImmutableArray<ResolvedAssemblyReference> assemblies,
        AssemblyContextGroupOptions options,
        CancellationToken cancellationToken = default)
    {
        long retainedBytes = 0;
        var snapshots = new Dictionary<AssemblyAcquisitionRegistration, AssemblyImageSnapshot>(
            ReferenceEqualityComparer.Instance);
        var leases = new Dictionary<AssemblyAcquisitionRegistration, AssemblyImageReferenceLease>(
            ReferenceEqualityComparer.Instance);
        var retained = ImmutableArray.CreateBuilder<ResolvedAssemblyReference>(assemblies.Length);
        bool transferred = false;
        try
        {
            for (int index = 0; index < assemblies.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ResolvedAssemblyReference assembly = assemblies[index];
                AssemblyImageSnapshotResult result = AssemblyImageSnapshot.Open(
                    assembly,
                    bytes =>
                    {
                        if (bytes > options.MaxRetainedImageBytes - retainedBytes)
                            return false;
                        retainedBytes += bytes;
                        return true;
                    },
                    bytes => retainedBytes -= bytes);
                if (result is AssemblyImageSnapshotResult.Rejected rejected)
                    return new Rejected(index, rejected.Failure);
                AssemblyImageSnapshot snapshot = ((AssemblyImageSnapshotResult.Ready)result).Snapshot;
                snapshots.Add(assembly.Registration, snapshot);
                AssemblyImageReferenceLease lease = snapshot.LeaseAssemblyReference(assembly);
                leases.Add(assembly.Registration, lease);
                retained.Add(lease.Assembly);
            }

            cancellationToken.ThrowIfCancellationRequested();
            IAcquisitionFreeAssemblyBindingPolicy policy =
                SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                    retained.Select(static assembly =>
                        (assembly, (IAcquisitionFreeAssemblyBindingPolicy)
                            NoResolverAssemblyBindingPolicy.Instance)));
            AssemblyContextGroup group = workspace.CreateAssemblyContextGroupWithRetainedImages(
                retained.Select(assembly => new AssemblyContextParticipant(assembly, policy)),
                snapshots, leases, options);
            transferred = true;
            return new Ready(group);
        }
        finally
        {
            if (!transferred)
                foreach (AssemblyImageReferenceLease lease in leases.Values)
                    lease.Dispose();
        }
    }
}
