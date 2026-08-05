using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// Closed description of where an exact metadata type-name lookup begins.
/// The start is separate from <see cref="TypeResolutionRequest"/> so each arm
/// carries only the coordinates needed for that kind of lookup.
/// </summary>
public abstract class TypeResolutionStart
{
    private protected TypeResolutionStart()
    {
    }

    /// <summary>
    /// Begins by probing an explicitly acquired assembly, then follows any
    /// matching forwarder.
    /// </summary>
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

    /// <summary>
    /// Begins by binding an exact assembly reference from the supplied origin.
    /// </summary>
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

    /// <summary>
    /// Begins by asking policy for the requesting assembly's intrinsic core
    /// library, without synthesizing an assembly reference.
    /// </summary>
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

    /// <summary>
    /// Preserves a module-reference start. Module acquisition is represented
    /// explicitly as unsupported by the current engine.
    /// </summary>
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

/// <summary>
/// Resolves one exact <see cref="MetadataTypeDefinitionName"/> from one
/// structured <see cref="TypeResolutionStart"/>.
/// </summary>
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

    /// <summary>Creates a request that starts from an acquired assembly.</summary>
    public static TypeResolutionRequest FromAssembly(
        ResolvedAssemblyReference value,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateScope(scope);
        return new(new TypeResolutionStart.Assembly(value, scope), type);
    }

    /// <summary>
    /// Creates a request that first binds an assembly reference.
    /// </summary>
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

    /// <summary>
    /// Creates a request for the requesting assembly's intrinsic core library.
    /// </summary>
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

    /// <summary>Creates a request that preserves a module-reference start.</summary>
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

/// <summary>
/// Work that a coordinator may add to a later catalog generation after a
/// frozen lookup reports that its manifest was incomplete.
/// </summary>
public abstract class ResolutionPlanRequest
{
    private protected ResolutionPlanRequest()
    {
    }

    /// <summary>A type-resolution request absent from the frozen manifest.</summary>
    public sealed class Type : ResolutionPlanRequest
    {
        internal Type(TypeResolutionRequest request) => Request = request;
        public TypeResolutionRequest Request { get; }
    }

    /// <summary>An assembly-binding request absent from the frozen manifest.</summary>
    public sealed class Binding : ResolutionPlanRequest
    {
        internal Binding(AssemblyBindingRequest request) => Request = request;
        public AssemblyBindingRequest Request { get; }
    }
}

/// <summary>
/// Closed reason why type resolution was rejected. These failures describe
/// resolution mechanics; binding policy diagnostics remain
/// <see cref="AssemblyBindingFailure"/> values.
/// </summary>
public abstract class TypeResolutionFailure
{
    private protected TypeResolutionFailure()
    {
    }

    /// <summary>The single-image declaration probe rejected the type name.</summary>
    public sealed class DeclarationRejected : TypeResolutionFailure
    {
        internal DeclarationRejected(MetadataTypeNameFailure rejection) =>
            Rejection = rejection;

        public MetadataTypeNameFailure Rejection { get; }
    }

    /// <summary>A forwarding chain revisited a catalog candidate.</summary>
    public sealed class ForwarderCycle : TypeResolutionFailure;

    /// <summary>The forwarding chain exceeded its configured hop budget.</summary>
    public sealed class HopBudgetExceeded : TypeResolutionFailure
    {
        internal HopBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    /// <summary>
    /// The declaration is exported from a module, for which the current engine
    /// has no acquisition policy.
    /// </summary>
    public sealed class UnsupportedModuleExport : TypeResolutionFailure
    {
        internal UnsupportedModuleExport(ModuleFileReference module) =>
            Module = module;
        public ModuleFileReference Module { get; }
    }

    /// <summary>
    /// Resolution began from a module reference, which the current engine does
    /// not acquire.
    /// </summary>
    public sealed class UnsupportedModuleReference : TypeResolutionFailure
    {
        internal UnsupportedModuleReference(string moduleName) =>
            ModuleName = moduleName;
        public string ModuleName { get; }
    }

    /// <summary>
    /// The start or requesting origin was absent from this generation's
    /// registered roots.
    /// </summary>
    public sealed class UnregisteredAssembly : TypeResolutionFailure
    {
        internal UnregisteredAssembly(
            AssemblyAcquisitionRegistration registration) =>
            Registration = registration;
        public AssemblyAcquisitionRegistration Registration { get; }
    }

    /// <summary>The binding policy returned an invalid result.</summary>
    public sealed class InvalidBindingPolicy : TypeResolutionFailure
    {
        internal InvalidBindingPolicy(AssemblyBindingFailure failure) =>
            Failure = failure;
        public AssemblyBindingFailure Failure { get; }
    }

    /// <summary>
    /// A selected descriptor could not be inventoried or opened as a retained
    /// inspection session.
    /// </summary>
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

    /// <summary>Discovery exceeded the configured candidate budget.</summary>
    public sealed class DiscoveryBudgetExceeded : TypeResolutionFailure
    {
        internal DiscoveryBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    /// <summary>
    /// The request was not part of the frozen manifest and must be included in
    /// a later generation before it can be answered.
    /// </summary>
    public sealed class PlanExpansionRequired : TypeResolutionFailure
    {
        internal PlanExpansionRequired(ResolutionPlanRequest request) =>
            Request = request;
        public ResolutionPlanRequest Request { get; }
    }
}

/// <summary>
/// Opaque key for one resolved TypeDef in one frozen catalog generation.
/// It does not establish correspondence with definitions from other
/// generations.
/// </summary>
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

/// <summary>
/// Describes how strongly a catalog-issued definition token establishes
/// correspondence.
/// </summary>
public enum DefinitionJoinKind
{
    Exact,
    IndeterminateDuplicateArtifact,
}

/// <summary>
/// Hashable catalog currency for one TypeDef correspondence class in one
/// frozen generation.
/// </summary>
public sealed class DefinitionJoinToken : IEquatable<DefinitionJoinToken>
{
    readonly Guid _value;

    internal DefinitionJoinToken(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        Guid value,
        DefinitionJoinKind kind,
        DuplicateArtifactEvidence? evidence)
    {
        Catalog = catalog;
        Generation = generation;
        _value = value;
        Kind = kind;
        Evidence = evidence;
    }

    internal AssemblyCatalogId Catalog { get; }
    internal AssemblyCatalogGenerationId Generation { get; }
    public DefinitionJoinKind Kind { get; }
    public DuplicateArtifactEvidence? Evidence { get; }
    internal Guid Value => _value;

    public bool Equals(DefinitionJoinToken? other) =>
        other is not null
        && Catalog == other.Catalog
        && ReferenceEquals(Generation, other.Generation)
        && _value == other._value
        && Kind == other.Kind;

    public override bool Equals(object? obj) =>
        obj is DefinitionJoinToken other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Catalog, Generation, _value, Kind);

    public static bool operator ==(
        DefinitionJoinToken? left,
        DefinitionJoinToken? right) =>
        Equals(left, right);

    public static bool operator !=(
        DefinitionJoinToken? left,
        DefinitionJoinToken? right) =>
        !Equals(left, right);
}

/// <summary>
/// Catalog-owned result of projecting an opaque definition key into hashable
/// join currency.
/// </summary>
public abstract class DefinitionJoinTokenProjection
{
    private protected DefinitionJoinTokenProjection()
    {
    }

    public sealed class Issued : DefinitionJoinTokenProjection
    {
        internal Issued(DefinitionJoinToken token) => Token = token;

        public DefinitionJoinToken Token { get; }
    }

    public sealed class IncomparableCatalogs : DefinitionJoinTokenProjection
    {
        internal IncomparableCatalogs(
            AssemblyCatalogId catalog,
            AssemblyCatalogId definitionCatalog)
        {
            Catalog = catalog;
            DefinitionCatalog = definitionCatalog;
        }

        public AssemblyCatalogId Catalog { get; }
        public AssemblyCatalogId DefinitionCatalog { get; }
    }

    public sealed class StaleGeneration : DefinitionJoinTokenProjection
    {
        internal StaleGeneration(
            AssemblyCatalogGenerationId definitionGeneration,
            AssemblyCatalogGenerationId currentGeneration)
        {
            DefinitionGeneration = definitionGeneration;
            CurrentGeneration = currentGeneration;
        }

        public AssemblyCatalogGenerationId DefinitionGeneration { get; }
        public AssemblyCatalogGenerationId CurrentGeneration { get; }
    }
}

/// <summary>
/// Catalog-owned answer to whether two resolved TypeDefs correspond.
/// </summary>
public abstract class DefinitionCorrespondence
{
    private protected DefinitionCorrespondence()
    {
    }

    public sealed class Same : DefinitionCorrespondence
    {
        internal Same()
        {
        }
    }

    public sealed class Different : DefinitionCorrespondence
    {
        internal Different()
        {
        }
    }

    public sealed class IndeterminateDuplicateArtifact
        : DefinitionCorrespondence
    {
        internal IndeterminateDuplicateArtifact(
            DuplicateArtifactEvidence evidence) =>
            Evidence = evidence;

        public DuplicateArtifactEvidence Evidence { get; }
    }

    public sealed class IncomparableCatalogs : DefinitionCorrespondence
    {
        internal IncomparableCatalogs(
            AssemblyCatalogId left,
            AssemblyCatalogId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogId Left { get; }
        public AssemblyCatalogId Right { get; }
    }

    public sealed class StaleGeneration : DefinitionCorrespondence
    {
        internal StaleGeneration(
            AssemblyCatalogGenerationId left,
            AssemblyCatalogGenerationId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogGenerationId Left { get; }
        public AssemblyCatalogGenerationId Right { get; }
    }
}

public sealed class DuplicateArtifactCandidateEvidence
{
    internal DuplicateArtifactCandidateEvidence(
        ResolvedAssemblyReference assembly,
        MetadataTypeDefinitionAddress address)
    {
        Assembly = assembly;
        Address = address;
    }

    public ResolvedAssemblyReference Assembly { get; }
    public MetadataTypeDefinitionAddress Address { get; }
}

public sealed class DuplicateArtifactEvidence
{
    internal DuplicateArtifactEvidence(
        ImmutableArray<DuplicateArtifactCandidateEvidence> candidates) =>
        Candidates = candidates;

    public ImmutableArray<DuplicateArtifactCandidateEvidence> Candidates
        { get; }
}

/// <summary>
/// Durable physical location of a TypeDef row: module MVID plus validated
/// TypeDef token. This is an address, not a correspondence claim.
/// </summary>
public readonly record struct MetadataTypeDefinitionAddress(
    Guid ModuleVersionId,
    TypeDefinitionToken Definition)
{
    /// <summary>
    /// Resolves this durable address against a live reader only after checking
    /// its module MVID, token table, and TypeDef row bounds.
    /// </summary>
    public bool TryResolve(
        MetadataReader reader,
        out TypeDefinitionHandle handle)
    {
        ArgumentNullException.ThrowIfNull(reader);
        handle = default;

        int token = Definition.Value;
        if ((token & unchecked((int)0xFF000000)) != 0x02000000)
            return false;

        int row = token & 0x00FFFFFF;
        if (row <= 0 || row > reader.GetTableRowCount(TableIndex.TypeDef))
            return false;

        try
        {
            Guid mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (mvid != ModuleVersionId)
                return false;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return false;
        }

        handle = MetadataTokens.TypeDefinitionHandle(row);
        return true;
    }
}

/// <summary>
/// Successful resolution payload combining the opaque definition key, durable
/// address, catalog candidate, and exact lookup name.
/// </summary>
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

/// <summary>
/// Evidence for one followed forwarding edge. The declarations belong to
/// <see cref="SourceAssembly"/> and target the exact recorded assembly
/// reference under the scope used for the next binding.
/// </summary>
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

/// <summary>
/// Closed evidence describing whether ambiguity came from assembly binding or
/// from competing declarations inside one candidate.
/// </summary>
public abstract class TypeResolutionAmbiguity
{
    private protected TypeResolutionAmbiguity()
    {
    }

    /// <summary>Several assembly candidates remained plausible.</summary>
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

    /// <summary>One candidate contained competing declarations.</summary>
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

/// <summary>
/// Complete frozen answer for one type-resolution request. Every arm carries
/// the ordered forwarding hops observed before the terminal result.
/// </summary>
public abstract class TypeResolutionOutcome
{
    private protected TypeResolutionOutcome(
        ImmutableArray<TypeForwardingHop> hops) =>
        Hops = hops;

    public ImmutableArray<TypeForwardingHop> Hops { get; }

    /// <summary>An exact type definition was resolved.</summary>
    public sealed class Resolved : TypeResolutionOutcome
    {
        internal Resolved(
            ResolvedTypeDefinition definition,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Definition = definition;
        public ResolvedTypeDefinition Definition { get; }
    }

    /// <summary>
    /// The final readable candidate contained neither a definition nor a
    /// matching forwarder.
    /// </summary>
    public sealed class NotFound : TypeResolutionOutcome
    {
        internal NotFound(
            ResolvedAssemblyCandidate lastAssembly,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            LastAssembly = lastAssembly;
        public ResolvedAssemblyCandidate LastAssembly { get; }
    }

    /// <summary>Policy found no assembly for the required binding.</summary>
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

    /// <summary>
    /// Policy understood the binding but could not provide a usable candidate.
    /// </summary>
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

    /// <summary>Resolution ended with explicit ambiguity evidence.</summary>
    public sealed class Ambiguous : TypeResolutionOutcome
    {
        internal Ambiguous(
            TypeResolutionAmbiguity ambiguity,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Ambiguity = ambiguity;
        public TypeResolutionAmbiguity Ambiguity { get; }
    }

    /// <summary>Resolution stopped with a typed mechanical failure.</summary>
    public sealed class Rejected : TypeResolutionOutcome
    {
        internal Rejected(
            TypeResolutionFailure failure,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Failure = failure;
        public TypeResolutionFailure Failure { get; }
    }
}
