using System.Collections.Immutable;

namespace ILInspector.Metadata;

public abstract class TypeResolutionStart
{
    private protected TypeResolutionStart()
    {
    }

    public sealed class Assembly : TypeResolutionStart
    {
        internal Assembly(
            ResolvedAssemblyReference value,
            AssemblyResolutionScope scope)
        {
            Value = value;
            Scope = scope;
        }

        public ResolvedAssemblyReference Value { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Reference : TypeResolutionStart
    {
        internal Reference(
            AssemblyReferenceIdentity value,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope)
        {
            Value = value;
            Origin = origin;
            Scope = scope;
        }

        public AssemblyReferenceIdentity Value { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class CoreLibrary : TypeResolutionStart
    {
        internal CoreLibrary(
            AssemblyBindingOrigin.RequestingAssembly origin,
            AssemblyResolutionScope scope)
        {
            Origin = origin;
            Scope = scope;
        }

        public AssemblyBindingOrigin.RequestingAssembly Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Module : TypeResolutionStart
    {
        internal Module(
            string name,
            AssemblyBindingOrigin.RequestingAssembly origin)
        {
            Name = name;
            Origin = origin;
        }

        public string Name { get; }
        public AssemblyBindingOrigin.RequestingAssembly Origin { get; }
    }
}

public sealed class TypeResolutionRequest
{
    public TypeResolutionRequest(
        TypeResolutionStart start,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(type);
        Start = start;
        Type = type;
    }

    public TypeResolutionStart Start { get; }
    public MetadataTypeDefinitionName Type { get; }

    public static TypeResolutionRequest FromAssembly(
        ResolvedAssemblyReference value,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateScope(scope);
        return new(new TypeResolutionStart.Assembly(value, scope), type);
    }

    public static TypeResolutionRequest FromReference(
        AssemblyReferenceIdentity value,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(origin);
        ValidateScope(scope);
        return new(new TypeResolutionStart.Reference(value, origin, scope), type);
    }

    public static TypeResolutionRequest FromCoreLibrary(
        ResolvedAssemblyReference requestingAssembly,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(requestingAssembly);
        ValidateScope(scope);
        return new(
            new TypeResolutionStart.CoreLibrary(
                AssemblyBindingOrigin.FromAssembly(requestingAssembly),
                scope),
            type);
    }

    public static TypeResolutionRequest FromModule(
        ResolvedAssemblyReference requestingAssembly,
        string moduleName,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(requestingAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        return new(
            new TypeResolutionStart.Module(
                moduleName,
                AssemblyBindingOrigin.FromAssembly(requestingAssembly)),
            type);
    }

    static void ValidateScope(AssemblyResolutionScope scope)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
    }
}

public abstract class ResolutionPlanRequest
{
    private protected ResolutionPlanRequest()
    {
    }

    public sealed class Type : ResolutionPlanRequest
    {
        internal Type(TypeResolutionRequest request) => Request = request;
        public TypeResolutionRequest Request { get; }
    }

    public sealed class Binding : ResolutionPlanRequest
    {
        internal Binding(AssemblyBindingRequest request) => Request = request;
        public AssemblyBindingRequest Request { get; }
    }
}

public abstract class TypeResolutionFailure
{
    private protected TypeResolutionFailure()
    {
    }

    public sealed class DeclarationRejected : TypeResolutionFailure
    {
        internal DeclarationRejected(MetadataTypeNameFailure rejection) =>
            Rejection = rejection;

        public MetadataTypeNameFailure Rejection { get; }
    }

    public sealed class ForwarderCycle : TypeResolutionFailure;

    public sealed class HopBudgetExceeded : TypeResolutionFailure
    {
        internal HopBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    public sealed class UnsupportedModuleExport : TypeResolutionFailure
    {
        internal UnsupportedModuleExport(ModuleFileReference module) =>
            Module = module;
        public ModuleFileReference Module { get; }
    }

    public sealed class UnsupportedModuleReference : TypeResolutionFailure
    {
        internal UnsupportedModuleReference(string moduleName) =>
            ModuleName = moduleName;
        public string ModuleName { get; }
    }

    public sealed class UnregisteredAssembly : TypeResolutionFailure
    {
        internal UnregisteredAssembly(
            AssemblyAcquisitionRegistration registration) =>
            Registration = registration;
        public AssemblyAcquisitionRegistration Registration { get; }
    }

    public sealed class InvalidBindingPolicy : TypeResolutionFailure
    {
        internal InvalidBindingPolicy(AssemblyBindingFailure failure) =>
            Failure = failure;
        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class CandidateOpenFailed : TypeResolutionFailure
    {
        internal CandidateOpenFailed(
            ResolvedAssemblyReference assembly,
            CandidateOpenFailure failure)
        {
            Assembly = assembly;
            Failure = failure;
        }

        public ResolvedAssemblyReference Assembly { get; }
        public CandidateOpenFailure Failure { get; }
    }

    public sealed class DiscoveryBudgetExceeded : TypeResolutionFailure
    {
        internal DiscoveryBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    public sealed class PlanExpansionRequired : TypeResolutionFailure
    {
        internal PlanExpansionRequired(ResolutionPlanRequest request) =>
            Request = request;
        public ResolutionPlanRequest Request { get; }
    }
}

public sealed class ResolvedTypeDefinitionKey
{
    internal ResolvedTypeDefinitionKey(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        AssemblyCandidateId assembly,
        TypeDefinitionToken definition)
    {
        Catalog = catalog;
        Generation = generation;
        Assembly = assembly;
        Definition = definition;
    }

    public AssemblyCatalogId Catalog { get; }
    internal AssemblyCatalogGenerationId Generation { get; }
    internal AssemblyCandidateId Assembly { get; }
    internal TypeDefinitionToken Definition { get; }
}

public readonly record struct MetadataTypeDefinitionAddress(
    Guid ModuleVersionId,
    TypeDefinitionToken Definition);

public sealed class ResolvedTypeDefinition
{
    internal ResolvedTypeDefinition(
        ResolvedTypeDefinitionKey key,
        MetadataTypeDefinitionAddress address,
        ResolvedAssemblyCandidate assembly,
        MetadataTypeDefinitionName type)
    {
        Key = key;
        Address = address;
        Assembly = assembly;
        Type = type;
    }

    public ResolvedTypeDefinitionKey Key { get; }
    public MetadataTypeDefinitionAddress Address { get; }
    public ResolvedAssemblyCandidate Assembly { get; }
    public MetadataTypeDefinitionName Type { get; }
}

public sealed class TypeForwardingHop
{
    internal TypeForwardingHop(
        ResolvedAssemblyCandidate sourceAssembly,
        ImmutableArray<ExportedTypeToken> declarations,
        AssemblyReferenceIdentity targetReference,
        AssemblyResolutionScope scope)
    {
        SourceAssembly = sourceAssembly;
        Declarations = declarations;
        TargetReference = targetReference;
        Scope = scope;
    }

    public ResolvedAssemblyCandidate SourceAssembly { get; }
    public ImmutableArray<ExportedTypeToken> Declarations { get; }
    public AssemblyReferenceIdentity TargetReference { get; }
    public AssemblyResolutionScope Scope { get; }
}

public abstract class TypeResolutionAmbiguity
{
    private protected TypeResolutionAmbiguity()
    {
    }

    public sealed class AssemblyBinding : TypeResolutionAmbiguity
    {
        internal AssemblyBinding(
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            ImmutableArray<ResolvedAssemblyCandidate> candidates)
        {
            Target = target;
            Origin = origin;
            Scope = scope;
            Candidates = candidates;
        }

        public AssemblyBindingTarget Target { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
        public ImmutableArray<ResolvedAssemblyCandidate> Candidates { get; }
    }

    public sealed class TypeDeclaration : TypeResolutionAmbiguity
    {
        internal TypeDeclaration(
            ResolvedAssemblyCandidate assembly,
            MetadataTypeDefinitionName type,
            ImmutableArray<TypeDeclarationCandidate> candidates)
        {
            Assembly = assembly;
            Type = type;
            Candidates = candidates;
        }

        public ResolvedAssemblyCandidate Assembly { get; }
        public MetadataTypeDefinitionName Type { get; }
        public ImmutableArray<TypeDeclarationCandidate> Candidates { get; }
    }
}

public abstract class TypeResolutionOutcome
{
    private protected TypeResolutionOutcome(
        ImmutableArray<TypeForwardingHop> hops) =>
        Hops = hops;

    public ImmutableArray<TypeForwardingHop> Hops { get; }

    public sealed class Resolved : TypeResolutionOutcome
    {
        internal Resolved(
            ResolvedTypeDefinition definition,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Definition = definition;
        public ResolvedTypeDefinition Definition { get; }
    }

    public sealed class NotFound : TypeResolutionOutcome
    {
        internal NotFound(
            ResolvedAssemblyCandidate lastAssembly,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            LastAssembly = lastAssembly;
        public ResolvedAssemblyCandidate LastAssembly { get; }
    }

    public sealed class UnboundBinding : TypeResolutionOutcome
    {
        internal UnboundBinding(
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            ImmutableArray<TypeForwardingHop> hops) : base(hops)
        {
            Target = target;
            Origin = origin;
            Scope = scope;
        }

        public AssemblyBindingTarget Target { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Unavailable : TypeResolutionOutcome
    {
        internal Unavailable(
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            AssemblyBindingFailure failure,
            ImmutableArray<TypeForwardingHop> hops) : base(hops)
        {
            Target = target;
            Origin = origin;
            Scope = scope;
            Failure = failure;
        }

        public AssemblyBindingTarget Target { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class Ambiguous : TypeResolutionOutcome
    {
        internal Ambiguous(
            TypeResolutionAmbiguity ambiguity,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Ambiguity = ambiguity;
        public TypeResolutionAmbiguity Ambiguity { get; }
    }

    public sealed class Rejected : TypeResolutionOutcome
    {
        internal Rejected(
            TypeResolutionFailure failure,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Failure = failure;
        public TypeResolutionFailure Failure { get; }
    }
}
