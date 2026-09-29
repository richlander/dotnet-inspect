using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Libraries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PerformanceOracles;

/// <summary>
/// The thinnest valid production host for the registered
/// member-overload-population/default operation: one sealed in-memory
/// ArtifactSetSession holding the assembly bytes, one LibraryReference over it,
/// and one LibraryContentOwner that issues a fresh LibraryOperationLease per
/// operation. No package, cache, network, or CLI host is involved.
/// </summary>
internal sealed class MemberGroupRoute : IDisposable
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 50_000_000,
            maxRetainedTextCharacters: 20_000_000);

    private readonly ArtifactSetSession _session;
    private readonly LibraryContentOwner _owner;
    private readonly byte[] _bytes;

    private MemberGroupRoute(ArtifactSetSession session, LibraryContentOwner owner, byte[] bytes)
    {
        _session = session;
        _owner = owner;
        _bytes = bytes;
    }

    internal static MemberGroupRoute Create(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ManagedMetadataIdentity.Assembly identity;
        using (var pe = new PEReader(new MemoryStream(bytes, writable: false)))
        {
            identity = new(AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()));
        }

        var session = new ArtifactSetSession();
        ArtifactContribution? contribution = null;
        session.AddRequiredAcquisitionAsync(
                (scope, _) =>
                {
                    contribution = scope.Register(
                        new Provenance("membergroup-scorecard/api"),
                        _ => new MemoryStream(bytes, writable: false));
                    return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            [contribution], ArtifactAcquisitionLeases.None));
                })
            .AsTask().GetAwaiter().GetResult();
        if (session.SealAsync().AsTask().GetAwaiter().GetResult()
            is not ArtifactSetPublicationOutcome.Published)
        {
            throw new InvalidOperationException("The scorecard artifact set did not publish.");
        }

        using ArtifactQueryLease queryLease = session.IssueLease(session.CreateQueryAuthorization());
        ArtifactContentReference reference =
            session.GetContentReference(contribution!.Descriptor.Identity, queryLease);
        ArtifactContentLease contentLease = session.IssueContentLease(reference, queryLease);
        LibraryReference library = LibraryReference.CreateDirect(
            new LibraryAssemblyCorrespondence(reference, identity, reference, identity));
        return new(session, new LibraryContentOwner(library, [contentLease]), bytes);
    }

    /// <summary>Request, plan (registered-route resolution), lease, execute, result.</summary>
    internal MemberOverloadPopulationInspectionOutcome Execute(
        MemberGroupScorecardScenario scenario,
        MetadataTypeDefinitionName declaringType,
        MemberGroupTerminal terminal,
        int maximumRows)
    {
        MemberOverloadPopulationInspectionPlan plan = Plan(scenario, declaringType, terminal, maximumRows);
        LibraryOperationLease lease =
            ((LibraryOperationLeaseIssueOutcome.Issued)_owner.IssueOperationLease(_owner.Reference)).Lease;
        return MemberOverloadPopulationInspectionOperation.Execute(
                new(_owner.Reference, plan), lease)
            .Content;
    }

    /// <summary>Only the request and its resolution through the registered route.</summary>
    internal static MemberOverloadPopulationInspectionPlan Plan(
        MemberGroupScorecardScenario scenario,
        MetadataTypeDefinitionName declaringType,
        MemberGroupTerminal terminal,
        int maximumRows) =>
        new(
            new MemberGroupSubject(declaringType, scenario.MethodName),
            new MemberOverloadPopulationRequest(
                terminal is MemberGroupTerminal.Rows ? null : new MemberOverloadCountRequest(),
                terminal is MemberGroupTerminal.Count ? null : new MemberOverloadRowsRequest(maximumRows),
                (MemberOverloadAccessibilityFilter)(int)scenario.Accessibility,
                (MemberOverloadReceiverFilter)(int)scenario.Receiver,
                includeHidden: false),
            s_bounds);

    /// <summary>
    /// The route's metadata work without the route: open a session over the
    /// same bytes, post MethodSemantics, prepare, and execute the fold.
    /// </summary>
    internal MetadataMethodGroupInspectionOutcome FreshSession(
        MemberGroupScorecardScenario scenario,
        MetadataTypeDefinitionName declaringType,
        MemberGroupTerminal terminal,
        int maximumRows)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(new MemoryStream(_bytes, writable: false));
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(s_bounds.MaxMetadataRows));
        using MetadataDeclarationSession declaration = session.CreateDeclarationSession(operation);
        return declaration.InspectMethodGroup(
            declaringType,
            scenario.MethodName,
            startOrdinal: 0,
            maximumRows,
            materializeRows: terminal is not MemberGroupTerminal.Count,
            scenario.Accessibility,
            scenario.Receiver,
            includeHidden: false,
            s_bounds.MaxMembers,
            s_bounds.MaxRetainedTextCharacters);
    }

    public void Dispose()
    {
        _owner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed record Provenance(string Name) : IArtifactProvenance;
}
