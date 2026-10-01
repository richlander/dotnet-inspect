using System.Collections.Immutable;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal abstract record WorkspaceBorrowedLibraryTypeResolution
{
    private WorkspaceBorrowedLibraryTypeResolution()
    {
    }

    internal sealed record Resolved(AssemblyReferenceIdentity Assembly)
        : WorkspaceBorrowedLibraryTypeResolution;

    internal sealed record Unavailable
        : WorkspaceBorrowedLibraryTypeResolution;
}

internal sealed class WorkspaceBorrowedLibraryTypeResolver
{
    private readonly WorkspaceDeclarationPopulation _population;
    private readonly IReadOnlyDictionary<
        AssemblyReferenceIdentity,
        WorkspaceDeclarationMember?> _members;
    private readonly Dictionary<
        AssemblyReferenceIdentity,
        LibraryDeclarations?> _declarations =
            new(AssemblyReferenceIdentity.EquivalentComparer);

    internal WorkspaceBorrowedLibraryTypeResolver(
        WorkspaceDeclarationPopulation population,
        IEnumerable<WorkspaceDeclarationMember> members)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(members);
        _population = population;
        var membersByAssembly = new Dictionary<
            AssemblyReferenceIdentity,
            WorkspaceDeclarationMember?>(
                AssemblyReferenceIdentity.EquivalentComparer);
        foreach (WorkspaceDeclarationMember member in members)
        {
            if (!membersByAssembly.TryAdd(
                    member.AssemblyIdentity,
                    member))
            {
                membersByAssembly[member.AssemblyIdentity] = null;
            }
        }
        _members = membersByAssembly;
    }

    internal WorkspaceBorrowedLibraryTypeResolution Resolve(
        AssemblyReferenceIdentity assembly,
        MetadataTypeDefinitionName type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(type);
        cancellationToken.ThrowIfCancellationRequested();
        var visited = new List<AssemblyReferenceIdentity>();
        AssemblyReferenceIdentity current = assembly;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited.Any(candidate =>
                    candidate.IsEquivalentTo(current)))
            {
                return new WorkspaceBorrowedLibraryTypeResolution.Unavailable();
            }
            visited.Add(current);
            LibraryDeclarations? library =
                GetDeclarations(current, cancellationToken);
            if (library is null)
            {
                return new WorkspaceBorrowedLibraryTypeResolution.Unavailable();
            }
            bool definition = library.Definitions.Contains(type);
            bool forwarded = library.Forwarders.TryGetValue(
                type,
                out AssemblyReferenceIdentity? target);
            if (definition == forwarded)
            {
                return new WorkspaceBorrowedLibraryTypeResolution.Unavailable();
            }
            if (definition)
            {
                return new WorkspaceBorrowedLibraryTypeResolution.Resolved(
                    library.Assembly);
            }

            current = target!;
            }
    }

    private LibraryDeclarations? GetDeclarations(
            AssemblyReferenceIdentity assembly,
            CancellationToken cancellationToken)
    {
            if (_declarations.TryGetValue(
                    assembly,
                    out LibraryDeclarations? declarations))
            {
                return declarations;
            }

            declarations =
                _members.TryGetValue(
                    assembly,
                    out WorkspaceDeclarationMember? member)
                    && member is not null
                        ? ReadDeclarations(member, cancellationToken)
                        : null;
            _declarations[assembly] = declarations;
            return declarations;
    }

    private LibraryDeclarations? ReadDeclarations(
        WorkspaceDeclarationMember member,
        CancellationToken cancellationToken)
    {
        WorkspaceDeclarationInventoryOutcome outcome =
            _population.ReadDeclarations(
                member.Occurrence,
                cancellationToken);
        if (outcome
            is not WorkspaceDeclarationInventoryOutcome.LibraryInspected
            {
                Outcome:
                    LibraryTypeDeclarationInventoryInspectionOutcome.Completed
                        completed,
            })
        {
            return null;
        }

        LibraryTypeDeclarationInventoryCorrespondence correspondence =
            completed.Correspondence;
        ImmutableArray<MetadataTypeDefinitionName> forwarderNames =
            correspondence.Inventory.Forwarders;
        if (forwarderNames.IsDefaultOrEmpty)
        {
            return new(
                correspondence.AssemblyIdentity,
                correspondence.Inventory.Definitions.ToImmutableHashSet(),
                ImmutableDictionary<
                    MetadataTypeDefinitionName,
                    AssemblyReferenceIdentity>.Empty);
        }

        WorkspaceDeclarationInventoryOutcome rowsOutcome =
            _population.ReadDeclarations(
                member.Occurrence,
                new(
                    startOrdinal: 0,
                    maximumRows: forwarderNames.Length,
                    includeMemberCount: false,
                    expectedModuleVersionId:
                        correspondence.ModuleVersionId,
                    includeDefinitions: false,
                    includeForwarders: true,
                    definitionKinds: ApiTypeInventoryKinds.None),
                cancellationToken);
        if (rowsOutcome
            is not WorkspaceDeclarationInventoryOutcome.LibraryInspected
            {
                Outcome:
                    LibraryTypeDeclarationInventoryInspectionOutcome.Completed
                    {
                        Correspondence:
                        {
                            Rows:
                                LibraryTypeDeclarationRowsInspectionOutcome.Read
                                    read,
                        },
                    },
            }
            || read.Rows.Length != forwarderNames.Length)
        {
            return null;
        }

        var forwarders = ImmutableDictionary.CreateBuilder<
            MetadataTypeDefinitionName,
            AssemblyReferenceIdentity>();
        foreach (AssemblyTypeDeclarationRow row in read.Rows)
        {
            if (row.Declaration.Kind
                    is not AssemblyTypeDeclarationKind.Forwarder
                || row.Forwarding is null
                || !forwarders.TryAdd(
                    row.Declaration.Name,
                    row.Forwarding.Target))
            {
                return null;
            }
        }
        return new(
            correspondence.AssemblyIdentity,
            correspondence.Inventory.Definitions.ToImmutableHashSet(),
            forwarders.ToImmutable());
    }

    private sealed record LibraryDeclarations(
        AssemblyReferenceIdentity Assembly,
        ImmutableHashSet<MetadataTypeDefinitionName> Definitions,
        ImmutableDictionary<
            MetadataTypeDefinitionName,
            AssemblyReferenceIdentity> Forwarders);
}
