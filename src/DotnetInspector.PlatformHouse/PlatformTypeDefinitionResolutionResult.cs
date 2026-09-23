using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

/// <summary>Resource-free evidence for one assembly in type resolution.</summary>
public sealed class PlatformTypeResolutionAssemblyEvidence
{
    internal PlatformTypeResolutionAssemblyEvidence(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity identity,
        Guid? moduleVersionId,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(provenance);
        Registration = registration;
        Identity = identity;
        ModuleVersionId = moduleVersionId;
        Provenance = provenance;
    }

    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public Guid? ModuleVersionId { get; }
    public AssemblyResolutionProvenance Provenance { get; }
}

/// <summary>Opaque detached identity for one binding lineage.</summary>
public sealed class PlatformTypeResolutionLineageIdentity
{
    internal PlatformTypeResolutionLineageIdentity()
    {
    }
}

/// <summary>Opaque detached identity for one unresolved binding.</summary>
public sealed class PlatformTypeResolutionUnresolvedBindingIdentity
{
    internal PlatformTypeResolutionUnresolvedBindingIdentity()
    {
    }
}

/// <summary>Resource-free evidence for one selected assembly occurrence.</summary>
public sealed class PlatformTypeResolutionOccurrenceEvidence
{
    internal PlatformTypeResolutionOccurrenceEvidence(
        PlatformTypeResolutionAssemblyEvidence assembly,
        PlatformTypeResolutionLineageIdentity lineage)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(lineage);
        Assembly = assembly;
        Lineage = lineage;
    }

    public PlatformTypeResolutionAssemblyEvidence Assembly { get; }
    public PlatformTypeResolutionLineageIdentity Lineage { get; }
}

/// <summary>Resource-free evidence for one resolution candidate.</summary>
public sealed class PlatformTypeResolutionCandidateEvidence
{
    internal PlatformTypeResolutionCandidateEvidence(
        PlatformTypeResolutionAssemblyEvidence assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Assembly = assembly;
    }

    public PlatformTypeResolutionAssemblyEvidence Assembly { get; }
}

/// <summary>Resource-free evidence for one resolved physical TypeDef.</summary>
public sealed class PlatformTypeResolutionDefinitionEvidence
{
    internal PlatformTypeResolutionDefinitionEvidence(
        MetadataTypeDefinitionAddress address,
        PlatformTypeResolutionCandidateEvidence assembly,
        PlatformTypeResolutionOccurrenceEvidence occurrence,
        MetadataTypeDefinitionName type,
        MetadataTypeDefinitionKind kind,
        bool declaringAssemblyDefinesCoreLibraryRoot,
        PlatformTypeResolutionFailureEvidence? kindResolutionFailure,
        AssemblyReferenceIdentity? kindResolutionDependencyAssembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(type);
        Address = address;
        Assembly = assembly;
        Occurrence = occurrence;
        Type = type;
        Kind = kind;
        DeclaringAssemblyDefinesCoreLibraryRoot =
            declaringAssemblyDefinesCoreLibraryRoot;
        KindResolutionFailure = kindResolutionFailure;
        KindResolutionDependencyAssembly =
            kindResolutionDependencyAssembly;
    }

    public MetadataTypeDefinitionAddress Address { get; }
    public PlatformTypeResolutionCandidateEvidence Assembly { get; }
    public PlatformTypeResolutionOccurrenceEvidence Occurrence { get; }
    public MetadataTypeDefinitionName Type { get; }
    public MetadataTypeDefinitionKind Kind { get; }
    public bool DeclaringAssemblyDefinesCoreLibraryRoot { get; }
    public PlatformTypeResolutionFailureEvidence? KindResolutionFailure
    {
        get;
    }
    public AssemblyReferenceIdentity? KindResolutionDependencyAssembly
    {
        get;
    }
}

/// <summary>Resource-free evidence for one exact forwarding hop.</summary>
public sealed class PlatformTypeForwardingHopEvidence
{
    internal PlatformTypeForwardingHopEvidence(
        PlatformTypeResolutionCandidateEvidence sourceAssembly,
        PlatformTypeResolutionOccurrenceEvidence sourceOccurrence,
        ImmutableArray<ExportedTypeToken> declarations,
        AssemblyReferenceIdentity targetReference,
        AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);
        ArgumentNullException.ThrowIfNull(sourceOccurrence);
        ArgumentNullException.ThrowIfNull(targetReference);
        SourceAssembly = sourceAssembly;
        SourceOccurrence = sourceOccurrence;
        Declarations = declarations;
        TargetReference = targetReference;
        Scope = scope;
    }

    public PlatformTypeResolutionCandidateEvidence SourceAssembly { get; }
    public PlatformTypeResolutionOccurrenceEvidence SourceOccurrence { get; }
    public ImmutableArray<ExportedTypeToken> Declarations { get; }
    public AssemblyReferenceIdentity TargetReference { get; }
    public AssemblyResolutionScope Scope { get; }
}

/// <summary>A resource-free binding origin used by type resolution.</summary>
public abstract class PlatformTypeResolutionBindingOriginEvidence
{
    private protected PlatformTypeResolutionBindingOriginEvidence()
    {
    }

    public sealed class Global :
        PlatformTypeResolutionBindingOriginEvidence
    {
        internal Global()
        {
        }
    }

    public sealed class RequestingAssembly :
        PlatformTypeResolutionBindingOriginEvidence
    {
        internal RequestingAssembly(
            PlatformTypeResolutionAssemblyEvidence assembly,
            PlatformTypeResolutionOccurrenceEvidence? occurrence,
            PlatformTypeResolutionLineageIdentity? lineage)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            Assembly = assembly;
            Occurrence = occurrence;
            Lineage = lineage;
        }

        public PlatformTypeResolutionAssemblyEvidence Assembly { get; }
        public PlatformTypeResolutionOccurrenceEvidence? Occurrence { get; }
        public PlatformTypeResolutionLineageIdentity? Lineage { get; }
    }
}

/// <summary>Resource-free evidence for one competing declaration.</summary>
public abstract class PlatformTypeDeclarationCandidateEvidence
{
    private protected PlatformTypeDeclarationCandidateEvidence()
    {
    }

    public sealed class Definition : PlatformTypeDeclarationCandidateEvidence
    {
        internal Definition(
            TypeDefinitionToken token,
            MetadataTypeDefinitionKind kind,
            bool isInterface,
            bool isValueType,
            MetadataTypeDefinitionKindFailure? kindFailure)
        {
            Token = token;
            Kind = kind;
            IsInterface = isInterface;
            IsValueType = isValueType;
            KindFailure = kindFailure;
        }

        public TypeDefinitionToken Token { get; }
        public MetadataTypeDefinitionKind Kind { get; }
        public bool IsInterface { get; }
        public bool IsValueType { get; }
        public MetadataTypeDefinitionKindFailure? KindFailure { get; }
    }

    public sealed class Forwarder : PlatformTypeDeclarationCandidateEvidence
    {
        internal Forwarder(
            ImmutableArray<ExportedTypeToken> declarations,
            AssemblyReferenceIdentity target)
        {
            ArgumentNullException.ThrowIfNull(target);
            Declarations = declarations;
            Target = target;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public AssemblyReferenceIdentity Target { get; }
    }

    public sealed class ModuleExport :
        PlatformTypeDeclarationCandidateEvidence
    {
        internal ModuleExport(
            ImmutableArray<ExportedTypeToken> declarations,
            ModuleFileReference module)
        {
            ArgumentNullException.ThrowIfNull(module);
            Declarations = declarations;
            Module = module;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public ModuleFileReference Module { get; }
    }
}

/// <summary>Resource-free ambiguity evidence from type resolution.</summary>
public abstract class PlatformTypeResolutionAmbiguityEvidence
{
    private protected PlatformTypeResolutionAmbiguityEvidence()
    {
    }

    public sealed class AssemblyBinding :
        PlatformTypeResolutionAmbiguityEvidence
    {
        internal AssemblyBinding(
            AssemblyBindingTarget target,
            PlatformTypeResolutionBindingOriginEvidence origin,
            AssemblyResolutionScope scope,
            ImmutableArray<PlatformTypeResolutionCandidateEvidence> candidates)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(origin);
            Target = target;
            Origin = origin;
            Scope = scope;
            Candidates = candidates;
        }

        public AssemblyBindingTarget Target { get; }
        public PlatformTypeResolutionBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
        public ImmutableArray<PlatformTypeResolutionCandidateEvidence>
            Candidates { get; }
    }

    public sealed class TypeDeclaration :
        PlatformTypeResolutionAmbiguityEvidence
    {
        internal TypeDeclaration(
            PlatformTypeResolutionCandidateEvidence assembly,
            PlatformTypeResolutionOccurrenceEvidence occurrence,
            MetadataTypeDefinitionName type,
            ImmutableArray<PlatformTypeDeclarationCandidateEvidence>
                candidates)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(occurrence);
            ArgumentNullException.ThrowIfNull(type);
            Assembly = assembly;
            Occurrence = occurrence;
            Type = type;
            Candidates = candidates;
        }

        public PlatformTypeResolutionCandidateEvidence Assembly { get; }
        public PlatformTypeResolutionOccurrenceEvidence Occurrence { get; }
        public MetadataTypeDefinitionName Type { get; }
        public ImmutableArray<PlatformTypeDeclarationCandidateEvidence>
            Candidates { get; }
    }
}

/// <summary>Resource-free start evidence for a deferred type request.</summary>
public abstract class PlatformTypeResolutionStartEvidence
{
    private protected PlatformTypeResolutionStartEvidence()
    {
    }

    public sealed class Assembly : PlatformTypeResolutionStartEvidence
    {
        internal Assembly(
            PlatformTypeResolutionAssemblyEvidence value,
            PlatformTypeResolutionOccurrenceEvidence? occurrence,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
            Occurrence = occurrence;
            Scope = scope;
        }

        public PlatformTypeResolutionAssemblyEvidence Value { get; }
        public PlatformTypeResolutionOccurrenceEvidence? Occurrence { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Reference : PlatformTypeResolutionStartEvidence
    {
        internal Reference(
            AssemblyReferenceIdentity value,
            PlatformTypeResolutionBindingOriginEvidence origin,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(origin);
            Value = value;
            Origin = origin;
            Scope = scope;
        }

        public AssemblyReferenceIdentity Value { get; }
        public PlatformTypeResolutionBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class CoreLibrary : PlatformTypeResolutionStartEvidence
    {
        internal CoreLibrary(
            PlatformTypeResolutionBindingOriginEvidence.RequestingAssembly
                origin,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(origin);
            Origin = origin;
            Scope = scope;
        }

        public PlatformTypeResolutionBindingOriginEvidence.RequestingAssembly
            Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Module : PlatformTypeResolutionStartEvidence
    {
        internal Module(
            string name,
            PlatformTypeResolutionBindingOriginEvidence.RequestingAssembly
                origin)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(origin);
            Name = name;
            Origin = origin;
        }

        public string Name { get; }
        public PlatformTypeResolutionBindingOriginEvidence.RequestingAssembly
            Origin { get; }
    }
}

public sealed record PlatformTypeResolutionRequestEvidence(
    PlatformTypeResolutionStartEvidence Start,
    MetadataTypeDefinitionName Type);

public sealed record PlatformAssemblyBindingRequestEvidence(
    AssemblyBindingTarget Target,
    PlatformTypeResolutionBindingOriginEvidence Origin,
    AssemblyResolutionScope Scope);

/// <summary>Resource-free deferred work requested by Metadata.</summary>
public abstract class PlatformResolutionPlanRequestEvidence
{
    private protected PlatformResolutionPlanRequestEvidence()
    {
    }

    public sealed class Type : PlatformResolutionPlanRequestEvidence
    {
        internal Type(PlatformTypeResolutionRequestEvidence request) =>
            Request = request;

        public PlatformTypeResolutionRequestEvidence Request { get; }
    }

    public sealed class Binding : PlatformResolutionPlanRequestEvidence
    {
        internal Binding(PlatformAssemblyBindingRequestEvidence request) =>
            Request = request;

        public PlatformAssemblyBindingRequestEvidence Request { get; }
    }
}

/// <summary>Resource-free typed rejection evidence from Metadata.</summary>
public abstract class PlatformTypeResolutionFailureEvidence
{
    private protected PlatformTypeResolutionFailureEvidence()
    {
    }

    public sealed class DeclarationRejected :
        PlatformTypeResolutionFailureEvidence
    {
        internal DeclarationRejected(MetadataTypeNameFailure rejection) =>
            Rejection = rejection;

        public MetadataTypeNameFailure Rejection { get; }
    }

    public sealed class DeclarationBudgetExceeded :
        PlatformTypeResolutionFailureEvidence
    {
        internal DeclarationBudgetExceeded(int budget, string detail)
        {
            Budget = budget;
            Detail = detail;
        }

        public int Budget { get; }
        public string Detail { get; }
    }

    public sealed class DefinitionKindUnavailable :
        PlatformTypeResolutionFailureEvidence
    {
        internal DefinitionKindUnavailable(
            MetadataTypeDefinitionKindFailure failure) =>
            Failure = failure;

        public MetadataTypeDefinitionKindFailure Failure { get; }
    }

    public sealed class ForwarderCycle :
        PlatformTypeResolutionFailureEvidence
    {
        internal ForwarderCycle()
        {
        }
    }

    public sealed class HopBudgetExceeded :
        PlatformTypeResolutionFailureEvidence
    {
        internal HopBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    public sealed class RequestBudgetExceeded :
        PlatformTypeResolutionFailureEvidence
    {
        internal RequestBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    public sealed class UnsupportedModuleExport :
        PlatformTypeResolutionFailureEvidence
    {
        internal UnsupportedModuleExport(ModuleFileReference module) =>
            Module = module;

        public ModuleFileReference Module { get; }
    }

    public sealed class UnsupportedModuleReference :
        PlatformTypeResolutionFailureEvidence
    {
        internal UnsupportedModuleReference(string moduleName) =>
            ModuleName = moduleName;

        public string ModuleName { get; }
    }

    public sealed class UnregisteredAssembly :
        PlatformTypeResolutionFailureEvidence
    {
        internal UnregisteredAssembly(
            AssemblyAcquisitionRegistration registration) =>
            Registration = registration;

        public AssemblyAcquisitionRegistration Registration { get; }
    }

    public sealed class InvalidBindingPolicy :
        PlatformTypeResolutionFailureEvidence
    {
        internal InvalidBindingPolicy(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class CandidateOpenFailed :
        PlatformTypeResolutionFailureEvidence
    {
        internal CandidateOpenFailed(
            PlatformTypeResolutionAssemblyEvidence assembly,
            CandidateOpenFailure failure)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(failure);
            Assembly = assembly;
            Failure = failure;
        }

        public PlatformTypeResolutionAssemblyEvidence Assembly { get; }
        public CandidateOpenFailure Failure { get; }
    }

    public sealed class KindDependencyUnbound :
        PlatformTypeResolutionFailureEvidence
    {
        internal KindDependencyUnbound(
            AssemblyBindingTarget target,
            PlatformTypeResolutionBindingOriginEvidence origin,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(origin);
            Target = target;
            Origin = origin;
            Scope = scope;
        }

        public AssemblyBindingTarget Target { get; }
        public PlatformTypeResolutionBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class KindDependencyUnavailable :
        PlatformTypeResolutionFailureEvidence
    {
        internal KindDependencyUnavailable(
            AssemblyBindingTarget target,
            PlatformTypeResolutionBindingOriginEvidence origin,
            AssemblyResolutionScope scope,
            AssemblyBindingFailure failure)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(failure);
            Target = target;
            Origin = origin;
            Scope = scope;
            Failure = failure;
        }

        public AssemblyBindingTarget Target { get; }
        public PlatformTypeResolutionBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class KindDependencyCycle :
        PlatformTypeResolutionFailureEvidence
    {
        internal KindDependencyCycle(
            AssemblyBindingTarget target,
            PlatformTypeResolutionBindingOriginEvidence origin,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(origin);
            Target = target;
            Origin = origin;
            Scope = scope;
        }

        public AssemblyBindingTarget Target { get; }
        public PlatformTypeResolutionBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class KindDependencyTypeNotFound :
        PlatformTypeResolutionFailureEvidence
    {
        internal KindDependencyTypeNotFound(
            PlatformTypeResolutionCandidateEvidence assembly,
            PlatformTypeResolutionOccurrenceEvidence occurrence,
            MetadataTypeDefinitionName type)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(occurrence);
            ArgumentNullException.ThrowIfNull(type);
            Assembly = assembly;
            Occurrence = occurrence;
            Type = type;
        }

        public PlatformTypeResolutionCandidateEvidence Assembly { get; }
        public PlatformTypeResolutionOccurrenceEvidence Occurrence { get; }
        public MetadataTypeDefinitionName Type { get; }
    }

    public sealed class KindDependencyAmbiguous :
        PlatformTypeResolutionFailureEvidence
    {
        internal KindDependencyAmbiguous(
            PlatformTypeResolutionAmbiguityEvidence ambiguity,
            MetadataTypeDefinitionName type)
        {
            ArgumentNullException.ThrowIfNull(ambiguity);
            ArgumentNullException.ThrowIfNull(type);
            Ambiguity = ambiguity;
            Type = type;
        }

        public PlatformTypeResolutionAmbiguityEvidence Ambiguity { get; }
        public MetadataTypeDefinitionName Type { get; }
    }

    public sealed class DiscoveryBudgetExceeded :
        PlatformTypeResolutionFailureEvidence
    {
        internal DiscoveryBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    public sealed class PlanExpansionRequired :
        PlatformTypeResolutionFailureEvidence
    {
        internal PlanExpansionRequired(
            PlatformResolutionPlanRequestEvidence request) =>
            Request = request;

        public PlatformResolutionPlanRequestEvidence Request { get; }
    }
}

/// <summary>
/// Detached result of one exact Platform type-definition resolution.
/// </summary>
public abstract class PlatformTypeDefinitionResolutionResult
{
    private protected PlatformTypeDefinitionResolutionResult(
        ImmutableArray<PlatformTypeForwardingHopEvidence> hops,
        PlatformTypeResolutionOccurrenceEvidence? terminalOccurrence,
        AssemblyReferenceIdentity? terminalAssemblyIdentity)
    {
        Hops = hops;
        TerminalOccurrence = terminalOccurrence;
        TerminalAssemblyIdentity = terminalAssemblyIdentity;
    }

    public ImmutableArray<PlatformTypeForwardingHopEvidence> Hops { get; }
    public PlatformTypeResolutionOccurrenceEvidence? TerminalOccurrence
    {
        get;
    }
    public AssemblyReferenceIdentity? TerminalAssemblyIdentity { get; }

    public sealed class Resolved : PlatformTypeDefinitionResolutionResult
    {
        internal Resolved(
            PlatformTypeResolutionDefinitionEvidence definition,
            ImmutableArray<PlatformTypeForwardingHopEvidence> hops,
            PlatformTypeResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity) =>
            Definition = definition;

        public PlatformTypeResolutionDefinitionEvidence Definition { get; }
    }

    public sealed class NotFound : PlatformTypeDefinitionResolutionResult
    {
        internal NotFound(
            PlatformTypeResolutionCandidateEvidence lastAssembly,
            PlatformTypeResolutionOccurrenceEvidence lastOccurrence,
            ImmutableArray<PlatformTypeForwardingHopEvidence> hops,
            PlatformTypeResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            ArgumentNullException.ThrowIfNull(lastAssembly);
            ArgumentNullException.ThrowIfNull(lastOccurrence);
            LastAssembly = lastAssembly;
            LastOccurrence = lastOccurrence;
        }

        public PlatformTypeResolutionCandidateEvidence LastAssembly { get; }
        public PlatformTypeResolutionOccurrenceEvidence LastOccurrence { get; }
    }

    public sealed class UnboundBinding :
        PlatformTypeDefinitionResolutionResult
    {
        internal UnboundBinding(
            PlatformTypeResolutionUnresolvedBindingIdentity binding,
            AssemblyBindingTarget target,
            PlatformTypeResolutionBindingOriginEvidence origin,
            AssemblyResolutionScope scope,
            ImmutableArray<PlatformTypeForwardingHopEvidence> hops,
            PlatformTypeResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(origin);
            Binding = binding;
            Target = target;
            Origin = origin;
            Scope = scope;
        }

        public PlatformTypeResolutionUnresolvedBindingIdentity Binding { get; }
        public AssemblyBindingTarget Target { get; }
        public PlatformTypeResolutionBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Unavailable :
        PlatformTypeDefinitionResolutionResult
    {
        internal Unavailable(
            PlatformTypeResolutionUnresolvedBindingIdentity binding,
            AssemblyBindingTarget target,
            PlatformTypeResolutionBindingOriginEvidence origin,
            AssemblyResolutionScope scope,
            AssemblyBindingFailure failure,
            ImmutableArray<PlatformTypeForwardingHopEvidence> hops,
            PlatformTypeResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(failure);
            Binding = binding;
            Target = target;
            Origin = origin;
            Scope = scope;
            Failure = failure;
        }

        public PlatformTypeResolutionUnresolvedBindingIdentity Binding { get; }
        public AssemblyBindingTarget Target { get; }
        public PlatformTypeResolutionBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class Ambiguous : PlatformTypeDefinitionResolutionResult
    {
        internal Ambiguous(
            PlatformTypeResolutionAmbiguityEvidence ambiguity,
            ImmutableArray<PlatformTypeForwardingHopEvidence> hops,
            PlatformTypeResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            ArgumentNullException.ThrowIfNull(ambiguity);
            Ambiguity = ambiguity;
        }

        public PlatformTypeResolutionAmbiguityEvidence Ambiguity { get; }
    }

    public sealed class Rejected : PlatformTypeDefinitionResolutionResult
    {
        internal Rejected(
            PlatformTypeResolutionFailureEvidence failure,
            ImmutableArray<PlatformTypeForwardingHopEvidence> hops,
            PlatformTypeResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public PlatformTypeResolutionFailureEvidence Failure { get; }
    }
}
