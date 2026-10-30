using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

public abstract record MetadataRootAdjacencyInspectionOutcome
{
    private protected MetadataRootAdjacencyInspectionOutcome()
    {
    }

    public sealed record Valid
        : MetadataRootAdjacencyInspectionOutcome;

    public sealed record Invalid(string Detail)
        : MetadataRootAdjacencyInspectionOutcome;
}

internal sealed record MetadataRootAdjacencySnapshot(
    ImmutableArray<AssemblyReferenceIdentity> References,
    ImmutableArray<AssemblyReferenceIdentity> ForwarderTargets,
    string? Failure);

internal static class MetadataRootAdjacencyInspector
{
    internal static MetadataRootAdjacencySnapshot Read(
        MetadataReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        cancellationToken.ThrowIfCancellationRequested();
        var references =
            ImmutableArray.CreateBuilder<AssemblyReferenceIdentity>();
        var seenReferences = new HashSet<AssemblyReferenceIdentity>();
        var referencesByHandle =
            new Dictionary<
                AssemblyReferenceHandle,
                AssemblyReferenceIdentity>();
        var referenceProjection =
            new AssemblyReferenceProjectionCache(reader);
        string? failure = null;
        foreach (AssemblyReferenceHandle handle
                 in reader.AssemblyReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                AssemblyReferenceIdentity reference =
                    AssemblyReferenceIdentity.From(
                        handle,
                        referenceProjection);
                referencesByHandle.Add(handle, reference);
                if (seenReferences.Add(reference))
                    references.Add(reference);
            }
            catch (Exception exception) when (
                exception is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                failure ??=
                    "The selected image has an invalid AssemblyRef row.";
            }
        }

        var forwarderTargets =
            ImmutableArray.CreateBuilder<AssemblyReferenceIdentity>();
        var seenForwarderTargets =
            new HashSet<AssemblyReferenceIdentity>();
        Span<ExportedTypeHandle> rootToLeaf =
            stackalloc ExportedTypeHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        foreach (ExportedTypeHandle handle in reader.ExportedTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!MetadataRelationshipTraversal
                        .TryWalkExportedTypeImplementationChain(
                            reader,
                            handle,
                            rootToLeaf,
                            out _,
                            out EntityHandle terminal,
                            out _))
                {
                    failure ??=
                        "The selected image has an invalid ExportedType relationship.";
                    continue;
                }

                if (terminal.Kind == HandleKind.AssemblyReference)
                {
                    if (!reader.GetExportedType(rootToLeaf[0]).IsForwarder)
                    {
                        failure ??=
                            ApiSurfaceInspectionFailure
                                .UnmarkedAssemblyForwarderDetail;
                        continue;
                    }

                    var targetHandle =
                        (AssemblyReferenceHandle)terminal;
                    if (!referencesByHandle.TryGetValue(
                            targetHandle,
                            out AssemblyReferenceIdentity? target))
                    {
                        target =
                            AssemblyReferenceIdentity.From(
                                targetHandle,
                                referenceProjection);
                        referencesByHandle.Add(
                            targetHandle,
                            target);
                        if (seenReferences.Add(target))
                            references.Add(target);
                    }

                    if (seenForwarderTargets.Add(target))
                        forwarderTargets.Add(target);
                }
                else if (terminal.Kind != HandleKind.AssemblyFile)
                {
                    failure ??=
                        "The selected image has an unsupported ExportedType terminal.";
                }
            }
            catch (Exception exception) when (
                exception is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                failure ??=
                    "The selected image has an invalid ExportedType row.";
            }
        }

        return new(
            references.ToImmutable(),
            forwarderTargets.ToImmutable(),
            failure);
    }
}
