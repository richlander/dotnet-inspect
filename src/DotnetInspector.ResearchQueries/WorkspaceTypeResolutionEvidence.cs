using System.Collections.Immutable;

using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Queries;

/// <summary>Identifies one live composition's materialization, not a reusable workspace.</summary>
public sealed class WorkspaceProjectionOperationId
{
    internal WorkspaceProjectionOperationId() { }
}

public sealed class WorkspaceGroupOccurrenceId
{
    internal WorkspaceGroupOccurrenceId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

public sealed class WorkspaceAcquisitionOccurrenceId
{
    internal WorkspaceAcquisitionOccurrenceId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

public sealed class WorkspaceArtifactOccurrenceId
{
    internal WorkspaceArtifactOccurrenceId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

public sealed class WorkspaceBindingLineageId
{
    internal WorkspaceBindingLineageId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

public sealed class WorkspaceBindingPolicyVersionId
{
    internal WorkspaceBindingPolicyVersionId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

public sealed class WorkspaceCatalogId
{
    internal WorkspaceCatalogId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

public sealed class WorkspaceDefinitionId
{
    internal WorkspaceDefinitionId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

public sealed class WorkspaceUnresolvedBindingId
{
    internal WorkspaceUnresolvedBindingId(WorkspaceProjectionOperationId operation) => Operation = operation;
    public WorkspaceProjectionOperationId Operation { get; }
}

/// <summary>Materialized Metadata evidence; no member carries an image or an opener.</summary>
public abstract class WorkspaceTypeResolutionEvidence
{
    private WorkspaceTypeResolutionEvidence() { }

    public sealed class Available : WorkspaceTypeResolutionEvidence
    {
        internal Available(WorkspaceMetadataEvidence.Outcome outcome) => Outcome = outcome;
        public WorkspaceMetadataEvidence.Outcome Outcome { get; }
    }

    public sealed class QueryRejected : WorkspaceTypeResolutionEvidence
    {
        internal QueryRejected(QueryComparisonInputId input, WorkspaceMetadataEvidence.CandidateFailure failure)
        {
            Input = input;
            Failure = failure;
        }
        public QueryComparisonInputId Input { get; }
        public WorkspaceMetadataEvidence.CandidateFailure Failure { get; }
    }
}

/// <summary>
/// Queries-owned spelling of the public type-resolution evidence graph.
/// All textual materialization is lossless inert encoding, except the explicitly
/// bounded module-hash summary.
/// </summary>
public static class WorkspaceMetadataEvidence
{
    public sealed record Acquisition(
        WorkspaceAcquisitionOccurrenceId Id,
        WorkspaceArtifactOccurrenceId? ArtifactRegistration,
        Guid? ModuleVersionId,
        QueryComparisonInputId? Input);

    public sealed record Assembly(
        Acquisition Registration,
        AssemblyReferenceIdentity Identity,
        InertString? Path,
        Provenance Provenance,
        DateTime? LastWriteTimeUtc);

    public sealed record Candidate(Assembly Assembly);
    public sealed record Lineage(WorkspaceBindingLineageId Id, WorkspaceBindingPolicyVersionId? Version);
    public sealed record Occurrence(Assembly Assembly, Lineage Lineage);
    public sealed record DefinitionKey(WorkspaceDefinitionId Id, WorkspaceCatalogId Catalog);
    public sealed record TypeName(InertString Namespace, ImmutableArray<InertString> Segments);
    public readonly record struct DefinitionToken(int Value);
    public readonly record struct ExportToken(int Value);
    public readonly record struct DefinitionAddress(Guid ModuleVersionId, DefinitionToken Definition);
    public sealed record HashSummary(int ByteLength, InertString Sha256);
    public sealed record Module(InertString Name, bool ContainsMetadata, HashSummary Hash);

    public sealed record BindingFailure(
        AssemblyBindingFailureKind Kind,
        CandidateOpenFailureKind? CandidateFailureKind,
        MetadataRootMalformedReason? MetadataRootReason);

    public sealed record CandidateFailure(
        CandidateOpenFailureKind Kind,
        InertString Detail,
        MetadataRootMalformedReason? MetadataRootReason);

    public sealed record NameFailure(
        MetadataTypeNameFailureMechanism Mechanism,
        InertString Detail,
        int? SubjectToken,
        int ConsumedNodes,
        RelationshipTraversalRejectionKind? RelationshipKind,
        SignatureDecodeRejectionKind? SignatureKind,
        InertString Kind);

    public sealed record Definition(
        DefinitionKey Key,
        DefinitionAddress Address,
        Candidate Assembly,
        Occurrence Occurrence,
        TypeName Type,
        MetadataTypeDefinitionKind Kind,
        bool IsInterface,
        bool IsValueType,
        bool DeclaringAssemblyDefinesCoreLibraryRoot);

    public sealed record Hop(
        Candidate SourceAssembly,
        Occurrence SourceOccurrence,
        ImmutableArray<ExportToken> Declarations,
        AssemblyReferenceIdentity TargetReference,
        AssemblyResolutionScope Scope);

    public sealed record TypeRequest(Start Start, TypeName Type);
    public sealed record BindingRequest(Target Target, Origin Origin, AssemblyResolutionScope Scope);

    public abstract class Provenance
    {
        private Provenance() { }
        public sealed class PackageAsset(
            InertString packageId, InertString packageVersion, InertString? tfm, InertString? rid) : Provenance
        {
            public InertString PackageId { get; } = packageId;
            public InertString PackageVersion { get; } = packageVersion;
            public InertString? Tfm { get; } = tfm;
            public InertString? Rid { get; } = rid;
        }
        public sealed class PlatformAsset(
            InertString framework, InertString? frameworkVersion, InertString resolverSource) : Provenance
        {
            public InertString Framework { get; } = framework;
            public InertString? FrameworkVersion { get; } = frameworkVersion;
            public InertString ResolverSource { get; } = resolverSource;
        }
        public sealed class ProjectAsset(InertString project, InertString? tfm, InertString? rid) : Provenance
        {
            public InertString Project { get; } = project;
            public InertString? Tfm { get; } = tfm;
            public InertString? Rid { get; } = rid;
        }
        public sealed class LocalAsset(InertString resolverSource) : Provenance
        {
            public InertString ResolverSource { get; } = resolverSource;
        }
        public sealed class DesignatedAsset(InertString resolverSource) : Provenance
        {
            public InertString ResolverSource { get; } = resolverSource;
        }
        public sealed class EmbeddedAsset(InertString contentRef, InertString digest, InertString declaredName) : Provenance
        {
            public InertString ContentRef { get; } = contentRef;
            public InertString Digest { get; } = digest;
            public InertString DeclaredName { get; } = declaredName;
        }
    }

    public abstract class Target
    {
        private Target() { }
        public sealed class AssemblyReference(AssemblyReferenceIdentity identity) : Target
        {
            public AssemblyReferenceIdentity Identity { get; } = identity;
        }
        public sealed class IntrinsicCoreLibrary : Target;
    }

    public abstract class Origin
    {
        private Origin() { }
        public sealed class GlobalOrigin : Origin;
        public sealed class RequestingAssembly(
            Assembly assembly, Occurrence? occurrence, Lineage? lineage, Acquisition registration) : Origin
        {
            public Assembly Assembly { get; } = assembly;
            public Occurrence? Occurrence { get; } = occurrence;
            public Lineage? Lineage { get; } = lineage;
            public Acquisition Registration { get; } = registration;
        }
    }

    public abstract class Start
    {
        private Start() { }
        public sealed class Assembly(
            WorkspaceMetadataEvidence.Assembly value, Occurrence? occurrence, AssemblyResolutionScope scope) : Start
        {
            public WorkspaceMetadataEvidence.Assembly Value { get; } = value;
            public Occurrence? Occurrence { get; } = occurrence;
            public AssemblyResolutionScope Scope { get; } = scope;
        }
        public sealed class Reference(
            AssemblyReferenceIdentity value, Origin origin, AssemblyResolutionScope scope) : Start
        {
            public AssemblyReferenceIdentity Value { get; } = value;
            public Origin Origin { get; } = origin;
            public AssemblyResolutionScope Scope { get; } = scope;
        }
        public sealed class CoreLibrary(Origin.RequestingAssembly origin, AssemblyResolutionScope scope) : Start
        {
            public Origin.RequestingAssembly Origin { get; } = origin;
            public AssemblyResolutionScope Scope { get; } = scope;
        }
        public sealed class Module(InertString name, Origin.RequestingAssembly origin) : Start
        {
            public InertString Name { get; } = name;
            public Origin.RequestingAssembly Origin { get; } = origin;
        }
    }

    public abstract class PlanRequest
    {
        private PlanRequest() { }
        public sealed class Type(TypeRequest request) : PlanRequest
        {
            public TypeRequest Request { get; } = request;
        }
        public sealed class Binding(BindingRequest request) : PlanRequest
        {
            public BindingRequest Request { get; } = request;
        }
    }

    public abstract class Declaration
    {
        private Declaration() { }
        public sealed class Definition(
            DefinitionToken token, MetadataTypeDefinitionKind kind, bool isInterface, bool isValueType) : Declaration
        {
            public DefinitionToken Token { get; } = token;
            public MetadataTypeDefinitionKind Kind { get; } = kind;
            public bool IsInterface { get; } = isInterface;
            public bool IsValueType { get; } = isValueType;
        }
        public sealed class Forwarder(ImmutableArray<ExportToken> declarations, AssemblyReferenceIdentity target) : Declaration
        {
            public ImmutableArray<ExportToken> Declarations { get; } = declarations;
            public AssemblyReferenceIdentity Target { get; } = target;
        }
        public sealed class ModuleExport(ImmutableArray<ExportToken> declarations, Module module) : Declaration
        {
            public ImmutableArray<ExportToken> Declarations { get; } = declarations;
            public Module Module { get; } = module;
        }
    }

    public abstract class Ambiguity
    {
        private Ambiguity() { }
        public sealed class AssemblyBinding(
            Target target, Origin origin, AssemblyResolutionScope scope, ImmutableArray<Candidate> candidates) : Ambiguity
        {
            public Target Target { get; } = target;
            public Origin Origin { get; } = origin;
            public AssemblyResolutionScope Scope { get; } = scope;
            public ImmutableArray<Candidate> Candidates { get; } = candidates;
        }
        public sealed class TypeDeclaration(
            Candidate assembly, Occurrence occurrence, TypeName type, ImmutableArray<Declaration> candidates) : Ambiguity
        {
            public Candidate Assembly { get; } = assembly;
            public Occurrence Occurrence { get; } = occurrence;
            public TypeName Type { get; } = type;
            public ImmutableArray<Declaration> Candidates { get; } = candidates;
        }
    }

    public abstract class Failure
    {
        private Failure() { }
        public sealed class DeclarationRejected(NameFailure rejection) : Failure
        {
            public NameFailure Rejection { get; } = rejection;
        }
        public sealed class ForwarderCycle : Failure;
        public sealed class HopBudgetExceeded(int budget) : Failure
        {
            public int Budget { get; } = budget;
        }
        public sealed class RequestBudgetExceeded(int budget) : Failure
        {
            public int Budget { get; } = budget;
        }
        public sealed class UnsupportedModuleExport(Module module) : Failure
        {
            public Module Module { get; } = module;
        }
        public sealed class UnsupportedModuleReference(InertString moduleName) : Failure
        {
            public InertString ModuleName { get; } = moduleName;
        }
        public sealed class UnregisteredAssembly(Acquisition registration) : Failure
        {
            public Acquisition Registration { get; } = registration;
        }
        public sealed class InvalidBindingPolicy(BindingFailure failure) : WorkspaceMetadataEvidence.Failure
        {
            public BindingFailure Failure { get; } = failure;
        }
        public sealed class CandidateOpenFailed(Assembly assembly, CandidateFailure failure) : WorkspaceMetadataEvidence.Failure
        {
            public Assembly Assembly { get; } = assembly;
            public CandidateFailure Failure { get; } = failure;
        }
        public sealed class KindDependencyUnbound(Target target, Origin origin, AssemblyResolutionScope scope) : Failure
        {
            public Target Target { get; } = target;
            public Origin Origin { get; } = origin;
            public AssemblyResolutionScope Scope { get; } = scope;
        }
        public sealed class KindDependencyUnavailable(
            Target target, Origin origin, AssemblyResolutionScope scope, BindingFailure failure) : WorkspaceMetadataEvidence.Failure
        {
            public Target Target { get; } = target;
            public Origin Origin { get; } = origin;
            public AssemblyResolutionScope Scope { get; } = scope;
            public BindingFailure Failure { get; } = failure;
        }
        public sealed class KindDependencyCycle(Target target, Origin origin, AssemblyResolutionScope scope) : Failure
        {
            public Target Target { get; } = target;
            public Origin Origin { get; } = origin;
            public AssemblyResolutionScope Scope { get; } = scope;
        }
        public sealed class KindDependencyTypeNotFound(Candidate assembly, Occurrence occurrence, TypeName type) : Failure
        {
            public Candidate Assembly { get; } = assembly;
            public Occurrence Occurrence { get; } = occurrence;
            public TypeName Type { get; } = type;
        }
        public sealed class KindDependencyAmbiguous(Ambiguity ambiguity, TypeName type) : Failure
        {
            public Ambiguity Ambiguity { get; } = ambiguity;
            public TypeName Type { get; } = type;
        }
        public sealed class DiscoveryBudgetExceeded(int budget) : Failure
        {
            public int Budget { get; } = budget;
        }
        public sealed class PlanExpansionRequired(PlanRequest request) : Failure
        {
            public PlanRequest Request { get; } = request;
        }
    }

    public abstract class Outcome
    {
        private Outcome(
            ImmutableArray<Hop> hops,
            Occurrence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
        {
            Hops = hops;
            TerminalOccurrence = terminalOccurrence;
            TerminalAssemblyIdentity = terminalAssemblyIdentity;
        }

        public ImmutableArray<Hop> Hops { get; }
        public Occurrence? TerminalOccurrence { get; }
        public AssemblyReferenceIdentity? TerminalAssemblyIdentity { get; }

        public sealed class Resolved : Outcome
        {
            internal Resolved(
                Definition definition, ImmutableArray<Hop> hops, Occurrence? occurrence,
                AssemblyReferenceIdentity? identity) : base(hops, occurrence, identity) => Definition = definition;
            public Definition Definition { get; }
        }

        public sealed class NotFound : Outcome
        {
            internal NotFound(
                Candidate lastAssembly, Occurrence lastOccurrence, ImmutableArray<Hop> hops,
                Occurrence? occurrence, AssemblyReferenceIdentity? identity) : base(hops, occurrence, identity)
            {
                LastAssembly = lastAssembly;
                LastOccurrence = lastOccurrence;
            }
            public Candidate LastAssembly { get; }
            public Occurrence LastOccurrence { get; }
        }

        public sealed class UnboundBinding : Outcome
        {
            internal UnboundBinding(
                WorkspaceUnresolvedBindingId binding, Target target, Origin origin, AssemblyResolutionScope scope,
                ImmutableArray<Hop> hops, Occurrence? occurrence, AssemblyReferenceIdentity? identity)
                : base(hops, occurrence, identity)
            {
                Binding = binding;
                Target = target;
                Origin = origin;
                Scope = scope;
            }
            public WorkspaceUnresolvedBindingId Binding { get; }
            public Target Target { get; }
            public Origin Origin { get; }
            public AssemblyResolutionScope Scope { get; }
        }

        public sealed class Unavailable : Outcome
        {
            internal Unavailable(
                WorkspaceUnresolvedBindingId binding, Target target, Origin origin, AssemblyResolutionScope scope,
                BindingFailure failure, ImmutableArray<Hop> hops, Occurrence? occurrence,
                AssemblyReferenceIdentity? identity) : base(hops, occurrence, identity)
            {
                Binding = binding;
                Target = target;
                Origin = origin;
                Scope = scope;
                Failure = failure;
            }
            public WorkspaceUnresolvedBindingId Binding { get; }
            public Target Target { get; }
            public Origin Origin { get; }
            public AssemblyResolutionScope Scope { get; }
            public BindingFailure Failure { get; }
        }

        public sealed class Ambiguous : Outcome
        {
            internal Ambiguous(
                Ambiguity ambiguity, ImmutableArray<Hop> hops, Occurrence? occurrence,
                AssemblyReferenceIdentity? identity) : base(hops, occurrence, identity) => Ambiguity = ambiguity;
            public Ambiguity Ambiguity { get; }
        }

        public sealed class Rejected : Outcome
        {
            internal Rejected(
                Failure failure, ImmutableArray<Hop> hops, Occurrence? occurrence,
                AssemblyReferenceIdentity? identity) : base(hops, occurrence, identity) => Failure = failure;
            public Failure Failure { get; }
        }
    }
}
