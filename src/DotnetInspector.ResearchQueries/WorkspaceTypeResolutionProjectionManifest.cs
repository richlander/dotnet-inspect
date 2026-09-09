using System.Collections.Immutable;
using System.Security.Cryptography;

using DotnetInspector.Artifacts;
using ILInspector.Metadata;
using ILInspector.Research;
using InertText;

using E = DotnetInspector.Queries.WorkspaceMetadataEvidence;

namespace DotnetInspector.Queries;

internal enum WorkspaceProjectionDisposition
{
    Copy,
    Project,
    InertText,
    OpaqueIdentity,
    Derive,
    Contain,
    Exclude,
}

internal sealed record WorkspaceProjectionProperty(
    string? Source,
    string? Destination,
    WorkspaceProjectionDisposition Disposition,
    string Rule);

internal sealed record WorkspaceProjectionIdentity(
    Type Source,
    Type Destination,
    string EqualityRule,
    string ParentProperty);

/// <summary>
/// A typed materializer is also its fidelity schema. Tests can discover its source,
/// destination and property dispositions without a second list of handled types.
/// Type tokens below are descriptive only; product execution never reflects.
/// </summary>
internal abstract class WorkspaceProjectionSchema(
    string name,
    Type source,
    Type destination,
    ImmutableArray<WorkspaceProjectionProperty> properties)
{
    internal string Name { get; } = name;
    internal Type Source { get; } = source;
    internal Type Destination { get; } = destination;
    internal virtual ImmutableArray<WorkspaceProjectionProperty> Properties { get; } = properties;
    internal virtual ImmutableArray<WorkspaceProjectionSchema> Arms => [];
}

internal sealed class WorkspaceProjection<TSource, TDestination>(
    string name,
    Func<WorkspaceProjectionContext, TSource, TDestination> materialize,
    params ImmutableArray<WorkspaceProjectionProperty> properties)
    : WorkspaceProjectionSchema(name, typeof(TSource), typeof(TDestination), properties)
{
    internal TDestination Project(WorkspaceProjectionContext context, TSource source)
        => materialize(context, source);
}

internal abstract class WorkspaceProjectionArm<TSource, TDestination>(
    string name, Type source, Type destination, ImmutableArray<WorkspaceProjectionProperty> properties)
    : WorkspaceProjectionSchema(name, source, destination, properties)
{
    internal abstract bool TryProject(
        WorkspaceProjectionContext context, TSource source, out TDestination destination);
}

internal sealed class WorkspaceProjectionArm<TSource, TDestination, TArm, TProjected>(
    Func<WorkspaceProjectionContext, TArm, TProjected> materialize,
    ImmutableArray<WorkspaceProjectionProperty> properties)
    : WorkspaceProjectionArm<TSource, TDestination>(
        "Arm", typeof(TArm), typeof(TProjected), properties)
    where TArm : TSource
    where TProjected : TDestination
{
    internal override bool TryProject(
        WorkspaceProjectionContext context, TSource source, out TDestination destination)
    {
        if (source is TArm arm)
        {
            destination = materialize(context, arm);
            return true;
        }
        destination = default!;
        return false;
    }
}

internal sealed class WorkspaceProjectionUnion<TSource, TDestination>(
    string name,
    params ImmutableArray<WorkspaceProjectionArm<TSource, TDestination>> arms)
    : WorkspaceProjectionSchema(name, typeof(TSource), typeof(TDestination), [])
{
    internal ImmutableArray<WorkspaceProjectionProperty> BaseProperties { get; init; } = [];
    internal override ImmutableArray<WorkspaceProjectionProperty> Properties => BaseProperties;
    internal override ImmutableArray<WorkspaceProjectionSchema> Arms => [.. arms];

    internal TDestination Project(WorkspaceProjectionContext context, TSource source)
    {
        foreach (var arm in arms)
            if (arm.TryProject(context, source, out TDestination result))
                return result;
        throw new InvalidOperationException($"Unrecognized {Name} evidence arm.");
    }
}

/// <summary>Ephemeral owner handles; this object never enters a published result.</summary>
internal sealed class WorkspaceProjectionContext(
    IReadOnlyDictionary<AssemblyAcquisitionRegistration, QueryComparisonInputId> inputs)
{
    readonly Dictionary<AssemblyAcquisitionRegistration, WorkspaceAcquisitionOccurrenceId> _acquisitions =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<ArtifactAcquisitionRegistration, WorkspaceArtifactOccurrenceId> _artifacts =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<AssemblyBindingLineage, WorkspaceBindingLineageId> _lineages = new();
    readonly Dictionary<AssemblyBindingPolicyVersion, WorkspaceBindingPolicyVersionId> _versions =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<AssemblyCatalogId, WorkspaceCatalogId> _catalogs = new();
    readonly Dictionary<ResolvedTypeDefinitionKey, WorkspaceDefinitionId> _definitions =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<UnresolvedBindingReference, WorkspaceUnresolvedBindingId> _bindings =
        new(ReferenceEqualityComparer.Instance);

    internal WorkspaceProjectionOperationId Operation { get; } = new();

    internal QueryComparisonInputId? Input(AssemblyAcquisitionRegistration value)
        => inputs.TryGetValue(value, out var input) ? input : null;

    internal WorkspaceAcquisitionOccurrenceId Acquisition(AssemblyAcquisitionRegistration value)
        => Intern(_acquisitions, value, () => new(Operation));
    internal WorkspaceArtifactOccurrenceId Artifact(ArtifactAcquisitionRegistration value)
        => Intern(_artifacts, value, () => new(Operation));
    internal WorkspaceBindingLineageId Lineage(AssemblyBindingLineage value)
        => Intern(_lineages, value, () => new(Operation));
    internal WorkspaceBindingPolicyVersionId Version(AssemblyBindingPolicyVersion value)
        => Intern(_versions, value, () => new(Operation));
    internal WorkspaceCatalogId Catalog(AssemblyCatalogId value)
        => Intern(_catalogs, value, () => new(Operation));
    internal WorkspaceDefinitionId Definition(ResolvedTypeDefinitionKey value)
        => Intern(_definitions, value, () => new(Operation));
    internal WorkspaceUnresolvedBindingId Binding(UnresolvedBindingReference value)
        => Intern(_bindings, value, () => new(Operation));

    static TValue Intern<TKey, TValue>(Dictionary<TKey, TValue> map, TKey key, Func<TValue> create)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out TValue? value))
            map.Add(key, value = create());
        return value;
    }
}

internal static class WorkspaceTypeResolutionProjectionManifest
{
    internal const string OpenReadDenyRule = "ResolvedAssemblyReference.OpenRead: declared delegate; never read";
    internal const string ModuleHashRule = "ModuleFileReference.Hash: byte length and SHA-256 hexadecimal only";
    internal const string TextRule = "Lossless InertString(TextPolicy.Field)";
    internal const string ReferenceIdentityRule = "Operation-local exact reference identity";
    internal const string LineageIdentityRule = "Operation-local AssemblyBindingLineage semantic equality";
    internal const string CatalogIdentityRule = "Operation-local AssemblyCatalogId equality";

    static WorkspaceProjectionProperty Copy(string name) => new(name, name, WorkspaceProjectionDisposition.Copy, "Exact");
    static WorkspaceProjectionProperty Text(string name) => new(name, name, WorkspaceProjectionDisposition.InertText, TextRule);
    static WorkspaceProjectionProperty Project(string name, string projector)
        => new(name, name, WorkspaceProjectionDisposition.Project, projector);
    static WorkspaceProjectionProperty Derive(string destination, string rule)
        => new(null, destination, WorkspaceProjectionDisposition.Derive, rule);

    internal static InertString Inert(string value) => new(TextPolicy.Field, value);
    internal static InertString? InertOptional(string? value) => value is null ? null : Inert(value);

    internal static ImmutableArray<TResult> Map<TSource, TResult>(
        ImmutableArray<TSource> source, Func<TSource, TResult> project)
        => source.IsDefault ? default : [.. source.Select(project)];

    internal static readonly WorkspaceProjection<AssemblyReferenceIdentity, AssemblyReferenceIdentity> Identity =
        new(nameof(Identity), static (_, value) => value,
            Copy("Name"), Copy("Version"), Copy("Culture"), Copy("PublicKeyToken"));

    internal static readonly WorkspaceProjection<ArtifactAcquisitionRegistration, WorkspaceArtifactOccurrenceId> Artifact =
        new(nameof(Artifact), static (c, v) => c.Artifact(v), Derive("Operation", ReferenceIdentityRule));
    internal static readonly WorkspaceProjection<AssemblyBindingPolicyVersion, WorkspaceBindingPolicyVersionId> PolicyVersion =
        new(nameof(PolicyVersion), static (c, v) => c.Version(v), Derive("Operation", ReferenceIdentityRule));
    internal static readonly WorkspaceProjection<AssemblyCatalogId, WorkspaceCatalogId> Catalog =
        new(nameof(Catalog), static (c, v) => c.Catalog(v),
            new WorkspaceProjectionProperty("Value", "Operation", WorkspaceProjectionDisposition.OpaqueIdentity, CatalogIdentityRule));
    internal static readonly WorkspaceProjection<UnresolvedBindingReference, WorkspaceUnresolvedBindingId> Binding =
        new(nameof(Binding), static (c, v) => c.Binding(v), Derive("Operation", ReferenceIdentityRule));

    internal static readonly WorkspaceProjection<AssemblyAcquisitionRegistration, E.Acquisition> Acquisition =
        new(nameof(Acquisition), static (c, v) => new(
            c.Acquisition(v),
            v.ArtifactRegistration is { } artifact ? Artifact.Project(c, artifact) : null,
            v.ModuleVersionId, c.Input(v)),
            Derive("Id", ReferenceIdentityRule),
            Project("ArtifactRegistration", nameof(Artifact)), Copy("ModuleVersionId"),
            Derive("Input", "Exact side-local sealed acquisition-registration correspondence, or null"));

    internal static readonly WorkspaceProjection<AssemblyBindingLineage, E.Lineage> Lineage =
        new(nameof(Lineage), static (c, v) => new(
            c.Lineage(v), v.Version is { } version ? PolicyVersion.Project(c, version) : null),
            Derive("Id", LineageIdentityRule), Project("Version", nameof(PolicyVersion)));

    internal static readonly WorkspaceProjection<ResolvedTypeDefinitionKey, E.DefinitionKey> DefinitionKey =
        new(nameof(DefinitionKey), static (c, v) => new(c.Definition(v), Catalog.Project(c, v.Catalog)),
            Derive("Id", ReferenceIdentityRule), Project("Catalog", nameof(Catalog)));

    internal static readonly WorkspaceProjection<ResolvedAssemblyReference, E.Assembly> Assembly =
        new(nameof(Assembly), static (c, v) => new(
            Acquisition.Project(c, v.Registration), Identity.Project(c, v.Identity), InertOptional(v.Path),
            Provenance!.Project(c, v.Provenance), v.LastWriteTimeUtc),
            Project("Registration", nameof(Acquisition)), Project("Identity", nameof(Identity)), Text("Path"),
            Project("Provenance", nameof(Provenance)), Copy("LastWriteTimeUtc"),
            new("OpenRead", null, WorkspaceProjectionDisposition.Exclude, OpenReadDenyRule));

    internal static readonly WorkspaceProjection<ResolvedAssemblyCandidate, E.Candidate> Candidate =
        new(nameof(Candidate), static (c, v) => new(Assembly.Project(c, v.Assembly)),
            Project("Assembly", nameof(Assembly)));
    internal static readonly WorkspaceProjection<AssemblyBindingOccurrence, E.Occurrence> Occurrence =
        new(nameof(Occurrence), static (c, v) => new(Assembly.Project(c, v.Assembly), Lineage.Project(c, v.Lineage)),
            Project("Assembly", nameof(Assembly)), Project("Lineage", nameof(Lineage)));
    internal static readonly WorkspaceProjection<MetadataTypeDefinitionName, E.TypeName> TypeName =
        new(nameof(TypeName), static (_, v) => new(Inert(v.Namespace), Map(v.Segments, Inert)),
            Text("Namespace"), Text("Segments"));
    internal static readonly WorkspaceProjection<TypeDefinitionToken, E.DefinitionToken> DefinitionToken =
        new(nameof(DefinitionToken), static (_, v) => new(v.Value), Copy("Value"));
    internal static readonly WorkspaceProjection<ExportedTypeToken, E.ExportToken> ExportToken =
        new(nameof(ExportToken), static (_, v) => new(v.Value), Copy("Value"));
    internal static readonly WorkspaceProjection<MetadataTypeDefinitionAddress, E.DefinitionAddress> Address =
        new(nameof(Address), static (c, v) => new(v.ModuleVersionId, DefinitionToken.Project(c, v.Definition)),
            Copy("ModuleVersionId"), Project("Definition", nameof(DefinitionToken)));

    internal static readonly WorkspaceProjection<ImmutableArray<byte>, E.HashSummary> ModuleHash =
        new(nameof(ModuleHash), static (_, v) => new(v.IsDefault ? 0 : v.Length,
            Inert(Convert.ToHexString(SHA256.HashData(v.AsSpan())))),
            Derive("ByteLength", ModuleHashRule), Derive("Sha256", ModuleHashRule));
    internal static readonly WorkspaceProjection<ModuleFileReference, E.Module> Module =
        new(nameof(Module), static (c, v) => new(Inert(v.Name), v.ContainsMetadata, ModuleHash.Project(c, v.Hash)),
            Text("Name"), Copy("ContainsMetadata"),
            new("Hash", "Hash", WorkspaceProjectionDisposition.Contain, nameof(ModuleHash)));
    internal static readonly WorkspaceProjection<AssemblyBindingFailure, E.BindingFailure> BindingFailure =
        new(nameof(BindingFailure), static (_, v) => new(v.Kind, v.CandidateFailureKind, v.MetadataRootReason),
            Copy("Kind"), Copy("CandidateFailureKind"), Copy("MetadataRootReason"));
    internal static readonly WorkspaceProjection<CandidateOpenFailure, E.CandidateFailure> CandidateFailure =
        new(nameof(CandidateFailure), static (_, v) => new(v.Kind, Inert(v.Detail), v.MetadataRootReason),
            Copy("Kind"), Text("Detail"), Copy("MetadataRootReason"));
    internal static readonly WorkspaceProjection<MetadataTypeNameFailure, E.NameFailure> NameFailure =
        new(nameof(NameFailure), static (_, v) => new(
            v.Mechanism, Inert(v.Detail), v.SubjectToken, v.ConsumedNodes,
            v.RelationshipKind, v.SignatureKind, Inert(v.Kind)),
            Copy("Mechanism"), Text("Detail"), Copy("SubjectToken"), Copy("ConsumedNodes"),
            Copy("RelationshipKind"), Copy("SignatureKind"), Text("Kind"));
    internal static readonly WorkspaceProjection<ResolvedTypeDefinition, E.Definition> Definition =
        new(nameof(Definition), static (c, v) => new(
            DefinitionKey.Project(c, v.Key), Address.Project(c, v.Address), Candidate.Project(c, v.Assembly),
            Occurrence.Project(c, v.Occurrence), TypeName.Project(c, v.Type), v.Kind,
            v.IsInterface, v.IsValueType, v.DeclaringAssemblyDefinesCoreLibraryRoot),
            Project("Key", nameof(DefinitionKey)), Project("Address", nameof(Address)),
            Project("Assembly", nameof(Candidate)), Project("Occurrence", nameof(Occurrence)),
            Project("Type", nameof(TypeName)), Copy("Kind"), Copy("IsInterface"), Copy("IsValueType"),
            Copy("DeclaringAssemblyDefinesCoreLibraryRoot"));
    internal static readonly WorkspaceProjection<TypeForwardingHop, E.Hop> Hop =
        new(nameof(Hop), static (c, v) => new(
            Candidate.Project(c, v.SourceAssembly), Occurrence.Project(c, v.SourceOccurrence),
            Map(v.Declarations, token => ExportToken.Project(c, token)),
            Identity.Project(c, v.TargetReference), v.Scope),
            Project("SourceAssembly", nameof(Candidate)), Project("SourceOccurrence", nameof(Occurrence)),
            Project("Declarations", nameof(ExportToken)), Project("TargetReference", nameof(Identity)), Copy("Scope"));
    internal static readonly WorkspaceProjection<TypeResolutionRequest, E.TypeRequest> TypeRequest =
        new(nameof(TypeRequest), static (c, v) => new(Start!.Project(c, v.Start), TypeName.Project(c, v.Type)),
            Project("Start", nameof(Start)), Project("Type", nameof(TypeName)));
    internal static readonly WorkspaceProjection<AssemblyBindingRequest, E.BindingRequest> BindingRequest =
        new(nameof(BindingRequest), static (c, v) => new(Target!.Project(c, v.Target), Origin!.Project(c, v.Origin), v.Scope),
            Project("Target", nameof(Target)), Project("Origin", nameof(Origin)), Copy("Scope"));

    internal static readonly WorkspaceProjectionUnion<AssemblyResolutionProvenance, E.Provenance> Provenance =
        new(nameof(Provenance),
            new WorkspaceProjectionArm<AssemblyResolutionProvenance, E.Provenance,
                AssemblyResolutionProvenance.PackageAsset, E.Provenance.PackageAsset>(
                static (_, v) => new(Inert(v.PackageId), Inert(v.PackageVersion), InertOptional(v.Tfm), InertOptional(v.Rid)),
                [Text("PackageId"), Text("PackageVersion"), Text("Tfm"), Text("Rid")]),
            new WorkspaceProjectionArm<AssemblyResolutionProvenance, E.Provenance,
                AssemblyResolutionProvenance.PlatformAsset, E.Provenance.PlatformAsset>(
                static (_, v) => new(Inert(v.Framework), InertOptional(v.FrameworkVersion), Inert(v.ResolverSource)),
                [Text("Framework"), Text("FrameworkVersion"), Text("ResolverSource")]),
            new WorkspaceProjectionArm<AssemblyResolutionProvenance, E.Provenance,
                AssemblyResolutionProvenance.ProjectAsset, E.Provenance.ProjectAsset>(
                static (_, v) => new(Inert(v.Project), InertOptional(v.Tfm), InertOptional(v.Rid)),
                [Text("Project"), Text("Tfm"), Text("Rid")]),
            new WorkspaceProjectionArm<AssemblyResolutionProvenance, E.Provenance,
                AssemblyResolutionProvenance.LocalAsset, E.Provenance.LocalAsset>(
                static (_, v) => new(Inert(v.ResolverSource)), [Text("ResolverSource")]),
            new WorkspaceProjectionArm<AssemblyResolutionProvenance, E.Provenance,
                AssemblyResolutionProvenance.DesignatedAsset, E.Provenance.DesignatedAsset>(
                static (_, v) => new(Inert(v.ResolverSource)), [Text("ResolverSource")]),
            new WorkspaceProjectionArm<AssemblyResolutionProvenance, E.Provenance,
                AssemblyResolutionProvenance.EmbeddedAsset, E.Provenance.EmbeddedAsset>(
                static (_, v) => new(Inert(v.ContentRef), Inert(v.Digest), Inert(v.DeclaredName)),
                [Text("ContentRef"), Text("Digest"), Text("DeclaredName")]));

    internal static readonly WorkspaceProjectionUnion<AssemblyBindingTarget, E.Target> Target =
        new(nameof(Target),
            new WorkspaceProjectionArm<AssemblyBindingTarget, E.Target,
                AssemblyBindingTarget.AssemblyReference, E.Target.AssemblyReference>(
                static (c, v) => new(Identity.Project(c, v.Identity)), [Project("Identity", nameof(Identity))]),
            new WorkspaceProjectionArm<AssemblyBindingTarget, E.Target,
                AssemblyBindingTarget.IntrinsicCoreLibrary, E.Target.IntrinsicCoreLibrary>(
                static (_, _) => new(), []));

    internal static readonly WorkspaceProjectionUnion<AssemblyBindingOrigin, E.Origin> Origin =
        new(nameof(Origin),
            new WorkspaceProjectionArm<AssemblyBindingOrigin, E.Origin,
                AssemblyBindingOrigin.GlobalOrigin, E.Origin.GlobalOrigin>(static (_, _) => new(), []),
            new WorkspaceProjectionArm<AssemblyBindingOrigin, E.Origin,
                AssemblyBindingOrigin.RequestingAssembly, E.Origin.RequestingAssembly>(
                static (c, v) => new(
                    Assembly.Project(c, v.Assembly), OptionalOccurrence(c, v.Occurrence),
                    v.Lineage is { } lineage ? Lineage.Project(c, lineage) : null,
                    Acquisition.Project(c, v.Registration)),
                [Project("Assembly", nameof(Assembly)), Project("Occurrence", nameof(Occurrence)),
                    Project("Lineage", nameof(Lineage)), Project("Registration", nameof(Acquisition))]));

    internal static readonly WorkspaceProjectionUnion<TypeResolutionStart, E.Start> Start =
        new(nameof(Start),
            new WorkspaceProjectionArm<TypeResolutionStart, E.Start,
                TypeResolutionStart.Assembly, E.Start.Assembly>(
                static (c, v) => new(Assembly.Project(c, v.Value), OptionalOccurrence(c, v.Occurrence), v.Scope),
                [Project("Value", nameof(Assembly)), Project("Occurrence", nameof(Occurrence)), Copy("Scope")]),
            new WorkspaceProjectionArm<TypeResolutionStart, E.Start,
                TypeResolutionStart.Reference, E.Start.Reference>(
                static (c, v) => new(Identity.Project(c, v.Value), Origin.Project(c, v.Origin), v.Scope),
                [Project("Value", nameof(Identity)), Project("Origin", nameof(Origin)), Copy("Scope")]),
            new WorkspaceProjectionArm<TypeResolutionStart, E.Start,
                TypeResolutionStart.CoreLibrary, E.Start.CoreLibrary>(
                static (c, v) => new((E.Origin.RequestingAssembly)Origin.Project(c, v.Origin), v.Scope),
                [Project("Origin", nameof(Origin)), Copy("Scope")]),
            new WorkspaceProjectionArm<TypeResolutionStart, E.Start,
                TypeResolutionStart.Module, E.Start.Module>(
                static (c, v) => new(Inert(v.Name), (E.Origin.RequestingAssembly)Origin.Project(c, v.Origin)),
                [Text("Name"), Project("Origin", nameof(Origin))]));

    internal static readonly WorkspaceProjectionUnion<ResolutionPlanRequest, E.PlanRequest> PlanRequest =
        new(nameof(PlanRequest),
            new WorkspaceProjectionArm<ResolutionPlanRequest, E.PlanRequest,
                ResolutionPlanRequest.Type, E.PlanRequest.Type>(
                static (c, v) => new(TypeRequest.Project(c, v.Request)), [Project("Request", nameof(TypeRequest))]),
            new WorkspaceProjectionArm<ResolutionPlanRequest, E.PlanRequest,
                ResolutionPlanRequest.Binding, E.PlanRequest.Binding>(
                static (c, v) => new(BindingRequest.Project(c, v.Request)), [Project("Request", nameof(BindingRequest))]));

    internal static readonly WorkspaceProjectionUnion<TypeDeclarationCandidate, E.Declaration> Declaration =
        new(nameof(Declaration),
            new WorkspaceProjectionArm<TypeDeclarationCandidate, E.Declaration,
                TypeDeclarationCandidate.Definition, E.Declaration.Definition>(
                static (c, v) => new(DefinitionToken.Project(c, v.Token), v.Kind, v.IsInterface, v.IsValueType),
                [Project("Token", nameof(DefinitionToken)), Copy("Kind"), Copy("IsInterface"), Copy("IsValueType")]),
            new WorkspaceProjectionArm<TypeDeclarationCandidate, E.Declaration,
                TypeDeclarationCandidate.Forwarder, E.Declaration.Forwarder>(
                static (c, v) => new(Map(v.Declarations, t => ExportToken.Project(c, t)), Identity.Project(c, v.Target)),
                [Project("Declarations", nameof(ExportToken)), Project("Target", nameof(Identity))]),
            new WorkspaceProjectionArm<TypeDeclarationCandidate, E.Declaration,
                TypeDeclarationCandidate.ModuleExport, E.Declaration.ModuleExport>(
                static (c, v) => new(Map(v.Declarations, t => ExportToken.Project(c, t)), Module.Project(c, v.Module)),
                [Project("Declarations", nameof(ExportToken)), Project("Module", nameof(Module))]));

    internal static readonly WorkspaceProjectionUnion<TypeResolutionAmbiguity, E.Ambiguity> Ambiguity =
        new(nameof(Ambiguity),
            new WorkspaceProjectionArm<TypeResolutionAmbiguity, E.Ambiguity,
                TypeResolutionAmbiguity.AssemblyBinding, E.Ambiguity.AssemblyBinding>(
                static (c, v) => new(Target.Project(c, v.Target), Origin.Project(c, v.Origin), v.Scope,
                    Map(v.Candidates, x => Candidate.Project(c, x))),
                [Project("Target", nameof(Target)), Project("Origin", nameof(Origin)), Copy("Scope"),
                    Project("Candidates", nameof(Candidate))]),
            new WorkspaceProjectionArm<TypeResolutionAmbiguity, E.Ambiguity,
                TypeResolutionAmbiguity.TypeDeclaration, E.Ambiguity.TypeDeclaration>(
                static (c, v) => new(Candidate.Project(c, v.Assembly), Occurrence.Project(c, v.Occurrence),
                    TypeName.Project(c, v.Type), Map(v.Candidates, x => Declaration.Project(c, x))),
                [Project("Assembly", nameof(Candidate)), Project("Occurrence", nameof(Occurrence)),
                    Project("Type", nameof(TypeName)), Project("Candidates", nameof(Declaration))]));

    static WorkspaceProjectionArm<TypeResolutionFailure, E.Failure> FailureArm<TSource, TDestination>(
        Func<WorkspaceProjectionContext, TSource, TDestination> project,
        params ImmutableArray<WorkspaceProjectionProperty> properties)
        where TSource : TypeResolutionFailure
        where TDestination : E.Failure
        => new WorkspaceProjectionArm<TypeResolutionFailure, E.Failure, TSource, TDestination>(project, properties);

    internal static readonly WorkspaceProjectionUnion<TypeResolutionFailure, E.Failure> Failure =
        new(nameof(Failure),
            FailureArm<TypeResolutionFailure.DeclarationRejected, E.Failure.DeclarationRejected>(
                static (c, v) => new(NameFailure.Project(c, v.Rejection)), Project("Rejection", nameof(NameFailure))),
            FailureArm<TypeResolutionFailure.ForwarderCycle, E.Failure.ForwarderCycle>(static (_, _) => new()),
            FailureArm<TypeResolutionFailure.HopBudgetExceeded, E.Failure.HopBudgetExceeded>(
                static (_, v) => new(v.Budget), Copy("Budget")),
            FailureArm<TypeResolutionFailure.RequestBudgetExceeded, E.Failure.RequestBudgetExceeded>(
                static (_, v) => new(v.Budget), Copy("Budget")),
            FailureArm<TypeResolutionFailure.UnsupportedModuleExport, E.Failure.UnsupportedModuleExport>(
                static (c, v) => new(Module.Project(c, v.Module)), Project("Module", nameof(Module))),
            FailureArm<TypeResolutionFailure.UnsupportedModuleReference, E.Failure.UnsupportedModuleReference>(
                static (_, v) => new(Inert(v.ModuleName)), Text("ModuleName")),
            FailureArm<TypeResolutionFailure.UnregisteredAssembly, E.Failure.UnregisteredAssembly>(
                static (c, v) => new(Acquisition.Project(c, v.Registration)), Project("Registration", nameof(Acquisition))),
            FailureArm<TypeResolutionFailure.InvalidBindingPolicy, E.Failure.InvalidBindingPolicy>(
                static (c, v) => new(BindingFailure.Project(c, v.Failure)), Project("Failure", nameof(BindingFailure))),
            FailureArm<TypeResolutionFailure.CandidateOpenFailed, E.Failure.CandidateOpenFailed>(
                static (c, v) => new(Assembly.Project(c, v.Assembly), CandidateFailure.Project(c, v.Failure)),
                Project("Assembly", nameof(Assembly)), Project("Failure", nameof(CandidateFailure))),
            FailureArm<TypeResolutionFailure.KindDependencyUnbound, E.Failure.KindDependencyUnbound>(
                static (c, v) => new(Target.Project(c, v.Target), Origin.Project(c, v.Origin), v.Scope),
                Project("Target", nameof(Target)), Project("Origin", nameof(Origin)), Copy("Scope")),
            FailureArm<TypeResolutionFailure.KindDependencyUnavailable, E.Failure.KindDependencyUnavailable>(
                static (c, v) => new(Target.Project(c, v.Target), Origin.Project(c, v.Origin), v.Scope,
                    BindingFailure.Project(c, v.Failure)),
                Project("Target", nameof(Target)), Project("Origin", nameof(Origin)), Copy("Scope"),
                Project("Failure", nameof(BindingFailure))),
            FailureArm<TypeResolutionFailure.KindDependencyCycle, E.Failure.KindDependencyCycle>(
                static (c, v) => new(Target.Project(c, v.Target), Origin.Project(c, v.Origin), v.Scope),
                Project("Target", nameof(Target)), Project("Origin", nameof(Origin)), Copy("Scope")),
            FailureArm<TypeResolutionFailure.KindDependencyTypeNotFound, E.Failure.KindDependencyTypeNotFound>(
                static (c, v) => new(Candidate.Project(c, v.Assembly), Occurrence.Project(c, v.Occurrence),
                    TypeName.Project(c, v.Type)),
                Project("Assembly", nameof(Candidate)), Project("Occurrence", nameof(Occurrence)),
                Project("Type", nameof(TypeName))),
            FailureArm<TypeResolutionFailure.KindDependencyAmbiguous, E.Failure.KindDependencyAmbiguous>(
                static (c, v) => new(Ambiguity.Project(c, v.Ambiguity), TypeName.Project(c, v.Type)),
                Project("Ambiguity", nameof(Ambiguity)), Project("Type", nameof(TypeName))),
            FailureArm<TypeResolutionFailure.DiscoveryBudgetExceeded, E.Failure.DiscoveryBudgetExceeded>(
                static (_, v) => new(v.Budget), Copy("Budget")),
            FailureArm<TypeResolutionFailure.PlanExpansionRequired, E.Failure.PlanExpansionRequired>(
                static (c, v) => new(PlanRequest.Project(c, v.Request)), Project("Request", nameof(PlanRequest))));

    static WorkspaceProjectionArm<TypeResolutionOutcome, E.Outcome> OutcomeArm<TSource, TDestination>(
        Func<WorkspaceProjectionContext, TSource, TDestination> project,
        params ImmutableArray<WorkspaceProjectionProperty> properties)
        where TSource : TypeResolutionOutcome
        where TDestination : E.Outcome
        => new WorkspaceProjectionArm<TypeResolutionOutcome, E.Outcome, TSource, TDestination>(project,
            [.. properties, Project("Hops", nameof(Hop)), Project("TerminalOccurrence", nameof(Occurrence)),
                Project("TerminalAssemblyIdentity", nameof(Identity))]);

    internal static readonly WorkspaceProjectionUnion<TypeResolutionOutcome, E.Outcome> Outcome =
        new(nameof(Outcome),
            OutcomeArm<TypeResolutionOutcome.Resolved, E.Outcome.Resolved>(
                static (c, v) => new(Definition.Project(c, v.Definition), Hops(c, v),
                    OptionalOccurrence(c, v.TerminalOccurrence), OptionalIdentity(c, v.TerminalAssemblyIdentity)),
                Project("Definition", nameof(Definition))),
            OutcomeArm<TypeResolutionOutcome.NotFound, E.Outcome.NotFound>(
                static (c, v) => new(Candidate.Project(c, v.LastAssembly), Occurrence.Project(c, v.LastOccurrence),
                    Hops(c, v), OptionalOccurrence(c, v.TerminalOccurrence), OptionalIdentity(c, v.TerminalAssemblyIdentity)),
                Project("LastAssembly", nameof(Candidate)), Project("LastOccurrence", nameof(Occurrence))),
            OutcomeArm<TypeResolutionOutcome.UnboundBinding, E.Outcome.UnboundBinding>(
                static (c, v) => new(Binding.Project(c, v.Binding), Target.Project(c, v.Target),
                    Origin.Project(c, v.Origin), v.Scope, Hops(c, v),
                    OptionalOccurrence(c, v.TerminalOccurrence), OptionalIdentity(c, v.TerminalAssemblyIdentity)),
                Project("Binding", nameof(Binding)), Project("Target", nameof(Target)),
                Project("Origin", nameof(Origin)), Copy("Scope")),
            OutcomeArm<TypeResolutionOutcome.Unavailable, E.Outcome.Unavailable>(
                static (c, v) => new(Binding.Project(c, v.Binding), Target.Project(c, v.Target),
                    Origin.Project(c, v.Origin), v.Scope, BindingFailure.Project(c, v.Failure), Hops(c, v),
                    OptionalOccurrence(c, v.TerminalOccurrence), OptionalIdentity(c, v.TerminalAssemblyIdentity)),
                Project("Binding", nameof(Binding)), Project("Target", nameof(Target)),
                Project("Origin", nameof(Origin)), Copy("Scope"), Project("Failure", nameof(BindingFailure))),
            OutcomeArm<TypeResolutionOutcome.Ambiguous, E.Outcome.Ambiguous>(
                static (c, v) => new(Ambiguity.Project(c, v.Ambiguity), Hops(c, v),
                    OptionalOccurrence(c, v.TerminalOccurrence), OptionalIdentity(c, v.TerminalAssemblyIdentity)),
                Project("Ambiguity", nameof(Ambiguity))),
            OutcomeArm<TypeResolutionOutcome.Rejected, E.Outcome.Rejected>(
                static (c, v) => new(Failure.Project(c, v.Failure), Hops(c, v),
                    OptionalOccurrence(c, v.TerminalOccurrence), OptionalIdentity(c, v.TerminalAssemblyIdentity)),
                Project("Failure", nameof(Failure))))
        {
            BaseProperties =
            [
                Project("Hops", nameof(Hop)), Project("TerminalOccurrence", nameof(Occurrence)),
                Project("TerminalAssemblyIdentity", nameof(Identity)),
            ],
        };

    internal static readonly WorkspaceProjectionUnion<AssemblyContextTypeResolutionResult, WorkspaceTypeResolutionEvidence> QueryResult =
        new(nameof(QueryResult),
            new WorkspaceProjectionArm<AssemblyContextTypeResolutionResult, WorkspaceTypeResolutionEvidence,
                AssemblyContextTypeResolutionResult.Available, WorkspaceTypeResolutionEvidence.Available>(
                static (c, v) => new(Outcome.Project(c, v.Outcome)), [Project("Outcome", nameof(Outcome))]),
            new WorkspaceProjectionArm<AssemblyContextTypeResolutionResult, WorkspaceTypeResolutionEvidence,
                AssemblyContextTypeResolutionResult.Rejected, WorkspaceTypeResolutionEvidence.QueryRejected>(
                static (c, v) => new(
                    c.Input(v.Assembly.Registration)
                        ?? throw new InvalidOperationException("The rejected participant has no sealed input."),
                    CandidateFailure.Project(c, v.Failure)),
                [new("Assembly", "Input", WorkspaceProjectionDisposition.Derive,
                    "Exact side-local sealed acquisition-registration correspondence"),
                    Project("Failure", nameof(CandidateFailure))]),
            new WorkspaceProjectionArm<AssemblyContextTypeResolutionResult, WorkspaceTypeResolutionEvidence,
                AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy, WorkspaceTypeResolutionEvidence.UnsupportedBindingPolicy>(
                static (c, v) => new(
                    c.Input(v.Assembly.Registration)
                        ?? throw new InvalidOperationException("The unsupported participant has no sealed input.")),
                [new("Assembly", "Input", WorkspaceProjectionDisposition.Derive,
                    "Exact side-local sealed acquisition-registration correspondence")]));

    static E.Occurrence? OptionalOccurrence(WorkspaceProjectionContext context, AssemblyBindingOccurrence? value)
        => value is null ? null : Occurrence.Project(context, value);
    static AssemblyReferenceIdentity? OptionalIdentity(WorkspaceProjectionContext context, AssemblyReferenceIdentity? value)
        => value is null ? null : Identity.Project(context, value);
    static ImmutableArray<E.Hop> Hops(WorkspaceProjectionContext context, TypeResolutionOutcome value)
        => Map(value.Hops, hop => Hop.Project(context, hop));

    internal static ImmutableArray<WorkspaceProjectionSchema> Materializers { get; } =
    [
        Identity, Artifact, PolicyVersion, Catalog, Binding, Acquisition, Lineage, DefinitionKey, Assembly,
        Candidate, Occurrence, TypeName, DefinitionToken, ExportToken, Address, ModuleHash, Module,
        BindingFailure, CandidateFailure, NameFailure, Definition, Hop, TypeRequest, BindingRequest,
        Provenance, Target, Origin, Start, PlanRequest, Declaration, Ambiguity, Failure, Outcome, QueryResult,
    ];

    // These are owner-authorized identity leaves, not a way to suppress discovery
    // of other Metadata properties or of an additional closed union.
    internal static ImmutableArray<Type> OpaqueOwnerLeaves { get; } =
    [
        typeof(ArtifactAcquisitionRegistration), typeof(AssemblyBindingPolicyVersion),
        typeof(AssemblyBindingLineage), typeof(AssemblyCatalogId),
        typeof(ResolvedTypeDefinitionKey), typeof(UnresolvedBindingReference),
    ];

    internal static ImmutableArray<WorkspaceProjectionIdentity> IdentityDerivations { get; } =
    [
        new(typeof(AssemblyAcquisitionRegistration), typeof(WorkspaceAcquisitionOccurrenceId),
            ReferenceIdentityRule, nameof(WorkspaceAcquisitionOccurrenceId.Operation)),
        new(typeof(AssemblyBindingLineage), typeof(WorkspaceBindingLineageId),
            LineageIdentityRule, nameof(WorkspaceBindingLineageId.Operation)),
        new(typeof(ResolvedTypeDefinitionKey), typeof(WorkspaceDefinitionId),
            ReferenceIdentityRule, nameof(WorkspaceDefinitionId.Operation)),
    ];

    internal static ImmutableArray<Type> PermittedValueLeaves { get; } =
    [
        typeof(bool), typeof(int), typeof(Guid), typeof(DateTime), typeof(Version),
        typeof(string), typeof(InertString),
    ];

    // Permitted identities are still subject to the structural gate's
    // field traversal; this list does not declare their graphs atomic.
    internal static ImmutableArray<Type> RetainedOwnerCurrency { get; } =
    [
        typeof(AssemblyReferenceIdentity), typeof(QueryComparisonOperationId),
        typeof(QueryComparisonQuestionId), typeof(QueryComparisonInputId),
        typeof(ResearchComparisonOperationId), typeof(ResearchComparisonQuestionId),
        typeof(ResearchComparisonInputId), typeof(ResearchTargetScopeId),
        typeof(ResearchTargetDomainId), typeof(ResearchTargetRequestId),
        typeof(ResearchTargetAttemptId),
    ];
}
