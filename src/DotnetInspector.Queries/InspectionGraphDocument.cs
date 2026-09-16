using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>The identity domain carried by one inspection-graph subject.</summary>
public enum InspectionGraphSubjectKind
{
    Member,
    Type,
    Assembly,
    Package,
}

/// <summary>Owner-issued identity for one member subject.</summary>
public abstract record InspectionGraphMemberIdentity
{
    protected InspectionGraphMemberIdentity()
    {
    }

    public abstract bool IsPortable { get; }

    public sealed record CallGraph : InspectionGraphMemberIdentity
    {
        public CallGraph(
            GraphNodeIdentity identity,
            MemberRef member)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ArgumentNullException.ThrowIfNull(member);
            Identity = identity;
            Member = member;
        }

        public GraphNodeIdentity Identity { get; }
        public MemberRef Member { get; }
        public override bool IsPortable => Identity.IsPortable;

        public bool Equals(CallGraph? other) =>
            other is not null && Identity == other.Identity;

        public override int GetHashCode() => Identity.GetHashCode();
    }

    /// <summary>
    /// One Metadata API member interpreted beside its acquired assembly.
    /// </summary>
    public sealed record AcquiredApi : InspectionGraphMemberIdentity
    {
        public AcquiredApi(
            AssemblyAcquisitionRegistration registration,
            MetadataTypeDefinitionName declaringType,
            MemberAnchor member)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(declaringType);
            ArgumentNullException.ThrowIfNull(member);
            Registration = registration;
            DeclaringType = declaringType;
            Member = member;
        }

        public AssemblyAcquisitionRegistration Registration { get; }
        public MetadataTypeDefinitionName DeclaringType { get; }
        public MemberAnchor Member { get; }
        public override bool IsPortable => false;
    }

    /// <summary>
    /// One Integration candidate member interpreted within its Census
    /// participant.
    /// </summary>
    public sealed record CensusMember : InspectionGraphMemberIdentity
    {
        public CensusMember(IntegrationCandidateSourceIdentity source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Element is not IntegrationCandidateSourceElement.Member)
            {
                throw new ArgumentException(
                    "An Integration member subject requires a member source element.",
                    nameof(source));
            }

            Source = source;
        }

        public IntegrationCandidateSourceIdentity Source { get; }
        public override bool IsPortable => false;
    }
}

/// <summary>Owner-issued identity for one type subject.</summary>
public abstract record InspectionGraphTypeIdentity
{
    protected InspectionGraphTypeIdentity()
    {
    }

    public abstract bool IsPortable { get; }

    public sealed record Structural : InspectionGraphTypeIdentity
    {
        public Structural(TypeRef type)
        {
            ArgumentNullException.ThrowIfNull(type);
            Type = type;
        }

        public TypeRef Type { get; }
        public override bool IsPortable => true;
    }

    /// <summary>
    /// One exact metadata definition interpreted beside its acquired assembly.
    /// </summary>
    public sealed record AcquiredDefinition : InspectionGraphTypeIdentity
    {
        public AcquiredDefinition(
            AssemblyAcquisitionRegistration registration,
            MetadataTypeDefinitionName type)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(type);
            Registration = registration;
            Type = type;
        }

        public AssemblyAcquisitionRegistration Registration { get; }
        public MetadataTypeDefinitionName Type { get; }
        public override bool IsPortable => false;
    }

    /// <summary>
    /// One Integration Census Type interpreted within its exact participant.
    /// </summary>
    public sealed record CensusType : InspectionGraphTypeIdentity
    {
        public CensusType(IntegrationTypeIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public IntegrationTypeIdentity Identity { get; }
        public override bool IsPortable => false;
    }
}

/// <summary>Owner-issued identity for one assembly subject.</summary>
public abstract record InspectionGraphAssemblyIdentity
{
    protected InspectionGraphAssemblyIdentity()
    {
    }

    public abstract bool IsPortable { get; }

    /// <summary>
    /// One assembly participant while its acquisition registration remains
    /// authoritative.
    /// </summary>
    /// <remarks>
    /// <c>InspectionGraphPackageBoundaryTests.PackageGroupsLens_DoesNotCollapseMatchingAssemblyMetadata</c>
    /// gates acquisition-distinct identity.
    /// </remarks>
    public sealed record Acquired : InspectionGraphAssemblyIdentity
    {
        internal Acquired(ResolvedAssemblyReference assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            Registration = assembly.Registration;
            Assembly = assembly.Identity;
            Provenance = assembly.Provenance;
        }

        public AssemblyAcquisitionRegistration Registration { get; }
        public AssemblyReferenceIdentity Assembly { get; }
        public AssemblyResolutionProvenance Provenance { get; }
        public override bool IsPortable => false;
    }

    public sealed record Metadata : InspectionGraphAssemblyIdentity
    {
        public Metadata(AssemblyReferenceIdentity assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            Assembly = assembly;
        }

        public AssemblyReferenceIdentity Assembly { get; }
        public override bool IsPortable => true;
    }

    /// <summary>
    /// One assembly participant addressed by an Integration Census.
    /// </summary>
    public sealed record CensusParticipant :
        InspectionGraphAssemblyIdentity
    {
        public CensusParticipant(
            IntegrationSourceParticipantIdentity participant)
        {
            ArgumentNullException.ThrowIfNull(participant);
            Participant = participant;
        }

        public IntegrationSourceParticipantIdentity Participant { get; }
        public override bool IsPortable => false;
    }
}

/// <summary>Owner-issued identity for one package subject.</summary>
public abstract record InspectionGraphPackageIdentity
{
    protected InspectionGraphPackageIdentity()
    {
    }

    public abstract bool IsPortable { get; }

    public sealed record Realized : InspectionGraphPackageIdentity
    {
        public Realized(RealizedMemberCoordinate.Package package)
        {
            ArgumentNullException.ThrowIfNull(package);
            Package = package;
        }

        public RealizedMemberCoordinate.Package Package { get; }
        public override bool IsPortable => true;
    }
}

/// <summary>
/// An owner-issued semantic identity. Display text is never graph identity.
/// </summary>
public abstract record InspectionGraphSubject
{
    private protected InspectionGraphSubject()
    {
    }

    public abstract InspectionGraphSubjectKind Kind { get; }
    public abstract bool IsPortable { get; }

    public static InspectionGraphSubject ForMember(
        GraphNodeIdentity identity,
        MemberRef member) =>
        ForMember(
            new InspectionGraphMemberIdentity.CallGraph(
                identity,
                member));

    public static InspectionGraphSubject ForMember(
        InspectionGraphMemberIdentity identity) =>
        new MemberSubject(identity);

    public static InspectionGraphSubject ForAcquiredApiMember(
        AssemblyAcquisitionRegistration registration,
        MetadataTypeDefinitionName declaringType,
        MemberAnchor member) =>
        ForMember(
            new InspectionGraphMemberIdentity.AcquiredApi(
                registration,
                declaringType,
                member));

    public static InspectionGraphSubject ForIntegrationMember(
        IntegrationCandidateSourceIdentity source) =>
        ForMember(new InspectionGraphMemberIdentity.CensusMember(source));

    public static InspectionGraphSubject ForType(
        InspectionGraphTypeIdentity identity) =>
        new TypeSubject(identity);

    public static InspectionGraphSubject ForStructuralType(TypeRef type) =>
        ForType(new InspectionGraphTypeIdentity.Structural(type));

    public static InspectionGraphSubject ForAcquiredType(
        AssemblyAcquisitionRegistration registration,
        MetadataTypeDefinitionName type) =>
        ForType(
            new InspectionGraphTypeIdentity.AcquiredDefinition(
                registration,
                type));

    public static InspectionGraphSubject ForIntegrationType(
        IntegrationTypeIdentity identity) =>
        ForType(new InspectionGraphTypeIdentity.CensusType(identity));

    public static InspectionGraphSubject ForAssembly(
        InspectionGraphAssemblyIdentity identity) =>
        new AssemblySubject(identity);

    public static InspectionGraphSubject ForAcquiredAssembly(
        ResolvedAssemblyReference assembly) =>
        ForAssembly(new InspectionGraphAssemblyIdentity.Acquired(assembly));

    public static InspectionGraphSubject ForMetadataAssembly(
        AssemblyReferenceIdentity assembly) =>
        ForAssembly(
            new InspectionGraphAssemblyIdentity.Metadata(assembly));

    public static InspectionGraphSubject ForIntegrationAssembly(
        IntegrationSourceParticipantIdentity participant) =>
        ForAssembly(
            new InspectionGraphAssemblyIdentity.CensusParticipant(
                participant));

    public static InspectionGraphSubject ForPackage(
        InspectionGraphPackageIdentity identity) =>
        new PackageSubject(identity);

    public static InspectionGraphSubject ForRealizedPackage(
        RealizedMemberCoordinate.Package package) =>
        ForPackage(new InspectionGraphPackageIdentity.Realized(package));

    public sealed record MemberSubject : InspectionGraphSubject
    {
        internal MemberSubject(
            InspectionGraphMemberIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public override InspectionGraphSubjectKind Kind =>
            InspectionGraphSubjectKind.Member;
        public override bool IsPortable => Identity.IsPortable;

        public InspectionGraphMemberIdentity Identity { get; }
    }

    public sealed record TypeSubject : InspectionGraphSubject
    {
        internal TypeSubject(InspectionGraphTypeIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public override InspectionGraphSubjectKind Kind =>
            InspectionGraphSubjectKind.Type;
        public override bool IsPortable => Identity.IsPortable;

        public InspectionGraphTypeIdentity Identity { get; }
    }

    public sealed record AssemblySubject : InspectionGraphSubject
    {
        internal AssemblySubject(
            InspectionGraphAssemblyIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public override InspectionGraphSubjectKind Kind =>
            InspectionGraphSubjectKind.Assembly;
        public override bool IsPortable => Identity.IsPortable;

        public InspectionGraphAssemblyIdentity Identity { get; }
    }

    public sealed record PackageSubject : InspectionGraphSubject
    {
        internal PackageSubject(
            InspectionGraphPackageIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public override InspectionGraphSubjectKind Kind =>
            InspectionGraphSubjectKind.Package;
        public override bool IsPortable => Identity.IsPortable;

        public InspectionGraphPackageIdentity Identity { get; }
    }
}

/// <summary>The subsystem that owns one graph contract.</summary>
public enum InspectionGraphOwner
{
    CallGraph,
    Packages,
    Metadata,
    Analysis,
    Research,
    Queries,
}

/// <summary>How a relationship's producer establishes its claim.</summary>
public enum InspectionGraphRelationshipSemantics
{
    Observed,
    Derived,
    Synthetic,
}

/// <summary>The semantically directed endpoint of one occurrence.</summary>
public enum InspectionGraphEndpointRole
{
    Source,
    Target,
}

/// <summary>
/// Defines how an occurrence endpoint supports a selected view endpoint.
/// </summary>
public abstract class InspectionGraphEndpointProjection
{
    protected InspectionGraphEndpointProjection()
    {
    }

    public static InspectionGraphEndpointProjection Exact { get; } =
        new ExactProjection();

    public abstract bool Supports(
        InspectionGraphOccurrence occurrence,
        InspectionGraphEndpointRole role,
        InspectionGraphSubject endpoint);

    private sealed class ExactProjection : InspectionGraphEndpointProjection
    {
        public override bool Supports(
            InspectionGraphOccurrence occurrence,
            InspectionGraphEndpointRole role,
            InspectionGraphSubject endpoint) =>
            role switch
            {
                InspectionGraphEndpointRole.Source =>
                    occurrence.SourceSubject == endpoint,
                InspectionGraphEndpointRole.Target =>
                    occurrence.TargetSubject == endpoint,
                _ => throw new ArgumentOutOfRangeException(nameof(role)),
            };
    }
}

/// <summary>Identity for one producer-owned occurrence evidence shape.</summary>
public sealed class InspectionGraphEvidenceDescriptor
{
    public InspectionGraphEvidenceDescriptor(
        string id,
        InspectionGraphOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        InspectionGraphCollections.RequireDefined(owner, nameof(owner));
        Id = id;
        Owner = owner;
    }

    public string Id { get; }
    public InspectionGraphOwner Owner { get; }
}

/// <summary>
/// Typed producer evidence retained by one graph occurrence.
/// </summary>
public interface IInspectionGraphEvidence
{
    InspectionGraphEvidenceDescriptor Descriptor { get; }
}

/// <summary>Evidence that contributes one graph occurrence.</summary>
public interface IInspectionGraphOccurrenceEvidence
    : IInspectionGraphEvidence
{
}

/// <summary>Typed detail retained by one graph limit or failure.</summary>
public interface IInspectionGraphDiagnosticEvidence
    : IInspectionGraphEvidence
{
}

/// <summary>
/// Projects producer evidence to its relationship-specific identity within one
/// document.
/// </summary>
public abstract class InspectionGraphOccurrenceIdentityProjection
{
    protected InspectionGraphOccurrenceIdentityProjection()
    {
    }

    public static InspectionGraphOccurrenceIdentityProjection
        SyntheticNoOccurrence { get; } =
        new SyntheticNoOccurrenceProjection();

    public abstract object Project(
        InspectionGraphOccurrence occurrence);

    private sealed class SyntheticNoOccurrenceProjection
        : InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            throw new InvalidOperationException(
                "A synthetic relationship without occurrences has no occurrence identity.");
    }
}

/// <summary>
/// The ways a typed seed may enter a directed relationship.
/// </summary>
public enum InspectionGraphSeedAdmissionKind
{
    EdgeEndpoint,
    OccurrenceEndpoint,
    OwnedSubjects,
}

/// <summary>
/// How one seed subject kind enters a relationship in semantic direction.
/// </summary>
public sealed record InspectionGraphSeedAdmission(
    InspectionGraphSubjectKind SubjectKind,
    InspectionGraphSeedAdmissionKind Kind,
    InspectionGraphEndpointRole Role);

/// <summary>
/// The L1 semantic contract for one directed relationship family.
/// </summary>
public sealed class InspectionGraphRelationshipDescriptor
{
    public InspectionGraphRelationshipDescriptor(
        string id,
        InspectionGraphOwner owner,
        InspectionGraphRelationshipSemantics semantics,
        IEnumerable<InspectionGraphSubjectKind> edgeSourceKinds,
        IEnumerable<InspectionGraphSubjectKind> edgeTargetKinds,
        IEnumerable<InspectionGraphSubjectKind> occurrenceSourceKinds,
        IEnumerable<InspectionGraphSubjectKind> occurrenceTargetKinds,
        IEnumerable<InspectionGraphSeedAdmission> seedAdmissions,
        InspectionGraphEndpointProjection endpointProjection,
        InspectionGraphOccurrenceIdentityProjection occurrenceIdentity,
        IEnumerable<InspectionGraphEvidenceDescriptor> evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(endpointProjection);
        ArgumentNullException.ThrowIfNull(occurrenceIdentity);
        InspectionGraphCollections.RequireDefined(owner, nameof(owner));
        InspectionGraphCollections.RequireDefined(
            semantics,
            nameof(semantics));
        Id = id;
        Owner = owner;
        Semantics = semantics;
        EdgeSourceKinds = InspectionGraphCollections.Snapshot(
            edgeSourceKinds,
            nameof(edgeSourceKinds));
        EdgeTargetKinds = InspectionGraphCollections.Snapshot(
            edgeTargetKinds,
            nameof(edgeTargetKinds));
        OccurrenceSourceKinds = InspectionGraphCollections.Snapshot(
            occurrenceSourceKinds,
            nameof(occurrenceSourceKinds));
        OccurrenceTargetKinds = InspectionGraphCollections.Snapshot(
            occurrenceTargetKinds,
            nameof(occurrenceTargetKinds));
        SeedAdmissions = InspectionGraphCollections.Snapshot(
            seedAdmissions,
            nameof(seedAdmissions));
        Evidence = InspectionGraphCollections.Snapshot(
            evidence,
            nameof(evidence));
        InspectionGraphCollections.RequireDefined(
            EdgeSourceKinds,
            nameof(edgeSourceKinds));
        InspectionGraphCollections.RequireDefined(
            EdgeTargetKinds,
            nameof(edgeTargetKinds));
        InspectionGraphCollections.RequireDefined(
            OccurrenceSourceKinds,
            nameof(occurrenceSourceKinds));
        InspectionGraphCollections.RequireDefined(
            OccurrenceTargetKinds,
            nameof(occurrenceTargetKinds));
        if (SeedAdmissions.IsEmpty)
        {
            throw new ArgumentException(
                "At least one seed admission is required.",
                nameof(seedAdmissions));
        }
        foreach (InspectionGraphSeedAdmission admission in SeedAdmissions)
        {
            ArgumentNullException.ThrowIfNull(admission);
            InspectionGraphCollections.RequireDefined(
                admission.SubjectKind,
                nameof(seedAdmissions));
            InspectionGraphCollections.RequireDefined(
                admission.Kind,
                nameof(seedAdmissions));
            InspectionGraphCollections.RequireDefined(
                admission.Role,
                nameof(seedAdmissions));
            ImmutableArray<InspectionGraphSubjectKind>? endpointKinds =
                (admission.Kind, admission.Role) switch
                {
                    (
                        InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                        InspectionGraphEndpointRole.Source) =>
                        EdgeSourceKinds,
                    (
                        InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                        InspectionGraphEndpointRole.Target) =>
                        EdgeTargetKinds,
                    (
                        InspectionGraphSeedAdmissionKind.OccurrenceEndpoint,
                        InspectionGraphEndpointRole.Source) =>
                        OccurrenceSourceKinds,
                    (
                        InspectionGraphSeedAdmissionKind.OccurrenceEndpoint,
                        InspectionGraphEndpointRole.Target) =>
                        OccurrenceTargetKinds,
                    (InspectionGraphSeedAdmissionKind.OwnedSubjects, _) =>
                        null,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(seedAdmissions)),
                };
            if (endpointKinds is { } kinds
                && !kinds.Contains(admission.SubjectKind))
            {
                throw new ArgumentException(
                    "A direct seed admission must use a subject kind admitted by that endpoint.",
                    nameof(seedAdmissions));
            }
            if (admission.Kind
                    == InspectionGraphSeedAdmissionKind.OwnedSubjects
                && !OwnedEndpointKinds(admission.Role).Any(
                    endpointKind => CanStrictlyOwn(
                        admission.SubjectKind,
                        endpointKind)))
            {
                throw new ArgumentException(
                    "An owned-subject seed admission must strictly own a subject kind in that semantic endpoint domain.",
                    nameof(seedAdmissions));
            }
        }
        if (EdgeSourceKinds.IsEmpty)
            throw new ArgumentException("At least one edge source subject kind is required.", nameof(edgeSourceKinds));
        if (EdgeTargetKinds.IsEmpty)
            throw new ArgumentException("At least one edge target subject kind is required.", nameof(edgeTargetKinds));
        if (OccurrenceSourceKinds.IsEmpty)
            throw new ArgumentException("At least one occurrence source subject kind is required.", nameof(occurrenceSourceKinds));
        if (OccurrenceTargetKinds.IsEmpty)
            throw new ArgumentException("At least one occurrence target subject kind is required.", nameof(occurrenceTargetKinds));
        if (Evidence.IsEmpty && semantics != InspectionGraphRelationshipSemantics.Synthetic)
            throw new ArgumentException("A non-synthetic relationship requires an evidence contract.", nameof(evidence));
        if (EdgeSourceKinds.Distinct().Count() != EdgeSourceKinds.Length)
            throw new ArgumentException("Edge source subject kinds must be distinct.", nameof(edgeSourceKinds));
        if (EdgeTargetKinds.Distinct().Count() != EdgeTargetKinds.Length)
            throw new ArgumentException("Edge target subject kinds must be distinct.", nameof(edgeTargetKinds));
        if (OccurrenceSourceKinds.Distinct().Count() != OccurrenceSourceKinds.Length)
            throw new ArgumentException("Occurrence source subject kinds must be distinct.", nameof(occurrenceSourceKinds));
        if (OccurrenceTargetKinds.Distinct().Count() != OccurrenceTargetKinds.Length)
            throw new ArgumentException("Occurrence target subject kinds must be distinct.", nameof(occurrenceTargetKinds));
        if (SeedAdmissions.Distinct().Count() != SeedAdmissions.Length)
        {
            throw new ArgumentException(
                "Seed admissions must be distinct.",
                nameof(seedAdmissions));
        }
        if (Evidence.Distinct().Count() != Evidence.Length)
            throw new ArgumentException("Evidence descriptors must be distinct.", nameof(evidence));
        if (Evidence.Select(static item => item.Id)
                .Distinct(StringComparer.Ordinal).Count()
            != Evidence.Length)
        {
            throw new ArgumentException(
                "Evidence descriptor ids must be distinct.",
                nameof(evidence));
        }
        EndpointProjection = endpointProjection;
        OccurrenceIdentity = occurrenceIdentity;
    }

    public string Id { get; }
    public InspectionGraphOwner Owner { get; }
    public InspectionGraphRelationshipSemantics Semantics { get; }
    public ImmutableArray<InspectionGraphSubjectKind> EdgeSourceKinds { get; }
    public ImmutableArray<InspectionGraphSubjectKind> EdgeTargetKinds { get; }
    public ImmutableArray<InspectionGraphSubjectKind> OccurrenceSourceKinds
        { get; }
    public ImmutableArray<InspectionGraphSubjectKind> OccurrenceTargetKinds
        { get; }
    public ImmutableArray<InspectionGraphSeedAdmission> SeedAdmissions { get; }
    public InspectionGraphEndpointProjection EndpointProjection { get; }
    public InspectionGraphOccurrenceIdentityProjection OccurrenceIdentity
        { get; }
    public ImmutableArray<InspectionGraphEvidenceDescriptor> Evidence { get; }

    internal bool AdmitsEdgeSource(InspectionGraphSubject subject) =>
        EdgeSourceKinds.Contains(subject.Kind);

    internal bool AdmitsEdgeTarget(InspectionGraphSubject subject) =>
        EdgeTargetKinds.Contains(subject.Kind);

    internal bool AdmitsOccurrenceSource(
        InspectionGraphSubject subject) =>
        OccurrenceSourceKinds.Contains(subject.Kind);

    internal bool AdmitsOccurrenceTarget(
        InspectionGraphSubject subject) =>
        OccurrenceTargetKinds.Contains(subject.Kind);

    public ImmutableArray<InspectionGraphSeedAdmission> GetSeedAdmissions(
        InspectionGraphSubjectKind subjectKind)
    {
        InspectionGraphCollections.RequireDefined(
            subjectKind,
            nameof(subjectKind));
        return
        [
            .. SeedAdmissions.Where(
                admission => admission.SubjectKind == subjectKind),
        ];
    }

    internal bool AdmitsEvidence(
        IInspectionGraphOccurrenceEvidence evidence) =>
        Evidence.Contains(evidence.Descriptor);

    IEnumerable<InspectionGraphSubjectKind> OwnedEndpointKinds(
        InspectionGraphEndpointRole role) =>
        role switch
        {
            InspectionGraphEndpointRole.Source =>
                EdgeSourceKinds.Concat(OccurrenceSourceKinds),
            InspectionGraphEndpointRole.Target =>
                EdgeTargetKinds.Concat(OccurrenceTargetKinds),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    static bool CanStrictlyOwn(
        InspectionGraphSubjectKind owner,
        InspectionGraphSubjectKind subject) =>
        (owner, subject) switch
        {
            (
                InspectionGraphSubjectKind.Type,
                InspectionGraphSubjectKind.Member) =>
                true,
            (
                InspectionGraphSubjectKind.Assembly,
                InspectionGraphSubjectKind.Type
                    or InspectionGraphSubjectKind.Member) =>
                true,
            (
                InspectionGraphSubjectKind.Package,
                InspectionGraphSubjectKind.Assembly
                    or InspectionGraphSubjectKind.Type
                    or InspectionGraphSubjectKind.Member) =>
                true,
            _ => false,
        };
}

/// <summary>Document-local node classification.</summary>
public enum InspectionGraphNodeRole
{
    Unclassified,
    Ordinary,
    External,
    Truncated,
}

/// <summary>One semantic subject retained as a graph node.</summary>
public sealed class InspectionGraphNode
{
    public InspectionGraphNode(
        int id,
        InspectionGraphSubject subject,
        InspectionGraphNodeRole role,
        IEnumerable<int> groupIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentNullException.ThrowIfNull(subject);
        InspectionGraphCollections.RequireDefined(role, nameof(role));
        Id = id;
        Subject = subject;
        Role = role;
        GroupIds = InspectionGraphCollections.Snapshot(
            groupIds,
            nameof(groupIds));
        if (GroupIds.Distinct().Count() != GroupIds.Length)
            throw new ArgumentException("Group ids must be distinct.", nameof(groupIds));
    }

    public int Id { get; }
    public InspectionGraphSubject Subject { get; }
    public InspectionGraphNodeRole Role { get; }
    public ImmutableArray<int> GroupIds { get; }
}

/// <summary>One typed grouping lens over graph nodes.</summary>
public sealed class InspectionGraphGroup
{
    public InspectionGraphGroup(
        int id,
        InspectionGraphSubject subject,
        int? parentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        if (parentId is not null)
            ArgumentOutOfRangeException.ThrowIfNegative(parentId.Value);
        ArgumentNullException.ThrowIfNull(subject);
        Id = id;
        Subject = subject;
        ParentId = parentId;
    }

    public int Id { get; }
    public InspectionGraphSubject Subject { get; }
    public int? ParentId { get; }
}

/// <summary>One contribution to a logical graph edge.</summary>
public sealed class InspectionGraphOccurrence
{
    public InspectionGraphOccurrence(
        int id,
        InspectionGraphRelationshipDescriptor relationship,
        InspectionGraphSubject sourceSubject,
        InspectionGraphSubject targetSubject,
        IInspectionGraphOccurrenceEvidence evidence,
        IEnumerable<int> derivedFromOccurrenceIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(sourceSubject);
        ArgumentNullException.ThrowIfNull(targetSubject);
        ArgumentNullException.ThrowIfNull(evidence);
        Id = id;
        Relationship = relationship;
        SourceSubject = sourceSubject;
        TargetSubject = targetSubject;
        Evidence = evidence;
        DerivedFromOccurrenceIds = InspectionGraphCollections.Snapshot(
            derivedFromOccurrenceIds,
            nameof(derivedFromOccurrenceIds));
        if (DerivedFromOccurrenceIds.Distinct().Count()
            != DerivedFromOccurrenceIds.Length)
        {
            throw new ArgumentException(
                "Derived occurrence ids must be distinct.",
                nameof(derivedFromOccurrenceIds));
        }
    }

    public int Id { get; }
    public InspectionGraphRelationshipDescriptor Relationship { get; }
    public InspectionGraphSubject SourceSubject { get; }
    public InspectionGraphSubject TargetSubject { get; }
    public IInspectionGraphOccurrenceEvidence Evidence { get; }
    public ImmutableArray<int> DerivedFromOccurrenceIds { get; }
}

/// <summary>One directed logical relationship between two graph nodes.</summary>
public sealed class InspectionGraphEdge
{
    public InspectionGraphEdge(
        int id,
        int fromNodeId,
        int toNodeId,
        InspectionGraphRelationshipDescriptor relationship,
        IEnumerable<int> occurrenceIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfNegative(fromNodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(toNodeId);
        ArgumentNullException.ThrowIfNull(relationship);
        Id = id;
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        Relationship = relationship;
        OccurrenceIds = InspectionGraphCollections.Snapshot(
            occurrenceIds,
            nameof(occurrenceIds));
        if (OccurrenceIds.Distinct().Count() != OccurrenceIds.Length)
            throw new ArgumentException("Occurrence ids must be distinct.", nameof(occurrenceIds));
    }

    public int Id { get; }
    public int FromNodeId { get; }
    public int ToNodeId { get; }
    public InspectionGraphRelationshipDescriptor Relationship { get; }
    public ImmutableArray<int> OccurrenceIds { get; }
}

/// <summary>The document collection addressed by a graph target.</summary>
public enum InspectionGraphTargetKind
{
    Node,
    Group,
    Edge,
    Occurrence,
}

/// <summary>A typed document-local characteristic target.</summary>
public readonly record struct InspectionGraphTarget
{
    private readonly bool _initialized;

    private InspectionGraphTarget(InspectionGraphTargetKind kind, int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        Kind = kind;
        Id = id;
        _initialized = true;
    }

    public InspectionGraphTargetKind Kind { get; }
    public int Id { get; }

    internal bool IsInitialized => _initialized;

    public static InspectionGraphTarget Node(int id) =>
        new(InspectionGraphTargetKind.Node, id);

    public static InspectionGraphTarget Group(int id) =>
        new(InspectionGraphTargetKind.Group, id);

    public static InspectionGraphTarget Edge(int id) =>
        new(InspectionGraphTargetKind.Edge, id);

    public static InspectionGraphTarget Occurrence(int id) =>
        new(InspectionGraphTargetKind.Occurrence, id);
}

/// <summary>The typed storage shape of a characteristic value.</summary>
public enum InspectionGraphValueShape
{
    Boolean,
    Integer,
    Token,
    TokenSet,
    Structured,
}

/// <summary>The typed contract for one characteristic value representation.</summary>
public sealed class InspectionGraphValueDescriptor
{
    public InspectionGraphValueDescriptor(
        string id,
        InspectionGraphValueShape shape)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        InspectionGraphCollections.RequireDefined(shape, nameof(shape));
        Id = id;
        Shape = shape;
    }

    public string Id { get; }
    public InspectionGraphValueShape Shape { get; }
}

/// <summary>Built-in scalar and set value contracts.</summary>
public static class InspectionGraphValueCatalog
{
    public static InspectionGraphValueDescriptor Boolean { get; } =
        new("boolean", InspectionGraphValueShape.Boolean);

    public static InspectionGraphValueDescriptor Integer { get; } =
        new("integer", InspectionGraphValueShape.Integer);

    public static InspectionGraphValueDescriptor Token { get; } =
        new("token", InspectionGraphValueShape.Token);

    public static InspectionGraphValueDescriptor TokenSet { get; } =
        new("token-set", InspectionGraphValueShape.TokenSet);
}

/// <summary>One typed L1 characteristic value.</summary>
public abstract record InspectionGraphValue
{
    protected InspectionGraphValue()
    {
    }

    public abstract InspectionGraphValueDescriptor Descriptor { get; }

    public InspectionGraphValueShape Shape => Descriptor.Shape;

    public sealed record Boolean(bool Value) : InspectionGraphValue
    {
        public override InspectionGraphValueDescriptor Descriptor =>
            InspectionGraphValueCatalog.Boolean;
    }

    public sealed record Integer(long Value) : InspectionGraphValue
    {
        public override InspectionGraphValueDescriptor Descriptor =>
            InspectionGraphValueCatalog.Integer;
    }

    public sealed record Token : InspectionGraphValue
    {
        public Token(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            Value = value;
        }

        public string Value { get; }

        public override InspectionGraphValueDescriptor Descriptor =>
            InspectionGraphValueCatalog.Token;
    }

    public sealed record TokenSet : InspectionGraphValue
    {
        public TokenSet(IEnumerable<string> values)
        {
            Values = InspectionGraphCollections.Snapshot(
                values,
                nameof(values));
            if (Values.IsEmpty)
                throw new ArgumentException("A token set cannot be empty.", nameof(values));
            if (Values.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Tokens cannot be empty.", nameof(values));
            if (Values.Distinct(StringComparer.Ordinal).Count() != Values.Length)
                throw new ArgumentException("Tokens must be distinct.", nameof(values));
        }

        public ImmutableArray<string> Values { get; }

        public override InspectionGraphValueDescriptor Descriptor =>
            InspectionGraphValueCatalog.TokenSet;

        public bool Equals(TokenSet? other) =>
            other is not null && Values.SequenceEqual(other.Values);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (string value in Values)
                hash.Add(value);
            return hash.ToHashCode();
        }
    }
}

/// <summary>How a characteristic value was established.</summary>
public enum InspectionGraphCharacteristicDerivationKind
{
    Direct,
    Aggregated,
    RolledUp,
    Derived,
}

/// <summary>A descriptor-owned aggregation rule.</summary>
public enum InspectionGraphAggregationPolicy
{
    None,
    Any,
    All,
    DistinctOccurrenceCount,
    DistinctSubjectCount,
    Sum,
    Maximum,
    OrderedDistinctSet,
    StrongestDisposition,
    ProducerDefined,
}

/// <summary>The L1 semantic contract for one optional characteristic.</summary>
public sealed class InspectionGraphCharacteristicDescriptor
{
    public InspectionGraphCharacteristicDescriptor(
        string id,
        InspectionGraphOwner owner,
        InspectionGraphValueDescriptor value,
        IEnumerable<InspectionGraphTargetKind> targets,
        IEnumerable<InspectionQueryDefinition> prerequisites,
        IEnumerable<InspectionGraphCharacteristicDerivationKind>
            admittedDerivations,
        InspectionGraphAggregationPolicy aggregation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(value);
        InspectionGraphCollections.RequireDefined(owner, nameof(owner));
        InspectionGraphCollections.RequireDefined(
            aggregation,
            nameof(aggregation));
        Id = id;
        Owner = owner;
        Value = value;
        Targets = InspectionGraphCollections.Snapshot(
            targets,
            nameof(targets));
        Prerequisites = InspectionGraphCollections.Snapshot(
            prerequisites,
            nameof(prerequisites));
        AdmittedDerivations = InspectionGraphCollections.Snapshot(
            admittedDerivations,
            nameof(admittedDerivations));
        InspectionGraphCollections.RequireDefined(
            Targets,
            nameof(targets));
        InspectionGraphCollections.RequireDefined(
            AdmittedDerivations,
            nameof(admittedDerivations));
        if (Targets.IsEmpty)
            throw new ArgumentException("At least one target kind is required.", nameof(targets));
        if (AdmittedDerivations.IsEmpty)
            throw new ArgumentException("At least one derivation kind is required.", nameof(admittedDerivations));
        if (Targets.Distinct().Count() != Targets.Length)
            throw new ArgumentException("Target kinds must be distinct.", nameof(targets));
        if (Prerequisites.Distinct().Count() != Prerequisites.Length)
            throw new ArgumentException("Prerequisites must be distinct.", nameof(prerequisites));
        if (AdmittedDerivations.Distinct().Count() != AdmittedDerivations.Length)
            throw new ArgumentException("Derivation kinds must be distinct.", nameof(admittedDerivations));
        Aggregation = aggregation;
    }

    public string Id { get; }
    public InspectionGraphOwner Owner { get; }
    public InspectionGraphValueDescriptor Value { get; }
    public InspectionGraphValueShape ValueShape => Value.Shape;
    public ImmutableArray<InspectionGraphTargetKind> Targets { get; }
    public ImmutableArray<InspectionQueryDefinition> Prerequisites { get; }
    public ImmutableArray<InspectionGraphCharacteristicDerivationKind>
        AdmittedDerivations { get; }
    public InspectionGraphAggregationPolicy Aggregation { get; }
}

/// <summary>Provenance for one characteristic value.</summary>
public sealed class InspectionGraphCharacteristicDerivation
{
    public InspectionGraphCharacteristicDerivation(
        InspectionGraphCharacteristicDerivationKind kind,
        IEnumerable<InspectionGraphTarget> sources)
    {
        InspectionGraphCollections.RequireDefined(kind, nameof(kind));
        Kind = kind;
        Sources = InspectionGraphCollections.Snapshot(
            sources,
            nameof(sources));
        if (Sources.Distinct().Count() != Sources.Length)
        {
            throw new ArgumentException(
                "Derivation sources must be distinct.",
                nameof(sources));
        }
    }

    public InspectionGraphCharacteristicDerivationKind Kind { get; }
    public ImmutableArray<InspectionGraphTarget> Sources { get; }
}

/// <summary>One optional typed value attached to a graph target.</summary>
public sealed record InspectionGraphCharacteristic(
    InspectionGraphCharacteristicDescriptor Descriptor,
    InspectionGraphTarget Target,
    InspectionGraphValue Value,
    InspectionGraphCharacteristicDerivation Derivation);

/// <summary>The role one requested subject has in the graph.</summary>
public enum InspectionGraphSeedRole
{
    Primary,
    Peer,
}

/// <summary>A requested subject bound to its node or group.</summary>
public sealed record InspectionGraphSeed(
    InspectionGraphSubject Subject,
    InspectionGraphTarget Target,
    InspectionGraphSeedRole Role);

/// <summary>The L1 identity of one completeness limit.</summary>
public sealed class InspectionGraphLimitDescriptor
{
    public InspectionGraphLimitDescriptor(
        string id,
        InspectionGraphOwner owner,
        IEnumerable<InspectionGraphEvidenceDescriptor>? evidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        InspectionGraphCollections.RequireDefined(owner, nameof(owner));
        Id = id;
        Owner = owner;
        Evidence = InspectionGraphCollections.Snapshot(
            evidence ?? [],
            nameof(evidence));
        if (Evidence.Select(static item => item.Id)
                .Distinct(StringComparer.Ordinal).Count()
            != Evidence.Length)
        {
            throw new ArgumentException(
                "Evidence descriptor ids must be distinct.",
                nameof(evidence));
        }
    }

    public string Id { get; }
    public InspectionGraphOwner Owner { get; }
    public ImmutableArray<InspectionGraphEvidenceDescriptor> Evidence { get; }

    internal bool AdmitsEvidence(
        IInspectionGraphDiagnosticEvidence evidence) =>
        Evidence.Contains(evidence.Descriptor);
}

/// <summary>A completeness limit, optionally scoped to one target.</summary>
public sealed record InspectionGraphLimit(
    InspectionGraphLimitDescriptor Descriptor,
    InspectionGraphTarget? Target = null,
    IInspectionGraphDiagnosticEvidence? Evidence = null);

/// <summary>The L1 identity of one producer failure.</summary>
public sealed class InspectionGraphFailureDescriptor
{
    public InspectionGraphFailureDescriptor(
        string id,
        InspectionGraphOwner owner,
        IEnumerable<InspectionGraphEvidenceDescriptor>? evidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        InspectionGraphCollections.RequireDefined(owner, nameof(owner));
        Id = id;
        Owner = owner;
        Evidence = InspectionGraphCollections.Snapshot(
            evidence ?? [],
            nameof(evidence));
        if (Evidence.Select(static item => item.Id)
                .Distinct(StringComparer.Ordinal).Count()
            != Evidence.Length)
        {
            throw new ArgumentException(
                "Evidence descriptor ids must be distinct.",
                nameof(evidence));
        }
    }

    public string Id { get; }
    public InspectionGraphOwner Owner { get; }
    public ImmutableArray<InspectionGraphEvidenceDescriptor> Evidence { get; }

    internal bool AdmitsEvidence(
        IInspectionGraphDiagnosticEvidence evidence) =>
        Evidence.Contains(evidence.Descriptor);
}

/// <summary>A visible producer failure, optionally scoped to one target.</summary>
public sealed record InspectionGraphFailure(
    InspectionGraphFailureDescriptor Descriptor,
    InspectionGraphTarget? Target = null,
    IInspectionGraphDiagnosticEvidence? Evidence = null);

/// <summary>
/// The lifetime authority retained by an inspection graph.
/// </summary>
public enum InspectionGraphDocumentScope
{
    SessionBound,
    Portable,
}

/// <summary>
/// Immutable, validated L1 graph facts shared by every presentation.
/// </summary>
public sealed class InspectionGraphDocument
{
    public InspectionGraphDocument(
        InspectionGraphDocumentScope scope,
        InspectionGraphModeRequest modeRequest,
        IEnumerable<InspectionGraphNode> nodes,
        IEnumerable<InspectionGraphGroup> groups,
        IEnumerable<InspectionGraphEdge> edges,
        IEnumerable<InspectionGraphOccurrence> occurrences,
        IEnumerable<InspectionGraphCharacteristic> characteristics,
        IEnumerable<InspectionGraphSeed> seeds,
        IEnumerable<InspectionGraphLimit> limits,
        IEnumerable<InspectionGraphFailure> failures)
        : this(
            scope,
            modeRequest,
            neighborhoodRequest: null,
            inducedSetRequest: null,
            nodes,
            groups,
            edges,
            occurrences,
            characteristics,
            seeds,
            limits,
            failures)
    {
    }

    public InspectionGraphDocument(
        InspectionGraphDocumentScope scope,
        InspectionGraphNeighborhoodRequest neighborhoodRequest,
        IEnumerable<InspectionGraphNode> nodes,
        IEnumerable<InspectionGraphGroup> groups,
        IEnumerable<InspectionGraphEdge> edges,
        IEnumerable<InspectionGraphOccurrence> occurrences,
        IEnumerable<InspectionGraphCharacteristic> characteristics,
        IEnumerable<InspectionGraphSeed> seeds,
        IEnumerable<InspectionGraphLimit> limits,
        IEnumerable<InspectionGraphFailure> failures)
        : this(
            scope,
            neighborhoodRequest?.ModeRequest
                ?? throw new ArgumentNullException(
                    nameof(neighborhoodRequest)),
            neighborhoodRequest,
            inducedSetRequest: null,
            nodes,
            groups,
            edges,
            occurrences,
            characteristics,
            seeds,
            limits,
            failures)
    {
    }

    public InspectionGraphDocument(
        InspectionGraphDocumentScope scope,
        InspectionGraphInducedSetRequest inducedSetRequest,
        IEnumerable<InspectionGraphNode> nodes,
        IEnumerable<InspectionGraphGroup> groups,
        IEnumerable<InspectionGraphEdge> edges,
        IEnumerable<InspectionGraphOccurrence> occurrences,
        IEnumerable<InspectionGraphCharacteristic> characteristics,
        IEnumerable<InspectionGraphSeed> seeds,
        IEnumerable<InspectionGraphLimit> limits,
        IEnumerable<InspectionGraphFailure> failures)
        : this(
            scope,
            inducedSetRequest?.ModeRequest
                ?? throw new ArgumentNullException(
                    nameof(inducedSetRequest)),
            neighborhoodRequest: null,
            inducedSetRequest,
            nodes,
            groups,
            edges,
            occurrences,
            characteristics,
            seeds,
            limits,
            failures)
    {
    }

    InspectionGraphDocument(
        InspectionGraphDocumentScope scope,
        InspectionGraphModeRequest modeRequest,
        InspectionGraphNeighborhoodRequest? neighborhoodRequest,
        InspectionGraphInducedSetRequest? inducedSetRequest,
        IEnumerable<InspectionGraphNode> nodes,
        IEnumerable<InspectionGraphGroup> groups,
        IEnumerable<InspectionGraphEdge> edges,
        IEnumerable<InspectionGraphOccurrence> occurrences,
        IEnumerable<InspectionGraphCharacteristic> characteristics,
        IEnumerable<InspectionGraphSeed> seeds,
        IEnumerable<InspectionGraphLimit> limits,
        IEnumerable<InspectionGraphFailure> failures)
    {
        InspectionGraphCollections.RequireDefined(scope, nameof(scope));
        ArgumentNullException.ThrowIfNull(modeRequest);
        Scope = scope;
        ModeRequest = modeRequest;
        NeighborhoodRequest = neighborhoodRequest;
        InducedSetRequest = inducedSetRequest;
        if (neighborhoodRequest is not null
            && !ReferenceEquals(
                neighborhoodRequest.ModeRequest,
                modeRequest))
        {
            throw new ArgumentException(
                "A neighborhood request must own the document mode request.",
                nameof(neighborhoodRequest));
        }
        if (inducedSetRequest is not null
            && !ReferenceEquals(
                inducedSetRequest.ModeRequest,
                modeRequest))
        {
            throw new ArgumentException(
                "An induced-set request must own the document mode request.",
                nameof(inducedSetRequest));
        }
        if (modeRequest.InducedSetRule
                == InspectionGraphInducedSetRule.ExplicitSubjects
            && inducedSetRequest is null)
        {
            throw new ArgumentException(
                "Explicit-subject induced mode requires its typed request.",
                nameof(inducedSetRequest));
        }
        Nodes = InspectionGraphCollections.Snapshot(nodes, nameof(nodes));
        Groups = InspectionGraphCollections.Snapshot(groups, nameof(groups));
        Edges = InspectionGraphCollections.Snapshot(edges, nameof(edges));
        Occurrences = InspectionGraphCollections.Snapshot(
            occurrences,
            nameof(occurrences));
        Characteristics = InspectionGraphCollections.Snapshot(
            characteristics,
            nameof(characteristics));
        Seeds = InspectionGraphCollections.Snapshot(seeds, nameof(seeds));
        Limits = InspectionGraphCollections.Snapshot(limits, nameof(limits));
        Failures = InspectionGraphCollections.Snapshot(
            failures,
            nameof(failures));

        ValidateDenseIds(Nodes, static node => node.Id, nameof(nodes));
        ValidateDenseIds(Groups, static group => group.Id, nameof(groups));
        ValidateDenseIds(Edges, static edge => edge.Id, nameof(edges));
        ValidateDenseIds(
            Occurrences,
            static occurrence => occurrence.Id,
            nameof(occurrences));
        if (Nodes.Select(static node => node.Subject).Distinct().Count()
            != Nodes.Length)
        {
            throw new ArgumentException(
                "A semantic subject can appear as at most one node.",
                nameof(nodes));
        }
        if (Scope == InspectionGraphDocumentScope.Portable)
            ValidatePortableSubjects();
        foreach (InspectionGraphOccurrence occurrence in Occurrences)
            ArgumentNullException.ThrowIfNull(occurrence.Evidence.Descriptor);
        foreach (InspectionGraphCharacteristic characteristic
            in Characteristics)
        {
            ArgumentNullException.ThrowIfNull(characteristic.Descriptor);
            ArgumentNullException.ThrowIfNull(characteristic.Value);
            ArgumentNullException.ThrowIfNull(characteristic.Derivation);
        }
        foreach (InspectionGraphLimit limit in Limits)
        {
            ArgumentNullException.ThrowIfNull(limit.Descriptor);
            if (limit.Evidence is not null)
                ArgumentNullException.ThrowIfNull(limit.Evidence.Descriptor);
        }
        foreach (InspectionGraphFailure failure in Failures)
        {
            ArgumentNullException.ThrowIfNull(failure.Descriptor);
            if (failure.Evidence is not null)
                ArgumentNullException.ThrowIfNull(
                    failure.Evidence.Descriptor);
        }
        ValidateDescriptorIds();
        ValidateGroups();
        ValidateEdgesAndOccurrences();
        ValidateCharacteristics();
        ValidateSeeds();
        ValidateDiagnostics();
        ValidateProjectionRequest();
    }

    public InspectionGraphDocumentScope Scope { get; }
    public InspectionGraphModeRequest ModeRequest { get; }
    public InspectionGraphNeighborhoodRequest? NeighborhoodRequest { get; }
    public InspectionGraphInducedSetRequest? InducedSetRequest { get; }
    public ImmutableArray<InspectionGraphNode> Nodes { get; }
    public ImmutableArray<InspectionGraphGroup> Groups { get; }
    public ImmutableArray<InspectionGraphEdge> Edges { get; }
    public ImmutableArray<InspectionGraphOccurrence> Occurrences { get; }
    public ImmutableArray<InspectionGraphCharacteristic> Characteristics { get; }
    public ImmutableArray<InspectionGraphSeed> Seeds { get; }
    public ImmutableArray<InspectionGraphLimit> Limits { get; }
    public ImmutableArray<InspectionGraphFailure> Failures { get; }

    private void ValidateGroups()
    {
        foreach (InspectionGraphGroup group in Groups)
        {
            if (group.ParentId is int parentId)
            {
                ValidateId(parentId, Groups.Length, "Group parent");
                if (parentId == group.Id)
                    throw new ArgumentException("A group cannot be its own parent.", nameof(Groups));
            }

        }

        var states = new byte[Groups.Length];
        for (var id = 0; id < Groups.Length; id++)
            VisitGroup(id, states);

        foreach (InspectionGraphNode node in Nodes)
        {
            foreach (int groupId in node.GroupIds)
                ValidateId(groupId, Groups.Length, "Node group");
        }
    }

    private void ValidateProjectionRequest()
    {
        if (InducedSetRequest is null)
            return;

        var subjects = Nodes.Select(static node => node.Subject)
            .Concat(Groups.Select(static group => group.Subject))
            .ToHashSet();
        if (InducedSetRequest.Subjects.Any(subject =>
            !subjects.Contains(subject)))
        {
            throw new ArgumentException(
                "Every explicit induced-set subject must be represented by a node or group.",
                nameof(InducedSetRequest));
        }
        var relationships = InducedSetRequest.Relationships.ToHashSet();
        if (Edges.Any(edge =>
            !relationships.Contains(edge.Relationship)))
        {
            throw new ArgumentException(
                "An explicit induced-set document can contain only selected relationships.",
                nameof(Edges));
        }

        IReadOnlyDictionary<InspectionGraphSubject, InspectionGraphNode>
            nodesBySubject = Nodes.ToDictionary(
                static node => node.Subject);
        foreach (InspectionGraphEdge edge in Edges)
        {
            foreach (int occurrenceId in edge.OccurrenceIds)
            {
                InspectionGraphOccurrence occurrence =
                    Occurrences[occurrenceId];
                if (InspectionGraphProjectionUtilities.AdmitsEndpoint(
                        this,
                        nodesBySubject,
                        InducedSetRequest.Subjects,
                        edge,
                        occurrence,
                        InspectionGraphEndpointRole.Source)
                    && InspectionGraphProjectionUtilities.AdmitsEndpoint(
                        this,
                        nodesBySubject,
                        InducedSetRequest.Subjects,
                        edge,
                        occurrence,
                        InspectionGraphEndpointRole.Target))
                {
                    continue;
                }

                throw new ArgumentException(
                    "Every explicit induced-set occurrence must be admitted on both semantic endpoint roles.",
                    nameof(Occurrences));
            }
        }

        InspectionGraphLimit[] subjectBounds =
        [
            .. Limits.Where(limit =>
                ReferenceEquals(
                    limit.Descriptor,
                    InspectionGraphInducedSetCatalog.SubjectBound)),
        ];
        if (subjectBounds.Length != 1
            || subjectBounds[0].Target is not null
            || subjectBounds[0].Evidence
                is not InspectionGraphInducedSubjectBoundEvidence evidence
            || evidence.SubjectCount
                != InducedSetRequest.Subjects.Length)
        {
            throw new ArgumentException(
                "An explicit induced-set document requires one global subject bound matching its input count.",
                nameof(Limits));
        }
    }

    private void ValidatePortableSubjects()
    {
        IEnumerable<InspectionGraphSubject> subjects =
            Nodes.Select(static node => node.Subject)
                .Concat(Groups.Select(static group => group.Subject))
                .Concat(Occurrences.SelectMany(static occurrence =>
                    new[]
                    {
                        occurrence.SourceSubject,
                        occurrence.TargetSubject,
                    }))
                .Concat(Seeds.Select(static seed => seed.Subject))
                .Concat(ModeRequest.Seeds);
        if (subjects.Any(static subject => !subject.IsPortable))
        {
            throw new ArgumentException(
                "A portable document cannot retain a session-bound subject identity.",
                nameof(Scope));
        }
    }

    private void VisitGroup(int id, byte[] states)
    {
        if (states[id] == 2)
            return;
        if (states[id] == 1)
            throw new ArgumentException("Group parents must not form a cycle.", nameof(Groups));

        states[id] = 1;
        if (Groups[id].ParentId is int parentId)
            VisitGroup(parentId, states);
        states[id] = 2;
    }

    private void ValidateEdgesAndOccurrences()
    {
        var boundOccurrences = new int[Occurrences.Length];
        var logicalEdges = new HashSet<(
            int From,
            int To,
            InspectionGraphRelationshipDescriptor Relationship)>();

        foreach (InspectionGraphEdge edge in Edges)
        {
            ValidateId(edge.FromNodeId, Nodes.Length, "Edge source node");
            ValidateId(edge.ToNodeId, Nodes.Length, "Edge target node");
            InspectionGraphSubject source = Nodes[edge.FromNodeId].Subject;
            InspectionGraphSubject target = Nodes[edge.ToNodeId].Subject;
            if (!edge.Relationship.AdmitsEdgeSource(source)
                || !edge.Relationship.AdmitsEdgeTarget(target))
            {
                throw new ArgumentException(
                    "An edge endpoint has a subject kind the relationship does not admit.",
                    nameof(Edges));
            }
            if (!logicalEdges.Add((
                edge.FromNodeId,
                edge.ToNodeId,
                edge.Relationship)))
            {
                throw new ArgumentException(
                    "Logical edges must be unique by source, target, and relationship.",
                    nameof(Edges));
            }
            if (edge.OccurrenceIds.IsEmpty
                && edge.Relationship.Semantics
                    != InspectionGraphRelationshipSemantics.Synthetic)
            {
                throw new ArgumentException(
                    "A non-synthetic edge requires at least one occurrence.",
                    nameof(Edges));
            }

            foreach (int occurrenceId in edge.OccurrenceIds)
            {
                ValidateId(
                    occurrenceId,
                    Occurrences.Length,
                    "Edge occurrence");
                InspectionGraphOccurrence occurrence =
                    Occurrences[occurrenceId];
                if (!ReferenceEquals(
                    occurrence.Relationship,
                    edge.Relationship))
                {
                    throw new ArgumentException(
                        "An occurrence relationship must equal its edge relationship.",
                        nameof(Edges));
                }
                if (!edge.Relationship.AdmitsOccurrenceSource(
                        occurrence.SourceSubject)
                    || !edge.Relationship.AdmitsOccurrenceTarget(
                        occurrence.TargetSubject))
                {
                    throw new ArgumentException(
                        "An occurrence endpoint has a subject kind the relationship does not admit.",
                        nameof(Occurrences));
                }
                if (!edge.Relationship.EndpointProjection.Supports(
                        occurrence,
                        InspectionGraphEndpointRole.Source,
                        source)
                    || !edge.Relationship.EndpointProjection.Supports(
                        occurrence,
                        InspectionGraphEndpointRole.Target,
                        target))
                {
                    throw new ArgumentException(
                        "An occurrence does not project to its edge endpoints in semantic direction.",
                        nameof(Edges));
                }
                if (!edge.Relationship.AdmitsEvidence(
                    occurrence.Evidence))
                {
                    throw new ArgumentException(
                        "Occurrence evidence is not admitted by its relationship.",
                        nameof(Occurrences));
                }

                boundOccurrences[occurrenceId]++;
            }
        }

        if (boundOccurrences.Any(static count => count == 0))
            throw new ArgumentException("Every occurrence must support at least one edge.", nameof(Occurrences));

        var occurrenceIdentities =
            new Dictionary<
                InspectionGraphRelationshipDescriptor,
                HashSet<object>>();
        foreach (InspectionGraphOccurrence occurrence in Occurrences)
        {
            object identity =
                occurrence.Relationship.OccurrenceIdentity.Project(
                    occurrence)
                ?? throw new ArgumentException(
                    "An occurrence identity cannot be null.",
                    nameof(Occurrences));
            if (!occurrenceIdentities.TryGetValue(
                occurrence.Relationship,
                out HashSet<object>? identities))
            {
                identities = [];
                occurrenceIdentities.Add(
                    occurrence.Relationship,
                    identities);
            }
            if (!identities.Add(identity))
            {
                throw new ArgumentException(
                    "Occurrence identities must be unique within a relationship.",
                    nameof(Occurrences));
            }
            if (occurrence.Relationship.Semantics
                    == InspectionGraphRelationshipSemantics.Derived
                && occurrence.DerivedFromOccurrenceIds.IsEmpty)
            {
                throw new ArgumentException(
                    "A derived relationship occurrence must cite its source occurrences.",
                    nameof(Occurrences));
            }
            if (occurrence.Relationship.Semantics
                    != InspectionGraphRelationshipSemantics.Derived
                && !occurrence.DerivedFromOccurrenceIds.IsEmpty)
            {
                throw new ArgumentException(
                    "Only a derived relationship occurrence can cite source occurrences.",
                    nameof(Occurrences));
            }
            foreach (int sourceId in occurrence.DerivedFromOccurrenceIds)
            {
                ValidateId(
                    sourceId,
                    Occurrences.Length,
                    "Derived source occurrence");
                if (sourceId == occurrence.Id)
                    throw new ArgumentException("An occurrence cannot derive from itself.", nameof(Occurrences));
            }
        }

        var derivationStates = new byte[Occurrences.Length];
        for (var id = 0; id < Occurrences.Length; id++)
            VisitOccurrence(id, derivationStates);
    }

    private void VisitOccurrence(int id, byte[] states)
    {
        if (states[id] == 2)
            return;
        if (states[id] == 1)
        {
            throw new ArgumentException(
                "Occurrence derivations must not form a cycle.",
                nameof(Occurrences));
        }

        states[id] = 1;
        foreach (int sourceId
            in Occurrences[id].DerivedFromOccurrenceIds)
        {
            VisitOccurrence(sourceId, states);
        }
        states[id] = 2;
    }

    private void ValidateCharacteristics()
    {
        var identities = new HashSet<(
            InspectionGraphCharacteristicDescriptor Descriptor,
            InspectionGraphTarget Target)>();
        foreach (InspectionGraphCharacteristic characteristic
            in Characteristics)
        {
            ValidateTarget(characteristic.Target);
            if (!identities.Add((
                characteristic.Descriptor,
                characteristic.Target)))
            {
                throw new ArgumentException(
                    "A descriptor can contribute only one value to a target.",
                    nameof(Characteristics));
            }
            if (!characteristic.Descriptor.Targets.Contains(
                characteristic.Target.Kind))
            {
                throw new ArgumentException(
                    "A characteristic target kind is not admitted by its descriptor.",
                    nameof(Characteristics));
            }
            if (!ReferenceEquals(
                characteristic.Value.Descriptor,
                characteristic.Descriptor.Value))
            {
                throw new ArgumentException(
                    "A characteristic value does not match its descriptor contract.",
                    nameof(Characteristics));
            }
            if (!characteristic.Descriptor.AdmittedDerivations.Contains(
                characteristic.Derivation.Kind))
            {
                throw new ArgumentException(
                    "A characteristic derivation is not admitted by its descriptor.",
                    nameof(Characteristics));
            }
            if (characteristic.Derivation.Kind
                    == InspectionGraphCharacteristicDerivationKind.Direct
                && !characteristic.Derivation.Sources.IsEmpty)
            {
                throw new ArgumentException(
                    "A direct characteristic cannot cite derivation sources.",
                    nameof(Characteristics));
            }
            if (characteristic.Derivation.Kind
                    != InspectionGraphCharacteristicDerivationKind.Direct
                && characteristic.Derivation.Sources.IsEmpty)
            {
                throw new ArgumentException(
                    "A non-direct characteristic must cite derivation sources.",
                    nameof(Characteristics));
            }
            if (characteristic.Derivation.Kind
                    is InspectionGraphCharacteristicDerivationKind.Aggregated
                        or InspectionGraphCharacteristicDerivationKind.RolledUp
                && characteristic.Descriptor.Aggregation
                    == InspectionGraphAggregationPolicy.None)
            {
                throw new ArgumentException(
                    "An aggregate characteristic requires a descriptor-owned aggregation policy.",
                    nameof(Characteristics));
            }
            if (characteristic.Derivation.Kind
                    == InspectionGraphCharacteristicDerivationKind.Aggregated
                && characteristic.Derivation.Sources.Any(
                    static source =>
                        source.Kind
                            != InspectionGraphTargetKind.Occurrence))
            {
                throw new ArgumentException(
                    "An aggregated characteristic must cite occurrences.",
                    nameof(Characteristics));
            }
            if (characteristic.Derivation.Kind
                    == InspectionGraphCharacteristicDerivationKind.RolledUp
                && characteristic.Derivation.Sources.Any(
                    static source =>
                        source.Kind is not (
                            InspectionGraphTargetKind.Node
                            or InspectionGraphTargetKind.Group)))
            {
                throw new ArgumentException(
                    "A rolled-up characteristic must cite subject nodes or groups.",
                    nameof(Characteristics));
            }
            foreach (InspectionGraphTarget source
                in characteristic.Derivation.Sources)
                ValidateTarget(source);
        }
    }

    private void ValidateSeeds()
    {
        var targets = new HashSet<InspectionGraphTarget>();
        var primaryCount = 0;
        foreach (InspectionGraphSeed seed in Seeds)
        {
            ArgumentNullException.ThrowIfNull(seed.Subject);
            InspectionGraphCollections.RequireDefined(
                seed.Role,
                nameof(seed.Role));
            if (seed.Target.Kind is not (
                InspectionGraphTargetKind.Node
                or InspectionGraphTargetKind.Group))
            {
                throw new ArgumentException(
                    "A seed must target a node or group.",
                    nameof(Seeds));
            }
            ValidateTarget(seed.Target);
            InspectionGraphSubject targetSubject =
                seed.Target.Kind == InspectionGraphTargetKind.Node
                    ? Nodes[seed.Target.Id].Subject
                    : Groups[seed.Target.Id].Subject;
            if (seed.Subject != targetSubject)
                throw new ArgumentException("A seed subject must equal its target subject.", nameof(Seeds));
            if (!targets.Add(seed.Target))
                throw new ArgumentException("A target can have only one seed role.", nameof(Seeds));
            if (seed.Role == InspectionGraphSeedRole.Primary)
                primaryCount++;
        }

        if (primaryCount > 1)
            throw new ArgumentException("A graph can have at most one primary seed.", nameof(Seeds));

        switch (ModeRequest.Mode)
        {
            case InspectionGraphMode.SingleSeed:
                InspectionGraphSeed primary = Seeds.SingleOrDefault(
                    static seed =>
                        seed.Role == InspectionGraphSeedRole.Primary)
                    ?? throw new ArgumentException(
                        "Single-seed mode requires one primary seed binding.",
                        nameof(Seeds));
                if (Seeds.Length != 1
                    || primary.Subject != ModeRequest.Seeds[0])
                {
                    throw new ArgumentException(
                        "The primary seed binding must match the single-seed request.",
                        nameof(Seeds));
                }
                break;
            case InspectionGraphMode.PeerSeeds:
                if (Seeds.Length != ModeRequest.Seeds.Length
                    || Seeds.Any(static seed =>
                        seed.Role != InspectionGraphSeedRole.Peer)
                    || !Seeds.Select(static seed => seed.Subject)
                        .SequenceEqual(ModeRequest.Seeds))
                {
                    throw new ArgumentException(
                        "Peer seed bindings must preserve every requested peer in request order.",
                        nameof(Seeds));
                }
                break;
            case InspectionGraphMode.InducedSet:
                if (!Seeds.IsEmpty)
                {
                    throw new ArgumentException(
                        "An induced-set graph cannot contain seed bindings.",
                        nameof(Seeds));
                }
                break;
        }
    }

    private void ValidateDiagnostics()
    {
        var limits = new HashSet<(
            InspectionGraphLimitDescriptor Descriptor,
            InspectionGraphTarget? Target)>();
        foreach (InspectionGraphLimit limit in Limits)
        {
            ArgumentNullException.ThrowIfNull(limit.Descriptor);
            if (limit.Target is InspectionGraphTarget target)
                ValidateTarget(target);
            if (limit.Evidence is not null
                && !limit.Descriptor.AdmitsEvidence(limit.Evidence))
            {
                throw new ArgumentException(
                    "Limit evidence is not admitted by its descriptor.",
                    nameof(Limits));
            }
            if (!limits.Add((limit.Descriptor, limit.Target)))
                throw new ArgumentException("Limits must be distinct.", nameof(Limits));
        }

        var failures = new HashSet<(
            InspectionGraphFailureDescriptor Descriptor,
            InspectionGraphTarget? Target)>();
        foreach (InspectionGraphFailure failure in Failures)
        {
            ArgumentNullException.ThrowIfNull(failure.Descriptor);
            if (failure.Target is InspectionGraphTarget target)
                ValidateTarget(target);
            if (failure.Evidence is not null
                && !failure.Descriptor.AdmitsEvidence(failure.Evidence))
            {
                throw new ArgumentException(
                    "Failure evidence is not admitted by its descriptor.",
                    nameof(Failures));
            }
            if (!failures.Add((failure.Descriptor, failure.Target)))
                throw new ArgumentException("Failures must be distinct.", nameof(Failures));
        }
    }

    private void ValidateTarget(InspectionGraphTarget target)
    {
        if (!target.IsInitialized)
            throw new ArgumentException("A graph target must be initialized.", nameof(target));

        int count = target.Kind switch
        {
            InspectionGraphTargetKind.Node => Nodes.Length,
            InspectionGraphTargetKind.Group => Groups.Length,
            InspectionGraphTargetKind.Edge => Edges.Length,
            InspectionGraphTargetKind.Occurrence => Occurrences.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        ValidateId(target.Id, count, "Graph target");
    }

    private void ValidateDescriptorIds()
    {
        ValidateDescriptorIds(
            Edges.Select(static edge => (
                edge.Relationship.Id,
                (object)edge.Relationship))
            .Concat(Occurrences.Select(static occurrence => (
                occurrence.Relationship.Id,
                (object)occurrence.Relationship))),
            "relationship");
        ValidateDescriptorIds(
            Edges.SelectMany(static edge =>
                    edge.Relationship.Evidence)
                .Concat(Occurrences.SelectMany(static occurrence =>
                    occurrence.Relationship.Evidence))
                .Concat(Limits.SelectMany(static limit =>
                    limit.Descriptor.Evidence))
                .Concat(Failures.SelectMany(static failure =>
                    failure.Descriptor.Evidence))
                .Concat(Occurrences.Select(
                    static occurrence =>
                        occurrence.Evidence.Descriptor))
                .Concat(Limits
                    .Where(static limit => limit.Evidence is not null)
                    .Select(static limit =>
                        limit.Evidence!.Descriptor))
                .Concat(Failures
                    .Where(static failure => failure.Evidence is not null)
                    .Select(static failure =>
                        failure.Evidence!.Descriptor))
                .Select(static descriptor => (
                    descriptor.Id,
                    (object)descriptor)),
            "evidence");
        ValidateDescriptorIds(
            Characteristics.Select(static characteristic => (
                characteristic.Descriptor.Id,
                (object)characteristic.Descriptor)),
            "characteristic");
        ValidateDescriptorIds(
            Characteristics.SelectMany(static characteristic =>
                new[]
                {
                    (
                        characteristic.Descriptor.Value.Id,
                        (object)characteristic.Descriptor.Value),
                    (
                        characteristic.Value.Descriptor.Id,
                        (object)characteristic.Value.Descriptor),
                }),
            "value");
        ValidateDescriptorIds(
            Limits.Select(static limit => (
                limit.Descriptor.Id,
                (object)limit.Descriptor)),
            "limit");
        ValidateDescriptorIds(
            Failures.Select(static failure => (
                failure.Descriptor.Id,
                (object)failure.Descriptor)),
            "failure");
    }

    private static void ValidateDescriptorIds(
        IEnumerable<(string Id, object Descriptor)> descriptors,
        string family)
    {
        var byId = new Dictionary<string, object>(
            StringComparer.Ordinal);
        foreach ((string id, object descriptor) in descriptors)
        {
            if (byId.TryGetValue(id, out object? existing)
                && !ReferenceEquals(existing, descriptor))
            {
                throw new ArgumentException(
                    $"Descriptor id '{id}' names more than one {family} contract.");
            }
            byId[id] = descriptor;
        }
    }

    private static void ValidateDenseIds<T>(
        ImmutableArray<T> values,
        Func<T, int> getId,
        string parameterName)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (getId(values[index]) != index)
            {
                throw new ArgumentException(
                    "Document-local ids must be dense, zero-based, and ordered.",
                    parameterName);
            }
        }
    }

    private static void ValidateId(int id, int count, string name)
    {
        if ((uint)id >= (uint)count)
            throw new ArgumentException($"{name} id {id} is outside the document.");
    }
}

static class InspectionGraphCollections
{
    public static void RequireDefined<T>(
        T value,
        string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    public static void RequireDefined<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : struct, Enum
    {
        foreach (T value in values)
            RequireDefined(value, parameterName);
    }

    public static ImmutableArray<T> Snapshot<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values is ImmutableArray<T> immutable && immutable.IsDefault)
            throw new ArgumentException("The immutable array must be initialized.", parameterName);

        ImmutableArray<T> snapshot = values.ToImmutableArray();
        if (snapshot.Any(static value => value is null))
            throw new ArgumentException("Collection elements cannot be null.", parameterName);
        return snapshot;
    }
}
