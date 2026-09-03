using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

// A policy selection contains public acquisition descriptors. This internal
// value pairs the catalog-interned outcome with everything needed to reproduce
// that outcome in a later generation, including failed selected descriptors.
sealed record CachedBindingEvaluation(
    AssemblyBindingOutcome Outcome,
    ResolvedAssemblyReference? Assembly = null,
    CandidateOpenFailure? OpenFailure = null,
    ImmutableArray<ResolvedAssemblyReference> Registrations = default);

abstract record TypeResolutionContextBuildResult
{
    internal sealed record Completed(TypeResolutionContext Context)
        : TypeResolutionContextBuildResult;

    internal sealed record PolicyVersionChanged(
        AssemblyBindingPolicyVersion Expected,
        AssemblyBindingPolicyVersion? Observed)
        : TypeResolutionContextBuildResult;
}

sealed class PolicyVersionChangedException(
    AssemblyBindingPolicyVersion expected,
    AssemblyBindingPolicyVersion? observed) : Exception
{
    internal AssemblyBindingPolicyVersion Expected { get; } = expected;
    internal AssemblyBindingPolicyVersion? Observed { get; } = observed;
}

readonly record struct PolicyCacheKey(
    AssemblyBindingPolicyVersion PolicyVersion,
    object RequestKey);

readonly record struct AssemblyBindingDomainKey(
    bool IsGlobal,
    AssemblyCandidateId Candidate)
{
    internal static AssemblyBindingDomainKey Global => new(true, default);

    internal static AssemblyBindingDomainKey FromCandidate(
        AssemblyCandidateId candidate) =>
        new(false, candidate);
}

sealed record BindingKey(
    AssemblyBindingDomainKey Domain,
    AssemblyBindingTarget Target,
    AssemblyResolutionScope Scope);

/// <summary>
/// Resource and traversal limits shared by all generations in a
/// <see cref="TypeResolutionCatalog"/>.
/// </summary>
public sealed record TypeResolutionContextOptions
{
    /// <summary>Default maximum distinct requests evaluated per generation.</summary>
    public const int DefaultMaxTypeResolutionRequests = 65_536;

    /// <summary>Default bound for forwarded type resolution.</summary>
    public const int DefaultMaxForwarderHops = 8;

    /// <summary>Maximum acquisition registrations retained by the catalog.</summary>
    public int MaxCandidates { get; init; } =
        InspectionAcquisitionPlanOptions.DefaultMaxCandidates;

    /// <summary>Maximum aggregate bytes retained in open candidate images.</summary>
    public long MaxRetainedImageBytes { get; init; } =
        InspectionAcquisitionPlanOptions.DefaultMaxRetainedImageBytes;

    /// <summary>Maximum source-open operations permitted concurrently.</summary>
    public int MaxConcurrentSourceOpens { get; init; } =
        InspectionAcquisitionPlanOptions.DefaultMaxConcurrentSourceOpens;

    /// <summary>
    /// Maximum forwarding edges followed before resolution reports
    /// <see cref="TypeResolutionFailure.HopBudgetExceeded"/>.
    /// </summary>
    public int MaxForwarderHops { get; init; } =
        DefaultMaxForwarderHops;

    /// <summary>
    /// Maximum distinct type-resolution requests evaluated in one generation.
    /// </summary>
    public int MaxTypeResolutionRequests { get; init; } =
        DefaultMaxTypeResolutionRequests;

    internal InspectionAcquisitionPlanOptions AcquisitionOptions()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetainedImageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxConcurrentSourceOpens);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxForwarderHops);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxTypeResolutionRequests);
        return new InspectionAcquisitionPlanOptions
        {
            MaxCandidates = MaxCandidates,
            MaxRetainedImageBytes = MaxRetainedImageBytes,
            MaxConcurrentSourceOpens = MaxConcurrentSourceOpens,
        };
    }
}

/// <summary>
/// Typed result of extracting an API surface through a resolution catalog's
/// retained candidate image.
/// </summary>
public abstract class ResolutionAwareApiSurfaceOutcome
{
    private protected ResolutionAwareApiSurfaceOutcome()
    {
    }

    public sealed class Read : ResolutionAwareApiSurfaceOutcome
    {
        internal Read(ApiSurface surface) => Surface = surface;

        public ApiSurface Surface { get; }
    }

    public sealed class Rejected : ResolutionAwareApiSurfaceOutcome
    {
        internal Rejected(CandidateOpenFailure failure) =>
            Failure = failure;

        public CandidateOpenFailure Failure { get; }
    }
}

/// <summary>
/// Inspection-lifetime owner of assembly acquisition and reusable declaration,
/// binding, and resolution caches. A catalog creates immutable
/// <see cref="TypeResolutionContext"/> generations as their request manifests
/// evolve.
/// </summary>
public sealed class TypeResolutionCatalog : IDisposable
{
    readonly object _gate = new();
    readonly SynchronousConcurrencyGate _generationGate = new(1);
    readonly InspectionAcquisitionPlan _acquisition;
    readonly Dictionary<DeclarationCacheKey, TypeDeclarationResult>
        _declarations = [];
    readonly Dictionary<PolicyCacheKey, CachedBindingEvaluation>
        _bindings = [];
    readonly Dictionary<PolicyCacheKey, TypeResolutionOutcome>
        _resolutions = [];
    readonly Dictionary<DefinitionClassKey, DefinitionJoinToken>
        _definitionJoinTokens = [];
    readonly Dictionary<BindingKey, UnresolvedBindingKey>
        _unresolvedBindingKeys = [];
    readonly TypeResolutionContextOptions _options;
    AssemblyCatalogGenerationId? _latestGeneration;
    ImmutableDictionary<AssemblyCandidateId, FrozenCandidate>
        _latestCandidates =
            ImmutableDictionary<AssemblyCandidateId, FrozenCandidate>.Empty;
    int _activeExtractions;
    bool _disposing;
    bool _disposed;

    /// <summary>
    /// Creates an inspection-lifetime catalog with the supplied resource and
    /// traversal limits.
    /// </summary>
    public TypeResolutionCatalog(TypeResolutionContextOptions? options = null)
    {
        _options = options ?? new TypeResolutionContextOptions();
        _acquisition = new InspectionAcquisitionPlan(
            _options.AcquisitionOptions());
    }

    /// <summary>Gets the identity shared by every generation in this catalog.</summary>
    public AssemblyCatalogId Id => _acquisition.CatalogId;

    /// <summary>
    /// Registers an already retained immutable image for reuse by later
    /// resolution contexts without reacquiring its bytes.
    /// </summary>
    public void RegisterRetainedSnapshot(
        ResolvedAssemblyReference assembly,
        AssemblyImageSnapshot snapshot)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _acquisition.RegisterRetainedSnapshot(assembly, snapshot);
        }
    }

    /// <summary>
    /// Extracts a resolution-aware API surface from the same retained candidate
    /// image used by this catalog's resolution generations.
    /// </summary>
    public ResolutionAwareApiSurfaceOutcome ExtractApiSurface(
        ResolvedAssemblyReference source,
        IAssemblyBindingPolicy bindingPolicy,
        bool includeAll = false,
        bool typesOnly = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindingPolicy);

        using IDisposable lease = AcquireExtractionLease();
        CandidateRegistrationResult registration =
            _acquisition.RegisterRoot(source);
        if (registration
            is CandidateRegistrationResult.Rejected rejected)
        {
            return new ResolutionAwareApiSurfaceOutcome.Rejected(
                rejected.Failure);
        }

        var readyRegistration =
            (CandidateRegistrationResult.Ready)registration;
        CandidateSessionResult session =
            _acquisition.OpenSession(
                readyRegistration.Candidate);
        if (session is CandidateSessionResult.Rejected sessionRejected)
        {
            return new ResolutionAwareApiSurfaceOutcome.Rejected(
                sessionRejected.Failure);
        }

        ApiSurface surface =
            ((CandidateSessionResult.Ready)session)
                .Session.ApiSurface(
                    source,
                    this,
                    bindingPolicy,
                    includeAll,
                    typesOnly);
        if (readyRegistration.InventoryFailure is { } inventoryFailure
            && !surface.InspectionFailures.Any(
                failure =>
                    failure.Mechanism
                        == MetadataTypeNameFailureMechanism.Metadata
                    && string.Equals(
                        failure.Detail,
                        inventoryFailure.Detail,
                        StringComparison.Ordinal)))
        {
            surface.InspectionFailures.Add(
                new ApiSurfaceInspectionFailure(
                    "inventory assembly adjacency",
                    0,
                    MetadataTypeNameFailureMechanism.Metadata,
                    inventoryFailure.Kind.ToString(),
                    inventoryFailure.Detail,
                    source.Identity));
        }
        return new ResolutionAwareApiSurfaceOutcome.Read(surface);
    }

    IDisposable AcquireExtractionLease()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _disposed || _disposing,
                this);
            _activeExtractions++;
            return new ExtractionLease(this);
        }
    }

    void EndExtraction()
    {
        lock (_gate)
        {
            _activeExtractions--;
            if (_activeExtractions == 0)
                Monitor.PulseAll(_gate);
        }
    }

    sealed class ExtractionLease(TypeResolutionCatalog owner) : IDisposable
    {
        TypeResolutionCatalog? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.EndExtraction();
    }

    /// <summary>
    /// Compares two opaque definition keys in the latest frozen generation.
    /// Duplicate copies remain explicitly indeterminate.
    /// </summary>
    public DefinitionCorrespondence Compare(
        ResolvedTypeDefinitionKey left,
        ResolvedTypeDefinitionKey right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (left.Catalog != Id || right.Catalog != Id)
            {
                return new DefinitionCorrespondence.IncomparableCatalogs(
                    left.Catalog,
                    right.Catalog);
            }

            if (!ReferenceEquals(left.Generation, right.Generation)
                || !ReferenceEquals(left.Generation, _latestGeneration))
            {
                return new DefinitionCorrespondence.StaleGeneration(
                    left.Generation,
                    right.Generation);
            }

            if (left.Assembly == right.Assembly)
            {
                return left.Definition == right.Definition
                    ? new DefinitionCorrespondence.Same()
                    : new DefinitionCorrespondence.Different();
            }

            if (!TryGetDefinitionClass(left, out DefinitionClassKey leftClass)
                || !TryGetDefinitionClass(
                    right,
                    out DefinitionClassKey rightClass))
            {
                return new DefinitionCorrespondence.StaleGeneration(
                    left.Generation,
                    right.Generation);
            }

            if (leftClass != rightClass)
                return new DefinitionCorrespondence.Different();

            return new DefinitionCorrespondence.IndeterminateDuplicateArtifact(
                DuplicateEvidence(leftClass));
        }
    }

    /// <summary>
    /// Issues the hashable correspondence token for a definition in the
    /// latest frozen generation.
    /// </summary>
    public DefinitionJoinTokenProjection ProjectDefinitionJoinToken(
        ResolvedTypeDefinitionKey definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (definition.Catalog != Id)
            {
                return new DefinitionJoinTokenProjection.IncomparableCatalogs(
                    Id,
                    definition.Catalog);
            }

            if (!ReferenceEquals(definition.Generation, _latestGeneration)
                || !TryGetDefinitionClass(
                    definition,
                    out DefinitionClassKey definitionClass))
            {
                return new DefinitionJoinTokenProjection.StaleGeneration(
                    definition.Generation,
                    _latestGeneration!);
            }

            if (_definitionJoinTokens.TryGetValue(
                    definitionClass,
                    out DefinitionJoinToken? existing))
            {
                return new DefinitionJoinTokenProjection.Issued(existing);
            }

            DuplicateArtifactEvidence? evidence = null;
            DefinitionJoinKind kind = DefinitionJoinKind.Exact;
            if (DefinitionClassCandidateCount(definitionClass) > 1)
            {
                kind = DefinitionJoinKind.IndeterminateDuplicateArtifact;
                evidence = DuplicateEvidence(definitionClass);
            }

            var token = new DefinitionJoinToken(
                Id,
                definition.Generation,
                Guid.NewGuid(),
                kind,
                evidence);
            _definitionJoinTokens.Add(definitionClass, token);
            return new DefinitionJoinTokenProjection.Issued(token);
        }
    }

    /// <summary>
    /// Issues the hashable correspondence key for one unresolved binding in
    /// the latest frozen generation.
    /// </summary>
    public UnresolvedBindingKeyProjection ProjectUnresolvedBindingKey(
        UnresolvedBindingReference binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (binding.Catalog != Id)
            {
                return new UnresolvedBindingKeyProjection.IncomparableCatalogs(
                    Id,
                    binding.Catalog);
            }

            if (!ReferenceEquals(binding.Generation, _latestGeneration))
            {
                return new UnresolvedBindingKeyProjection.StaleGeneration(
                    binding.Generation,
                    _latestGeneration!);
            }

            if (_unresolvedBindingKeys.TryGetValue(
                    binding.Binding,
                    out UnresolvedBindingKey? existing))
            {
                return new UnresolvedBindingKeyProjection.Issued(existing);
            }

            var key = new UnresolvedBindingKey(
                Id,
                binding.Generation,
                Guid.NewGuid());
            _unresolvedBindingKeys.Add(binding.Binding, key);
            return new UnresolvedBindingKeyProjection.Issued(key);
        }
    }

    /// <summary>
    /// Discovers and freezes a context for type requests over the supplied
    /// roots.
    /// </summary>
    public TypeResolutionContext CreateContext(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<TypeResolutionRequest> requests) =>
        CreateContextWithCancellation(
            policy,
            roots,
            [],
            requests,
            CancellationToken.None);

    /// <summary>
    /// Discovers and freezes a context for explicit binding and type requests
    /// over the supplied roots.
    /// </summary>
    public TypeResolutionContext CreateContext(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<AssemblyBindingRequest> bindingRequests,
        IEnumerable<TypeResolutionRequest> requests) =>
        CreateContextWithCancellation(
            policy,
            roots,
            bindingRequests,
            requests,
            CancellationToken.None);

    /// <summary>
    /// Discovers and freezes a cancellable context for explicit binding and
    /// type requests over the supplied roots.
    /// </summary>
    public TypeResolutionContext CreateContextWithCancellation(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<AssemblyBindingRequest> bindingRequests,
        IEnumerable<TypeResolutionRequest> requests,
        CancellationToken cancellationToken) =>
        CreateContextCore(
            policy,
            roots,
            bindingRequests,
            requests,
            ownsCatalog: false,
            allowRootAdjacencyDegradation: false,
            cancellationToken);

    internal TypeResolutionContext CreateApiSurfaceContext(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<TypeResolutionRequest> requests) =>
        CreateContextCore(
            policy,
            roots,
            [],
            requests,
            ownsCatalog: false,
            allowRootAdjacencyDegradation: true,
            CancellationToken.None);

    internal TypeResolutionContext CreateOwnedContext(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<TypeResolutionRequest> requests,
        CancellationToken cancellationToken) =>
        CreateContextCore(
            policy,
            roots,
            [],
            requests,
            ownsCatalog: true,
            allowRootAdjacencyDegradation: false,
            cancellationToken);

    TypeResolutionContext CreateContextCore(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<AssemblyBindingRequest> bindingRequests,
        IEnumerable<TypeResolutionRequest> requests,
        bool ownsCatalog,
        bool allowRootAdjacencyDegradation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(bindingRequests);
        ArgumentNullException.ThrowIfNull(requests);

        _generationGate.Enter(cancellationToken);
        try
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposed, this);

            TypeResolutionContextBuildResult result =
                TypeResolutionContext.Build(
                this,
                policy,
                roots,
                bindingRequests,
                requests,
                _options,
                ownsCatalog,
                allowRootAdjacencyDegradation,
                cancellationToken);
            return result switch
            {
                TypeResolutionContextBuildResult.Completed completed =>
                    completed.Context,
                TypeResolutionContextBuildResult.PolicyVersionChanged =>
                    throw new InvalidOperationException(
                        "The binding policy changed version during discovery."),
                _ => throw new InvalidOperationException(
                    "Unknown type-resolution context build result."),
            };
        }
        finally
        {
            _generationGate.Exit();
        }
    }

    internal InspectionAcquisitionPlan Acquisition => _acquisition;
    internal int MaxCandidates => _options.MaxCandidates;
    internal int MaxTypeResolutionRequests =>
        _options.MaxTypeResolutionRequests;
    internal object LifetimeGate => _gate;

    internal void EnsureAlive()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal bool TryGetDeclaration(
        AssemblyCandidateId candidate,
        MetadataTypeDefinitionName type,
        out TypeDeclarationResult? declaration) =>
        _declarations.TryGetValue(
            new DeclarationCacheKey(candidate, type),
            out declaration);

    internal void AddDeclaration(
        AssemblyCandidateId candidate,
        MetadataTypeDefinitionName type,
        TypeDeclarationResult declaration) =>
        _declarations.Add(
            new DeclarationCacheKey(candidate, type),
            declaration);

    internal bool TryGetBinding(
        AssemblyBindingPolicyVersion policyVersion,
        object key,
        out CachedBindingEvaluation? evaluation) =>
        _bindings.TryGetValue(
            new PolicyCacheKey(policyVersion, key),
            out evaluation);

    internal void PromoteBinding(
        AssemblyBindingPolicyVersion policyVersion,
        object key,
        CachedBindingEvaluation evaluation) =>
        _bindings.TryAdd(
            new PolicyCacheKey(policyVersion, key),
            evaluation);

    internal bool TryGetResolution(
        AssemblyBindingPolicyVersion policyVersion,
        object key,
        out TypeResolutionOutcome? outcome) =>
        _resolutions.TryGetValue(
            new PolicyCacheKey(policyVersion, key),
            out outcome);

    internal void PromoteResolution(
        AssemblyBindingPolicyVersion policyVersion,
        object key,
        TypeResolutionOutcome outcome) =>
        _resolutions.TryAdd(
            new PolicyCacheKey(policyVersion, key),
            outcome);

    internal void PublishGeneration(
        AssemblyCatalogGenerationId generation,
        IReadOnlyDictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyCandidate> candidates,
        IReadOnlyDictionary<AssemblyCandidateId, AssemblyInventorySnapshot>
            inventories)
    {
        var frozen = ImmutableDictionary.CreateBuilder<
            AssemblyCandidateId,
            FrozenCandidate>();
        foreach (ResolvedAssemblyCandidate candidate in candidates.Values)
        {
            frozen.Add(
                candidate.Id,
                new FrozenCandidate(
                    candidate.Assembly,
                    inventories[candidate.Id]));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _latestCandidates = frozen.ToImmutable();
            _latestGeneration = generation;
            _definitionJoinTokens.Clear();
            _unresolvedBindingKeys.Clear();
        }
    }

    /// <summary>Releases every retained candidate session owned by the catalog.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposing)
            {
                while (_disposing)
                    Monitor.Wait(_gate);
                return;
            }
            if (_disposed)
                return;

            _disposing = true;
            while (_activeExtractions != 0)
                Monitor.Wait(_gate);
        }

        _generationGate.Enter();
        try
        {
            lock (_gate)
            {
                _disposed = true;
                _latestCandidates =
                    ImmutableDictionary<
                        AssemblyCandidateId,
                        FrozenCandidate>.Empty;
                _latestGeneration = null;
                _declarations.Clear();
                _bindings.Clear();
                _resolutions.Clear();
                _definitionJoinTokens.Clear();
                _unresolvedBindingKeys.Clear();
            }

            _acquisition.Dispose();
        }
        finally
        {
            _generationGate.Exit();
            lock (_gate)
            {
                _disposing = false;
                Monitor.PulseAll(_gate);
            }
        }
    }

    readonly record struct DeclarationCacheKey(
        AssemblyCandidateId Candidate,
        MetadataTypeDefinitionName Type);

    bool TryGetDefinitionClass(
        ResolvedTypeDefinitionKey definition,
        out DefinitionClassKey definitionClass)
    {
        if (!_latestCandidates.TryGetValue(
                definition.Assembly,
                out FrozenCandidate? candidate))
        {
            definitionClass = default;
            return false;
        }

        definitionClass = new DefinitionClassKey(
            candidate.Inventory.Identity,
            candidate.Inventory.ModuleVersionId,
            definition.Definition);
        return true;
    }

    int DefinitionClassCandidateCount(DefinitionClassKey definitionClass) =>
        _latestCandidates.Values.Count(candidate =>
            candidate.Inventory.Identity == definitionClass.Identity
            && candidate.Inventory.ModuleVersionId
                == definitionClass.ModuleVersionId);

    DuplicateArtifactEvidence DuplicateEvidence(
        DefinitionClassKey definitionClass) =>
        new(
            _latestCandidates.Values
                .Where(candidate =>
                    candidate.Inventory.Identity == definitionClass.Identity
                    && candidate.Inventory.ModuleVersionId
                        == definitionClass.ModuleVersionId)
                .OrderBy(
                    candidate => candidate.Assembly.Path,
                    StringComparer.Ordinal)
                .Select(candidate =>
                    new DuplicateArtifactCandidateEvidence(
                        candidate.Assembly,
                        new MetadataTypeDefinitionAddress(
                            candidate.Inventory.ModuleVersionId,
                            definitionClass.Definition)))
                .ToImmutableArray());

    readonly record struct DefinitionClassKey(
        AssemblyReferenceIdentity Identity,
        Guid ModuleVersionId,
        TypeDefinitionToken Definition);

    sealed record FrozenCandidate(
        ResolvedAssemblyReference Assembly,
        AssemblyInventorySnapshot Inventory);
}

/// <summary>
/// One frozen catalog generation containing the binding and type-resolution
/// answers discovered for an explicit manifest. <see cref="Resolve"/> and
/// <see cref="Bind"/> only project requests onto that manifest; missing work is
/// returned as an expansion request for a later generation.
/// </summary>
public sealed class TypeResolutionContext : IDisposable
{
    readonly object _gate = new();
    readonly TypeResolutionCatalog _catalog;
    readonly bool _ownsCatalog;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        ResolvedAssemblyCandidate> _candidates;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        CandidateOpenFailure> _registrationFailures;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        ResolvedAssemblyReference> _descriptors;
    ImmutableDictionary<RequestKey, TypeResolutionOutcome> _outcomes;
    ImmutableDictionary<
        TypeResolutionManifestKey,
        TypeResolutionOutcome> _projectionFailures;
    ImmutableDictionary<BindingKey, AssemblyBindingOutcome> _bindings;
    ImmutableDictionary<AssemblyCandidateId, AssemblyInventorySnapshot>
        _inventories;
    bool _disposed;

    TypeResolutionContext(
        TypeResolutionCatalog catalog,
        bool ownsCatalog,
        AssemblyCatalogGenerationId generation,
        Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyCandidate> candidates,
        Dictionary<
            AssemblyAcquisitionRegistration,
            CandidateOpenFailure> registrationFailures,
        Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyReference> descriptors,
        ImmutableDictionary<RequestKey, TypeResolutionOutcome> outcomes,
        ImmutableDictionary<
            TypeResolutionManifestKey,
            TypeResolutionOutcome> projectionFailures,
        ImmutableDictionary<BindingKey, AssemblyBindingOutcome> bindings,
        ImmutableDictionary<AssemblyCandidateId, AssemblyInventorySnapshot>
            inventories)
    {
        _catalog = catalog;
        _ownsCatalog = ownsCatalog;
        Generation = generation;
        _candidates = candidates;
        _registrationFailures = registrationFailures;
        _descriptors = descriptors;
        _outcomes = outcomes;
        _projectionFailures = projectionFailures;
        _bindings = bindings;
        _inventories = inventories;
    }

    /// <summary>Gets the owning catalog's inspection-lifetime identity.</summary>
    public AssemblyCatalogId Catalog => _catalog.Id;

    /// <summary>Gets this frozen manifest generation's identity.</summary>
    public AssemblyCatalogGenerationId Generation { get; }

    /// <summary>
    /// Projects a definition resolved by this context into the owning
    /// catalog's current join currency.
    /// </summary>
    public DefinitionJoinTokenProjection ProjectDefinitionJoinToken(
        ResolvedTypeDefinitionKey definition)
    {
        lock (_gate)
        {
            lock (_catalog.LifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _catalog.EnsureAlive();
                return _catalog.ProjectDefinitionJoinToken(definition);
            }
        }
    }

    /// <summary>
    /// Projects an unresolved binding observed by this context into the owning
    /// catalog's current join currency.
    /// </summary>
    public UnresolvedBindingKeyProjection ProjectUnresolvedBindingKey(
        UnresolvedBindingReference binding)
    {
        lock (_gate)
        {
            lock (_catalog.LifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _catalog.EnsureAlive();
                return _catalog.ProjectUnresolvedBindingKey(binding);
            }
        }
    }

    /// <summary>
    /// Gets the frozen adjacency inventory for a candidate selected in this
    /// generation.
    /// </summary>
    public AssemblyInventorySnapshot GetInventory(
        ResolvedAssemblyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_inventories.TryGetValue(
                    candidate.Id,
                    out AssemblyInventorySnapshot? inventory))
            {
                throw new ArgumentException(
                    "The candidate does not belong to this generation.",
                    nameof(candidate));
            }

            return inventory;
        }
    }

    /// <summary>
    /// Returns the candidate descriptor backed by this generation's retained
    /// immutable image rather than its original acquisition source.
    /// </summary>
    /// <remarks>
    /// <c>CrossAssemblyMetadataResolver_UsesRetainedCandidateImage</c>
    /// gates that consumers do not reopen the mutable source.
    /// </remarks>
    public ResolvedAssemblyReference? RetainAssemblyReference(
        ResolvedAssemblyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            lock (_catalog.LifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _catalog.EnsureAlive();
                if (!_candidates.TryGetValue(
                        candidate.Assembly.Registration,
                        out ResolvedAssemblyCandidate? owned)
                    || !ReferenceEquals(owned, candidate))
                {
                    throw new ArgumentException(
                        "The candidate does not belong to this generation.",
                        nameof(candidate));
                }

                return _catalog.Acquisition
                    .RetainAssemblyReference(candidate);
            }
        }
    }

    /// <summary>
    /// Reads the defining image's instance-field primitive for a type already
    /// resolved in this generation. Returns <see langword="false"/> when the
    /// definition is from another catalog or generation, or the retained
    /// session cannot be opened. Does not expose a <see cref="MetadataReader"/>.
    /// Gated by <c>TypeResolutionEnumWidthTests</c>.
    /// </summary>
    public bool TryGetEnumUnderlyingType(
        ResolvedTypeDefinition definition,
        out PrimitiveTypeCode code)
    {
        ArgumentNullException.ThrowIfNull(definition);
        code = default;
        lock (_gate)
        {
            lock (_catalog.LifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _catalog.EnsureAlive();
                if (definition.Key.Catalog != Catalog
                    || !ReferenceEquals(definition.Key.Generation, Generation))
                {
                    return false;
                }
                if (definition.Kind
                    != MetadataTypeDefinitionKind.ValueType)
                {
                    return false;
                }

                CandidateSessionResult session =
                    _catalog.Acquisition.OpenSession(definition.Assembly);
                if (session is not CandidateSessionResult.Ready ready)
                    return false;

                return ready.Session.TryGetEnumUnderlyingType(
                    definition.Address,
                    out code);
            }
        }
    }

    /// <summary>
    /// Creates a standalone context that owns its private catalog and its
    /// retained candidate sessions.
    /// </summary>
    public static TypeResolutionContext Create(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<TypeResolutionRequest> requests,
        TypeResolutionContextOptions? options = null) =>
        CreateWithCancellation(
            policy,
            roots,
            requests,
            options,
            CancellationToken.None);

    /// <summary>
    /// Creates a cancellable standalone context that owns its private catalog
    /// and its retained candidate sessions.
    /// </summary>
    public static TypeResolutionContext CreateWithCancellation(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<TypeResolutionRequest> requests,
        TypeResolutionContextOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(requests);

        var catalog = new TypeResolutionCatalog(options);
        try
        {
            return catalog.CreateOwnedContext(
                    policy,
                    roots,
                    requests,
                    cancellationToken);
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    internal static TypeResolutionContextBuildResult Build(
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<AssemblyBindingRequest> bindingRequests,
        IEnumerable<TypeResolutionRequest> requests,
        TypeResolutionContextOptions options,
        bool ownsCatalog,
        bool allowRootAdjacencyDegradation,
        CancellationToken cancellationToken)
    {
        try
        {
            var builder = new Builder(
                catalog,
                policy,
                options.MaxForwarderHops,
                options.MaxCandidates,
                options.MaxTypeResolutionRequests,
                ownsCatalog,
                cancellationToken);
            foreach (ResolvedAssemblyReference root in roots)
            {
                ArgumentNullException.ThrowIfNull(root);
                builder.Register(
                    root,
                    allowRootAdjacencyDegradation);
            }

            foreach (AssemblyBindingRequest request in bindingRequests)
            {
                ArgumentNullException.ThrowIfNull(request);
                builder.Add(request);
            }

            foreach (TypeResolutionRequest request in requests)
            {
                ArgumentNullException.ThrowIfNull(request);
                builder.Add(request);
            }

            return builder.Freeze();
        }
        catch (PolicyVersionChangedException changed)
        {
            return new TypeResolutionContextBuildResult.PolicyVersionChanged(
                changed.Expected,
                changed.Observed);
        }
    }

    /// <summary>
    /// Returns the frozen answer for <paramref name="request"/>, or a typed
    /// expansion result when the request was not in this generation's
    /// manifest.
    /// </summary>
    public TypeResolutionOutcome Resolve(TypeResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            lock (_catalog.LifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _catalog.EnsureAlive();

                if (!TryProjectRequest(
                        request,
                        out RequestKey key,
                        out var failure))
                {
                    return _projectionFailures.TryGetValue(
                            TypeResolutionManifestKey.From(request),
                            out TypeResolutionOutcome? projectionOutcome)
                        ? projectionOutcome
                        : new TypeResolutionOutcome.Rejected(
                            failure!,
                            ImmutableArray<TypeForwardingHop>.Empty);
                }

                return _outcomes.TryGetValue(
                        key,
                        out TypeResolutionOutcome? outcome)
                    ? outcome
                    : new TypeResolutionOutcome.Rejected(
                        new TypeResolutionFailure.PlanExpansionRequired(
                            new ResolutionPlanRequest.Type(request)),
                        ImmutableArray<TypeForwardingHop>.Empty);
            }
        }
    }

    /// <summary>
    /// Returns the frozen binding answer for <paramref name="request"/>, or an
    /// expansion result when the request was not in this generation's
    /// manifest.
    /// </summary>
    public AssemblyBindingOutcome Bind(AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            lock (_catalog.LifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _catalog.EnsureAlive();

                if (!TryProjectBinding(
                        request.Target,
                        request.Origin,
                        request.Scope,
                        out BindingKey key,
                        out TypeResolutionFailure? failure))
                {
                    return failure
                        is TypeResolutionFailure.UnregisteredAssembly
                        ? new AssemblyBindingOutcome.ExpansionRequired(request)
                        : new AssemblyBindingOutcome.Unavailable(
                            new AssemblyBindingFailure(
                                AssemblyBindingFailureKind.CandidateUnavailable));
                }

                return _bindings.TryGetValue(
                        key,
                        out AssemblyBindingOutcome? outcome)
                    ? outcome
                    : new AssemblyBindingOutcome.ExpansionRequired(request);
            }
        }
    }

    bool TryProjectRequest(
        TypeResolutionRequest request,
        out RequestKey key,
        out TypeResolutionFailure? failure)
    {
        key = null!;
        failure = null;
        switch (request.Start)
        {
            case TypeResolutionStart.Assembly assembly:
                if (!TryCandidate(
                        assembly.Value.Registration,
                        out ResolvedAssemblyCandidate candidate,
                        out failure))
                {
                    return false;
                }

                key = new RequestKey.Assembly(
                    candidate.Id,
                    assembly.Scope,
                    request.Type);
                return true;

            case TypeResolutionStart.Reference reference:
                if (!TryProjectBinding(
                        AssemblyBindingTarget.Reference(reference.Value),
                        reference.Origin,
                        reference.Scope,
                        out BindingKey referenceKey,
                        out failure))
                {
                    return false;
                }

                key = new RequestKey.Binding(referenceKey, request.Type);
                return true;

            case TypeResolutionStart.CoreLibrary coreLibrary:
                if (!TryProjectBinding(
                        AssemblyBindingTarget.CoreLibrary(),
                        coreLibrary.Origin,
                        coreLibrary.Scope,
                        out BindingKey coreKey,
                        out failure))
                {
                    return false;
                }

                key = new RequestKey.Binding(coreKey, request.Type);
                return true;

            case TypeResolutionStart.Module module:
                if (!TryCandidate(
                        module.Origin.Registration,
                        out ResolvedAssemblyCandidate moduleCandidate,
                        out failure))
                {
                    return false;
                }

                key = new RequestKey.Module(
                    moduleCandidate.Id,
                    module.Name,
                    request.Type);
                return true;

            default:
                throw new InvalidOperationException(
                    "Unknown type-resolution start.");
        }
    }

    bool TryCandidate(
        AssemblyAcquisitionRegistration registration,
        out ResolvedAssemblyCandidate candidate,
        out TypeResolutionFailure? failure)
    {
        candidate = null!;
        if (_candidates.TryGetValue(
                registration,
                out ResolvedAssemblyCandidate? found)
            && found is not null)
        {
            candidate = found;
            failure = null;
            return true;
        }

        if (_registrationFailures.TryGetValue(
                registration,
                out CandidateOpenFailure? openFailure))
        {
            failure = CandidateFailure(
                FindDescriptor(registration),
                openFailure,
                _catalog.MaxCandidates);
            return false;
        }

        failure = new TypeResolutionFailure.UnregisteredAssembly(registration);
        candidate = null!;
        return false;
    }

    ResolvedAssemblyReference FindDescriptor(
        AssemblyAcquisitionRegistration registration)
    {
        return _descriptors.TryGetValue(registration, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException(
                "A registration failure lost its assembly descriptor.");
    }

    static TypeResolutionFailure CandidateFailure(
        ResolvedAssemblyReference assembly,
        CandidateOpenFailure failure,
        int maxCandidates) =>
        failure.Kind == CandidateOpenFailureKind.ResourceBudget
            ? new TypeResolutionFailure.DiscoveryBudgetExceeded(
                maxCandidates)
            : new TypeResolutionFailure.CandidateOpenFailed(
                assembly,
                failure);

    bool TryProjectBinding(
        AssemblyBindingTarget target,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope,
        out BindingKey key,
        out TypeResolutionFailure? failure) =>
        TryCreateBindingKey(
            _candidates,
            _registrationFailures,
            _descriptors,
            _catalog.MaxCandidates,
            target,
            origin,
            scope,
            out key,
            out failure);

    static bool TryCreateBindingKey(
        Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyCandidate> candidates,
        Dictionary<
            AssemblyAcquisitionRegistration,
            CandidateOpenFailure> registrationFailures,
        Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyReference> descriptors,
        int maxCandidates,
        AssemblyBindingTarget target,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope,
        out BindingKey key,
        out TypeResolutionFailure? failure)
    {
        AssemblyBindingDomainKey domain;
        switch (origin)
        {
            case AssemblyBindingOrigin.GlobalOrigin:
                domain = AssemblyBindingDomainKey.Global;
                break;
            case AssemblyBindingOrigin.RequestingAssembly requesting:
                if (!candidates.TryGetValue(
                        requesting.Registration,
                        out ResolvedAssemblyCandidate? candidate))
                {
                    key = null!;
                    failure = registrationFailures.TryGetValue(
                            requesting.Registration,
                            out CandidateOpenFailure? openFailure)
                        ? CandidateFailure(
                            descriptors[requesting.Registration],
                            openFailure,
                            maxCandidates)
                        : new TypeResolutionFailure.UnregisteredAssembly(
                            requesting.Registration);
                    return false;
                }

                domain = AssemblyBindingDomainKey.FromCandidate(candidate!.Id);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown assembly-binding origin.");
        }

        key = new BindingKey(domain, target, scope);
        failure = null;
        return true;
    }

    /// <summary>
    /// Releases this generation and, for standalone contexts created by
    /// <see cref="Create"/>, its private catalog.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _candidates.Clear();
            _registrationFailures.Clear();
            _descriptors.Clear();
            _outcomes =
                ImmutableDictionary<RequestKey, TypeResolutionOutcome>.Empty;
            _projectionFailures =
                ImmutableDictionary<
                    TypeResolutionManifestKey,
                    TypeResolutionOutcome>.Empty;
            _bindings =
                ImmutableDictionary<BindingKey, AssemblyBindingOutcome>.Empty;
            _inventories =
                ImmutableDictionary<
                    AssemblyCandidateId,
                    AssemblyInventorySnapshot>.Empty;
        }

        if (_ownsCatalog)
            _catalog.Dispose();
    }

    abstract record RequestKey(MetadataTypeDefinitionName Type)
    {
        internal sealed record Assembly(
            AssemblyCandidateId Candidate,
            AssemblyResolutionScope Scope,
            MetadataTypeDefinitionName Name) : RequestKey(Name);

        internal sealed record Binding(
            BindingKey BindingKey,
            MetadataTypeDefinitionName Name) : RequestKey(Name);

        internal sealed record Module(
            AssemblyCandidateId Candidate,
            string ModuleName,
            MetadataTypeDefinitionName Name) : RequestKey(Name);
    }

    readonly record struct OriginKey(
        bool IsGlobal,
        AssemblyAcquisitionRegistration? Registration)
    {
        internal static OriginKey From(AssemblyBindingOrigin origin) =>
            origin switch
            {
                AssemblyBindingOrigin.GlobalOrigin => new(true, null),
                AssemblyBindingOrigin.RequestingAssembly requesting =>
                    new(false, requesting.Registration),
                _ => throw new InvalidOperationException(
                    "Unknown assembly-binding origin."),
            };
    }

    sealed class Builder
    {
        readonly TypeResolutionCatalog _catalog;
        readonly InspectionAcquisitionPlan _acquisition;
        readonly IAssemblyBindingPolicy _policy;
        readonly AssemblyBindingPolicyVersion _policyVersion;
        readonly int _maxForwarderHops;
        readonly int _maxCandidates;
        readonly int _maxTypeResolutionRequests;
        readonly bool _ownsCatalog;
        readonly CancellationToken _cancellationToken;
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyCandidate> _candidates =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            CandidateOpenFailure> _registrationFailures =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            CandidateOpenFailure> _strictRegistrationFailures =
                new(ReferenceEqualityComparer.Instance);
        readonly HashSet<AssemblyAcquisitionRegistration>
            _strictlyValidatedRegistrations =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyReference> _descriptors =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<AssemblyCandidateId, AssemblyInventorySnapshot>
            _inventories = [];
        readonly Dictionary<BindingKey, CachedBindingEvaluation> _bindings = [];
        readonly Dictionary<RequestKey, TypeResolutionOutcome> _outcomes = [];
        readonly Dictionary<
            TypeResolutionManifestKey,
            TypeResolutionOutcome> _projectionFailures = [];
        readonly HashSet<RequestKey> _budgetedRequests = [];
        readonly AssemblyCatalogGenerationId _generation =
            new();

        abstract record CoreResolutionStep
        {
            internal sealed record Completed(
                TypeResolutionOutcome Outcome) : CoreResolutionStep;

            internal sealed record Dependency(
                TypeResolutionRequest Request,
                RequestKey Key,
                int GenericArgumentCount) : CoreResolutionStep;
        }

        readonly record struct KindResolutionFrame(
            TypeResolutionRequest Request,
            RequestKey Key,
            int GenericArgumentCount);

        readonly record struct KindAuthenticationResult(
            MetadataTypeDefinitionKind Kind,
            TypeResolutionFailure? Failure,
            AssemblyReferenceIdentity? DependencyAssembly);

        internal Builder(
            TypeResolutionCatalog catalog,
            IAssemblyBindingPolicy policy,
            int maxForwarderHops,
            int maxCandidates,
            int maxTypeResolutionRequests,
            bool ownsCatalog,
            CancellationToken cancellationToken)
        {
            _catalog = catalog;
            _acquisition = catalog.Acquisition;
            _policy = policy;
            _policyVersion = policy.Version
                ?? throw new ArgumentException(
                    "A binding policy must expose a version.",
                    nameof(policy));
            _maxForwarderHops = maxForwarderHops;
            _maxCandidates = maxCandidates;
            _maxTypeResolutionRequests = maxTypeResolutionRequests;
            _ownsCatalog = ownsCatalog;
            _cancellationToken = cancellationToken;
        }

        internal void Register(
            ResolvedAssemblyReference assembly,
            bool allowRootAdjacencyDegradation = false)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            bool registrationFailed =
                _registrationFailures.ContainsKey(assembly.Registration);
            if (allowRootAdjacencyDegradation
                    ? _candidates.ContainsKey(assembly.Registration)
                        || registrationFailed
                            && !_strictRegistrationFailures.ContainsKey(
                                assembly.Registration)
                    : _strictlyValidatedRegistrations.Contains(
                            assembly.Registration)
                        || registrationFailed)
            {
                return;
            }

            _descriptors.TryAdd(assembly.Registration, assembly);
            CandidateRegistrationResult registration =
                allowRootAdjacencyDegradation
                    ? _acquisition.RegisterRoot(assembly)
                    : _acquisition.Register(assembly);
            switch (registration)
            {
                case CandidateRegistrationResult.Ready ready:
                    if (allowRootAdjacencyDegradation)
                    {
                        _registrationFailures.Remove(
                            assembly.Registration);
                    }
                    _candidates.TryAdd(
                        assembly.Registration,
                        ready.Candidate);
                    _inventories.TryAdd(
                        ready.Candidate.Id,
                        ready.Inventory);
                    if (!allowRootAdjacencyDegradation)
                    {
                        _strictlyValidatedRegistrations.Add(
                            assembly.Registration);
                    }
                    break;
                case CandidateRegistrationResult.Rejected rejected:
                    if (allowRootAdjacencyDegradation)
                    {
                        _registrationFailures.TryAdd(
                            assembly.Registration,
                            rejected.Failure);
                    }
                    else
                    {
                        _strictlyValidatedRegistrations.Add(
                            assembly.Registration);
                        _strictRegistrationFailures.TryAdd(
                            assembly.Registration,
                            rejected.Failure);
                        if (!_candidates.ContainsKey(
                                assembly.Registration))
                        {
                            _registrationFailures.TryAdd(
                                assembly.Registration,
                                rejected.Failure);
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown candidate-registration result.");
            }
        }

        internal void Add(TypeResolutionRequest request)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (request.Start is TypeResolutionStart.Assembly assembly)
                Register(assembly.Value);

            if (!TryProjectRequest(
                    request,
                    out RequestKey key,
                    out TypeResolutionOutcome? projectionFailure))
            {
                TypeResolutionManifestKey manifestKey =
                    TypeResolutionManifestKey.From(request);
                _projectionFailures.TryAdd(
                    manifestKey,
                    projectionFailure!);
                return;
            }

            if (_outcomes.ContainsKey(key))
                return;

            if (!TryConsumeRequest(key))
            {
                _outcomes.Add(
                    key,
                    Rejected(
                        new TypeResolutionFailure.RequestBudgetExceeded(
                            _maxTypeResolutionRequests)));
                return;
            }

            if (_catalog.TryGetResolution(
                    _policyVersion,
                    key,
                    out TypeResolutionOutcome? cached))
            {
                SeedRecipe(key, cached!);
                _outcomes.Add(
                    key,
                    UsesStrictlyRejectedCandidate(cached!)
                        ? ResolveCore(request)
                        : Reproject(cached!));
                return;
            }

            TypeResolutionOutcome outcome = ResolveCore(request);
            _outcomes.Add(key, outcome);
        }

        // A catalog-level resolution cache entry names candidate-based keys.
        // Rehydrate every binding and descriptor dependency into this
        // generation before reprojecting its definition key.
        void SeedRecipe(
            RequestKey requestKey,
            TypeResolutionOutcome outcome)
        {
            if (requestKey is RequestKey.Binding binding)
                SeedBinding(binding.BindingKey);

            // HopBudgetExceeded records the terminal evidence hop without
            // evaluating its target binding, so that hop has no cache
            // dependency to seed.
            int bindingHopCount =
                outcome is TypeResolutionOutcome.Rejected
                {
                    Failure:
                        TypeResolutionFailure.HopBudgetExceeded,
                }
                    ? outcome.Hops.Length - 1
                    : outcome.Hops.Length;
            for (int i = 0; i < bindingHopCount; i++)
            {
                TypeForwardingHop hop = outcome.Hops[i];
                Register(hop.SourceAssembly.Assembly);
                if (TryBindingKey(
                        AssemblyBindingTarget.Reference(
                            hop.TargetReference),
                        AssemblyBindingOrigin.FromAssembly(
                            hop.SourceAssembly.Assembly),
                        hop.Scope,
                        out BindingKey key,
                        out _))
                {
                    SeedBinding(key);
                }
            }

            switch (outcome)
            {
                case TypeResolutionOutcome.Resolved resolved:
                    Register(resolved.Definition.Assembly.Assembly);
                    break;
                case TypeResolutionOutcome.NotFound notFound:
                    Register(notFound.LastAssembly.Assembly);
                    break;
            }
        }

        void SeedBinding(BindingKey key)
        {
            if (_bindings.ContainsKey(key))
                return;
            if (!_catalog.TryGetBinding(
                    _policyVersion,
                    key,
                    out CachedBindingEvaluation? evaluation))
            {
                throw new InvalidOperationException(
                    "A cached resolution recipe lost a binding dependency.");
            }

            RegisterEvaluationCandidates(evaluation!);
            _bindings.Add(
                key,
                RevalidateCachedBinding(evaluation!));
        }

        void RegisterEvaluationCandidates(
            CachedBindingEvaluation evaluation)
        {
            if (evaluation.Registrations.IsDefaultOrEmpty)
                return;

            foreach (ResolvedAssemblyReference assembly
                in evaluation.Registrations)
            {
                Register(assembly);
            }
        }

        internal void Add(AssemblyBindingRequest request)
        {
            // Forwarder inventories form attacker-controlled graphs. Use an
            // explicit worklist so discovery depth does not consume the
            // process stack.
            var pending = new Stack<AssemblyBindingRequest>();
            pending.Push(request);
            while (pending.TryPop(out AssemblyBindingRequest? current))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!TryBindingKey(
                        current.Target,
                        current.Origin,
                        current.Scope,
                        out BindingKey key,
                        out _)
                    || _bindings.ContainsKey(key))
                {
                    continue;
                }

                CachedBindingEvaluation evaluation =
                    EvaluateBinding(key, current);
                if (evaluation.Outcome
                    is not AssemblyBindingOutcome.Resolved resolved)
                {
                    continue;
                }

                AssemblyInventorySnapshot inventory =
                    _inventories[resolved.Candidate.Id];
                for (int i = inventory.ForwarderTargets.Length - 1;
                    i >= 0;
                    i--)
                {
                    AssemblyReferenceIdentity target =
                        inventory.ForwarderTargets[i];
                    pending.Push(
                        new AssemblyBindingRequest(
                            AssemblyBindingTarget.Reference(target),
                            AssemblyBindingOrigin.FromAssembly(
                                resolved.Candidate.Assembly),
                            TightenScope(current.Scope, target)));
                }

            }
        }

        internal TypeResolutionContextBuildResult Freeze()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            AssemblyBindingPolicyVersion? observedVersion = _policy.Version;
            if (!ReferenceEquals(_policyVersion, observedVersion))
            {
                return new TypeResolutionContextBuildResult
                    .PolicyVersionChanged(
                        _policyVersion,
                        observedVersion);
            }

            // Policy-dependent work is promoted only after the whole
            // generation completes under the policy version captured at its
            // start. Canceled or version-racing generations publish nothing.
            foreach (KeyValuePair<BindingKey, CachedBindingEvaluation> pair
                in _bindings)
            {
                _catalog.PromoteBinding(
                    _policyVersion,
                    pair.Key,
                    pair.Value);
            }
            foreach (KeyValuePair<RequestKey, TypeResolutionOutcome> pair
                in _outcomes)
            {
                if (pair.Value is TypeResolutionOutcome.Rejected
                    {
                        Failure:
                            TypeResolutionFailure.RequestBudgetExceeded,
                    }
                    or TypeResolutionOutcome.Resolved
                    {
                        Definition.KindResolutionFailure: not null,
                    })
                {
                    continue;
                }

                _catalog.PromoteResolution(
                    _policyVersion,
                    pair.Key,
                    pair.Value);
            }
            _catalog.PublishGeneration(
                _generation,
                _candidates,
                _inventories);
            return new TypeResolutionContextBuildResult.Completed(
                new TypeResolutionContext(
                    _catalog,
                    _ownsCatalog,
                    _generation,
                    _candidates,
                    _registrationFailures,
                    _descriptors,
                    _outcomes.ToImmutableDictionary(),
                    _projectionFailures.ToImmutableDictionary(),
                    _bindings.ToImmutableDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value.Outcome),
                    _inventories.ToImmutableDictionary()));
        }

        bool TryProjectRequest(
            TypeResolutionRequest request,
            out RequestKey key,
            out TypeResolutionOutcome? failure)
        {
            key = null!;
            failure = null;
            switch (request.Start)
            {
                case TypeResolutionStart.Assembly assembly:
                    if (!TryCandidate(
                            assembly.Value.Registration,
                            out ResolvedAssemblyCandidate candidate,
                            out TypeResolutionFailure? candidateFailure))
                    {
                        failure = Rejected(candidateFailure!);
                        return false;
                    }

                    key = new RequestKey.Assembly(
                        candidate.Id,
                        assembly.Scope,
                        request.Type);
                    return true;

                case TypeResolutionStart.Reference reference:
                    if (!TryBindingKey(
                            AssemblyBindingTarget.Reference(reference.Value),
                            reference.Origin,
                            reference.Scope,
                            out BindingKey referenceKey,
                            out TypeResolutionFailure? referenceFailure))
                    {
                        failure = Rejected(referenceFailure!);
                        return false;
                    }

                    key = new RequestKey.Binding(referenceKey, request.Type);
                    return true;

                case TypeResolutionStart.CoreLibrary coreLibrary:
                    if (!TryBindingKey(
                            AssemblyBindingTarget.CoreLibrary(),
                            coreLibrary.Origin,
                            coreLibrary.Scope,
                            out BindingKey coreKey,
                            out TypeResolutionFailure? coreFailure))
                    {
                        failure = Rejected(coreFailure!);
                        return false;
                    }

                    key = new RequestKey.Binding(coreKey, request.Type);
                    return true;

                case TypeResolutionStart.Module module:
                    if (!TryCandidate(
                            module.Origin.Registration,
                            out ResolvedAssemblyCandidate moduleCandidate,
                            out TypeResolutionFailure? moduleFailure))
                    {
                        failure = Rejected(moduleFailure!);
                        return false;
                    }

                    key = new RequestKey.Module(
                        moduleCandidate.Id,
                        module.Name,
                        request.Type);
                    return true;

                default:
                    throw new InvalidOperationException(
                        "Unknown type-resolution start.");
            }
        }

        TypeResolutionOutcome ResolveCore(TypeResolutionRequest request)
        {
            if (!TryProjectRequest(
                    request,
                    out RequestKey currentKey,
                    out TypeResolutionOutcome? projectionFailure))
            {
                return projectionFailure!;
            }

            var pending = new Stack<KindResolutionFrame>();
            var active = new HashSet<RequestKey> { currentKey };
            var authenticatedKinds =
                new Dictionary<RequestKey, KindAuthenticationResult>();
            TypeResolutionRequest currentRequest = request;

            while (true)
            {
                CoreResolutionStep step =
                    ResolveCoreStep(
                        currentRequest,
                        currentKey,
                        active,
                        authenticatedKinds);
                if (step is CoreResolutionStep.Dependency dependency)
                {
                    pending.Push(
                        new KindResolutionFrame(
                            currentRequest,
                            currentKey,
                            dependency.GenericArgumentCount));
                    active.Add(dependency.Key);
                    currentRequest = dependency.Request;
                    currentKey = dependency.Key;
                    continue;
                }

                TypeResolutionOutcome outcome =
                    ((CoreResolutionStep.Completed)step).Outcome;
                active.Remove(currentKey);
                if (pending.Count == 0)
                    return outcome;

                _outcomes.TryAdd(currentKey, outcome);
                if (outcome is TypeResolutionOutcome.Rejected
                    {
                        Failure:
                            TypeResolutionFailure.RequestBudgetExceeded,
                    } rejected)
                {
                    return Rejected(rejected.Failure);
                }

                KindResolutionFrame frame = pending.Pop();
                authenticatedKinds[frame.Key] =
                    AuthenticateKindFromOutcome(
                        currentRequest,
                        outcome,
                        frame.GenericArgumentCount);
                currentRequest = frame.Request;
                currentKey = frame.Key;
            }
        }

        CoreResolutionStep ResolveCoreStep(
            TypeResolutionRequest request,
            RequestKey requestKey,
            HashSet<RequestKey> active,
            Dictionary<RequestKey, KindAuthenticationResult>
                authenticatedKinds)
        {
            var hops = ImmutableArray.CreateBuilder<TypeForwardingHop>();
            ResolvedAssemblyCandidate current;
            AssemblyResolutionScope scope;

            switch (request.Start)
            {
                case TypeResolutionStart.Assembly assembly:
                    if (!TryCandidate(
                            assembly.Value.Registration,
                            out current!,
                            out TypeResolutionFailure? candidateFailure))
                    {
                        return Completed(
                            Rejected(candidateFailure!, hops));
                    }
                    scope = assembly.Scope;
                    break;

                case TypeResolutionStart.Reference reference:
                    if (!TrySelect(
                            AssemblyBindingTarget.Reference(reference.Value),
                            reference.Origin,
                            reference.Scope,
                            hops,
                            out current!,
                            out TypeResolutionOutcome? referenceOutcome))
                    {
                        return Completed(referenceOutcome!);
                    }
                    scope = reference.Scope;
                    break;

                case TypeResolutionStart.CoreLibrary coreLibrary:
                    if (!TrySelect(
                            AssemblyBindingTarget.CoreLibrary(),
                            coreLibrary.Origin,
                            coreLibrary.Scope,
                            hops,
                            out current!,
                            out TypeResolutionOutcome? coreOutcome))
                    {
                        return Completed(coreOutcome!);
                    }
                    scope = coreLibrary.Scope;
                    break;

                case TypeResolutionStart.Module module:
                    return Completed(
                        Rejected(
                            new TypeResolutionFailure
                                .UnsupportedModuleReference(
                                    module.Name),
                            hops));

                default:
                    throw new InvalidOperationException(
                        "Unknown type-resolution start.");
            }

            var visited = new HashSet<AssemblyCandidateId>();
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(current.Id))
                {
                    return Completed(
                        Rejected(
                            new TypeResolutionFailure.ForwarderCycle(),
                            hops));
                }

                if (!_catalog.TryGetDeclaration(
                        current.Id,
                        request.Type,
                        out TypeDeclarationResult? declaration))
                {
                    CandidateSessionResult sessionResult =
                        _acquisition.OpenSession(current);
                    if (sessionResult
                        is CandidateSessionResult.Rejected sessionRejected)
                    {
                        return Completed(
                            Rejected(
                                new TypeResolutionFailure
                                    .CandidateOpenFailed(
                                        current.Assembly,
                                        sessionRejected.Failure),
                                hops));
                    }

                    var ready = (CandidateSessionResult.Ready)sessionResult;
                    declaration = ready.Session.ProbeDeclaration(request.Type);
                    _catalog.AddDeclaration(
                        current.Id,
                        request.Type,
                        declaration);
                }

                switch (declaration)
                {
                    case TypeDeclarationResult.Defined defined:
                        AssemblyInventorySnapshot inventory =
                            _inventories[current.Id];
                        MetadataTypeDefinitionKind kind = defined.Kind;
                        TypeResolutionFailure? kindResolutionFailure =
                            null;
                        AssemblyReferenceIdentity?
                            kindResolutionDependencyAssembly =
                                null;
                        if (kind
                                == MetadataTypeDefinitionKind.Unknown
                            && defined.KindDependency is { } dependency)
                        {
                            if (authenticatedKinds.TryGetValue(
                                    requestKey,
                                    out KindAuthenticationResult
                                        authenticated))
                            {
                                kind = authenticated.Kind;
                                kindResolutionFailure =
                                    authenticated.Failure;
                                kindResolutionDependencyAssembly =
                                    authenticated
                                        .DependencyAssembly;
                            }
                            else
                            {
                                TypeResolutionRequest dependencyRequest =
                                    TypeResolutionRequest.FromReference(
                                        dependency.Reference,
                                        AssemblyBindingOrigin.FromAssembly(
                                            current.Assembly),
                                        dependency.Scope,
                                        dependency.Type);
                                if (!TryProjectRequest(
                                        dependencyRequest,
                                        out RequestKey dependencyKey,
                                        out TypeResolutionOutcome?
                                            dependencyProjectionFailure))
                                {
                                    if (dependencyProjectionFailure
                                        is not TypeResolutionOutcome.Rejected
                                            rejected)
                                    {
                                        throw new InvalidOperationException(
                                            "A projected dependency failure "
                                                + "must be a rejection.");
                                    }

                                    kindResolutionFailure =
                                        rejected.Failure;
                                    kindResolutionDependencyAssembly =
                                        dependencyProjectionFailure
                                            .TerminalAssemblyIdentity;
                                }
                                else if (active.Contains(dependencyKey))
                                {
                                    kind =
                                        MetadataTypeDefinitionKind
                                            .Unknown;
                                    kindResolutionFailure =
                                        new TypeResolutionFailure
                                            .KindDependencyCycle(
                                                new AssemblyBindingTarget
                                                    .AssemblyReference(
                                                        dependency.Reference),
                                                AssemblyBindingOrigin
                                                    .FromAssembly(
                                                        current.Assembly),
                                                dependency.Scope);
                                    kindResolutionDependencyAssembly =
                                        dependency.Reference;
                                }
                                else if (_outcomes.TryGetValue(
                                        dependencyKey,
                                        out TypeResolutionOutcome?
                                            cached))
                                {
                                    if (cached is TypeResolutionOutcome.Rejected
                                        {
                                            Failure:
                                                TypeResolutionFailure
                                                    .RequestBudgetExceeded,
                                        } rejected)
                                    {
                                        return Completed(
                                            Rejected(
                                                rejected.Failure,
                                                hops));
                                    }

                                    KindAuthenticationResult authentication =
                                        AuthenticateKindFromOutcome(
                                        dependencyRequest,
                                        cached,
                                        dependency
                                            .GenericArgumentCount);
                                    kind = authentication.Kind;
                                    kindResolutionFailure =
                                        authentication.Failure;
                                    kindResolutionDependencyAssembly =
                                        authentication
                                            .DependencyAssembly;
                                }
                                else if (!TryConsumeRequest(
                                        dependencyKey))
                                {
                                    return Completed(
                                        Rejected(
                                            new TypeResolutionFailure
                                                .RequestBudgetExceeded(
                                                    _maxTypeResolutionRequests),
                                            hops));
                                }
                                else if (_catalog.TryGetResolution(
                                        _policyVersion,
                                        dependencyKey,
                                        out TypeResolutionOutcome?
                                            catalogCached))
                                {
                                    SeedRecipe(
                                        dependencyKey,
                                        catalogCached!);
                                    if (UsesStrictlyRejectedCandidate(
                                            catalogCached!))
                                    {
                                        return new CoreResolutionStep
                                            .Dependency(
                                                dependencyRequest,
                                                dependencyKey,
                                                dependency
                                                    .GenericArgumentCount);
                                    }

                                    TypeResolutionOutcome reprojected =
                                        Reproject(catalogCached!);
                                    _outcomes.Add(
                                        dependencyKey,
                                        reprojected);
                                    if (reprojected
                                        is TypeResolutionOutcome.Rejected
                                        {
                                            Failure:
                                                TypeResolutionFailure
                                                    .RequestBudgetExceeded,
                                        } rejected)
                                    {
                                        return Completed(
                                            Rejected(
                                                rejected.Failure,
                                                hops));
                                    }

                                    KindAuthenticationResult authentication =
                                        AuthenticateKindFromOutcome(
                                        dependencyRequest,
                                        reprojected,
                                        dependency
                                            .GenericArgumentCount);
                                    kind = authentication.Kind;
                                    kindResolutionFailure =
                                        authentication.Failure;
                                    kindResolutionDependencyAssembly =
                                        authentication
                                            .DependencyAssembly;
                                }
                                else
                                {
                                    return new CoreResolutionStep
                                        .Dependency(
                                            dependencyRequest,
                                            dependencyKey,
                                            dependency
                                                .GenericArgumentCount);
                                }
                            }
                        }

                        var key = new ResolvedTypeDefinitionKey(
                            _acquisition.CatalogId,
                            _generation,
                            current.Id,
                            defined.Definition);
                        var address = new MetadataTypeDefinitionAddress(
                            inventory.ModuleVersionId,
                            defined.Definition);
                        return Completed(
                            new TypeResolutionOutcome.Resolved(
                                new ResolvedTypeDefinition(
                                    key,
                                    address,
                                    current,
                                    request.Type,
                                    kind,
                                    defined
                                        .DeclaringAssemblyDefinesCoreLibraryRoot,
                                    defined.GenericParameterCount,
                                    kindResolutionFailure,
                                    kindResolutionDependencyAssembly),
                                hops.ToImmutable()));

                    case TypeDeclarationResult.Missing:
                        return Completed(
                            new TypeResolutionOutcome.NotFound(
                                current,
                                hops.ToImmutable()));

                    case TypeDeclarationResult.Ambiguous ambiguous:
                        return Completed(
                            new TypeResolutionOutcome.Ambiguous(
                                new TypeResolutionAmbiguity.TypeDeclaration(
                                    current,
                                    request.Type,
                                    ambiguous.Candidates),
                                hops.ToImmutable()));

                    case TypeDeclarationResult.Rejected rejected:
                        return Completed(
                            Rejected(
                                new TypeResolutionFailure
                                    .DeclarationRejected(
                                        rejected.Rejection),
                                hops));

                    case TypeDeclarationResult.ExportedFromModule module:
                        return Completed(
                            Rejected(
                                new TypeResolutionFailure
                                    .UnsupportedModuleExport(
                                        module.Module),
                                hops));

                    case TypeDeclarationResult.Forwarded forwarded:
                        scope = TightenScope(scope, forwarded.Target);
                        hops.Add(
                            new TypeForwardingHop(
                                current,
                                forwarded.Declarations,
                                forwarded.Target,
                                scope));
                        if (hops.Count > _maxForwarderHops)
                        {
                            return Completed(
                                Rejected(
                                    new TypeResolutionFailure
                                        .HopBudgetExceeded(
                                            _maxForwarderHops),
                                    hops));
                        }

                        if (!TrySelect(
                                AssemblyBindingTarget.Reference(
                                    forwarded.Target),
                                AssemblyBindingOrigin.FromAssembly(
                                    current.Assembly),
                                scope,
                                hops,
                                out current!,
                                out TypeResolutionOutcome? forwardedOutcome))
                        {
                            return Completed(forwardedOutcome!);
                        }
                        break;

                    default:
                        throw new InvalidOperationException(
                            "Unknown type-declaration result.");
                }
            }
        }

        static CoreResolutionStep.Completed Completed(
            TypeResolutionOutcome outcome) =>
            new(outcome);

        static KindAuthenticationResult AuthenticateKindFromOutcome(
            TypeResolutionRequest request,
            TypeResolutionOutcome outcome,
            int genericArgumentCount)
        {
            MetadataTypeDefinitionKind kind =
                outcome is TypeResolutionOutcome.Resolved
                {
                    Definition.Kind:
                    MetadataTypeDefinitionKind.Class,
                } resolved
                && resolved.Definition.GenericParameterCount
                    == genericArgumentCount
                && !(resolved.Definition
                        .DeclaringAssemblyDefinesCoreLibraryRoot
                    && resolved.Definition.Type
                            .ToMetadataFullName()
                        is "System.ValueType" or "System.Enum")
                ? MetadataTypeDefinitionKind.Class
                : MetadataTypeDefinitionKind.Unknown;
            TypeResolutionFailure? failure = outcome switch
            {
                TypeResolutionOutcome.Resolved resolvedOutcome =>
                    resolvedOutcome.Definition.KindResolutionFailure,
                TypeResolutionOutcome.Rejected rejected =>
                    rejected.Failure,
                TypeResolutionOutcome.UnboundBinding unbound =>
                    new TypeResolutionFailure.KindDependencyUnbound(
                        unbound.Target,
                        unbound.Origin,
                        unbound.Scope),
                TypeResolutionOutcome.Unavailable unavailable =>
                    new TypeResolutionFailure.KindDependencyUnavailable(
                        unavailable.Target,
                        unavailable.Origin,
                        unavailable.Scope,
                        unavailable.Failure),
                TypeResolutionOutcome.NotFound notFound =>
                    new TypeResolutionFailure.KindDependencyTypeNotFound(
                        notFound.LastAssembly,
                        request.Type),
                TypeResolutionOutcome.Ambiguous ambiguous =>
                    new TypeResolutionFailure.KindDependencyAmbiguous(
                        ambiguous.Ambiguity,
                        request.Type),
                _ => null,
            };
            AssemblyReferenceIdentity? dependencyAssembly =
                failure is null
                    ? null
                    : outcome switch
                    {
                        TypeResolutionOutcome.Resolved resolvedOutcome =>
                            resolvedOutcome.Definition
                                .KindResolutionDependencyAssembly,
                        _ => outcome.TerminalAssemblyIdentity,
                    };
            return new KindAuthenticationResult(
                kind,
                failure,
                dependencyAssembly);
        }

        bool TryConsumeRequest(RequestKey key) =>
            _budgetedRequests.Contains(key)
            || (_budgetedRequests.Count < _maxTypeResolutionRequests
                && _budgetedRequests.Add(key));

        bool TrySelect(
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            ImmutableArray<TypeForwardingHop>.Builder hops,
            out ResolvedAssemblyCandidate? candidate,
            out TypeResolutionOutcome? outcome)
        {
            candidate = null;
            outcome = null;
            if (!TryBindingKey(
                    target,
                    origin,
                    scope,
                    out BindingKey key,
                    out TypeResolutionFailure? keyFailure))
            {
                outcome = Rejected(keyFailure!, hops);
                return false;
            }

            CachedBindingEvaluation evaluation = EvaluateBinding(
                key,
                new AssemblyBindingRequest(target, origin, scope));
            switch (evaluation.Outcome)
            {
                case AssemblyBindingOutcome.Resolved resolved:
                    candidate = resolved.Candidate;
                    return true;
                case AssemblyBindingOutcome.Missing:
                    outcome = new TypeResolutionOutcome.UnboundBinding(
                        new UnresolvedBindingReference(
                            _acquisition.CatalogId,
                            _generation,
                            key),
                        target,
                        origin,
                        scope,
                        hops.ToImmutable());
                    return false;
                case AssemblyBindingOutcome.Unavailable unavailable:
                    if (evaluation.OpenFailure is { } openFailure)
                    {
                        outcome = Rejected(
                            CandidateFailure(
                                evaluation.Assembly!,
                                openFailure,
                                _maxCandidates),
                            hops);
                    }
                    else
                    {
                        outcome = new TypeResolutionOutcome.Unavailable(
                            new UnresolvedBindingReference(
                                _acquisition.CatalogId,
                                _generation,
                                key),
                            target,
                            origin,
                            scope,
                            unavailable.Failure,
                            hops.ToImmutable());
                    }
                    return false;
                case AssemblyBindingOutcome.Ambiguous ambiguous:
                    outcome = new TypeResolutionOutcome.Ambiguous(
                        new TypeResolutionAmbiguity.AssemblyBinding(
                            target,
                            origin,
                            scope,
                            ambiguous.Candidates),
                        hops.ToImmutable());
                    return false;
                case AssemblyBindingOutcome.Rejected rejected:
                    outcome = Rejected(
                        new TypeResolutionFailure.InvalidBindingPolicy(
                            rejected.Failure),
                        hops);
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Discovery produced an invalid binding outcome.");
            }
        }

        CachedBindingEvaluation EvaluateBinding(
            BindingKey key,
            AssemblyBindingRequest request)
        {
            if (_bindings.TryGetValue(
                    key,
                    out CachedBindingEvaluation? cached))
                return cached;
            if (_catalog.TryGetBinding(
                    _policyVersion,
                    key,
                    out cached))
            {
                RegisterEvaluationCandidates(cached!);
                CachedBindingEvaluation revalidated =
                    RevalidateCachedBinding(cached!);
                _bindings.Add(key, revalidated);
                return revalidated;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            AssemblyBindingPolicyVersion? observedVersion =
                _policy.Version;
            if (!ReferenceEquals(_policyVersion, observedVersion))
            {
                throw new PolicyVersionChangedException(
                    _policyVersion,
                    observedVersion);
            }

            AssemblyBindingSelectionSnapshot? snapshot =
                _policy.Select(request);
            AssemblyBindingSelection selection;
            if (snapshot is null)
            {
                selection = AssemblyBindingSelection.ValidateForRequest(
                    request,
                    selection: null);
            }
            else
            {
                if (!ReferenceEquals(
                        _policyVersion,
                        snapshot.Version))
                {
                    throw new PolicyVersionChangedException(
                        _policyVersion,
                        snapshot.Version);
                }

                selection = AssemblyBindingSelection.ValidateForRequest(
                    request,
                    snapshot.Selection);
            }

            // Policy returns public descriptors. Registration turns those
            // selections into catalog candidates and a frozen Metadata-owned
            // outcome.
            CachedBindingEvaluation evaluation = selection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    SelectOne(
                        selected.Assembly,
                        selected.ShadowedAssemblies),
                AssemblyBindingSelection.Missing missing =>
                    new(
                        new AssemblyBindingOutcome.Missing(
                            missing.Disposition)),
                AssemblyBindingSelection.Unavailable unavailable =>
                    new(
                        new AssemblyBindingOutcome.Unavailable(
                            unavailable.Failure)),
                AssemblyBindingSelection.Ambiguous ambiguous =>
                    SelectMany(ambiguous.Assemblies),
                AssemblyBindingSelection.Rejected rejected =>
                    new(
                        new AssemblyBindingOutcome.Rejected(
                            rejected.Failure)),
                _ => InvalidBinding(),
            };
            _bindings.Add(key, evaluation);
            return evaluation;
        }

        CachedBindingEvaluation SelectOne(
            ResolvedAssemblyReference assembly,
            ImmutableArray<ResolvedAssemblyReference> shadowedAssemblies)
        {
            Register(assembly);
            if (_strictRegistrationFailures.TryGetValue(
                    assembly.Registration,
                    out CandidateOpenFailure? strictFailure))
            {
                return new(
                    new AssemblyBindingOutcome.Unavailable(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.CandidateUnavailable),
                        shadowedAssemblies),
                    assembly,
                    strictFailure,
                    [assembly]);
            }
            if (_candidates.TryGetValue(
                    assembly.Registration,
                    out ResolvedAssemblyCandidate? candidate))
            {
                return new(
                    new AssemblyBindingOutcome.Resolved(
                        candidate,
                        shadowedAssemblies),
                    Registrations: [assembly]);
            }

            CandidateOpenFailure failure =
                _registrationFailures[assembly.Registration];
            return new(
                new AssemblyBindingOutcome.Unavailable(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable),
                    shadowedAssemblies),
                assembly,
                failure,
                [assembly]);
        }

        CachedBindingEvaluation SelectMany(
            ImmutableArray<ResolvedAssemblyReference> assemblies)
        {
            var candidates =
                ImmutableArray.CreateBuilder<ResolvedAssemblyCandidate>();
            var seen = new HashSet<AssemblyCandidateId>();
            ResolvedAssemblyReference? unavailableAssembly = null;
            CandidateOpenFailure? unavailableFailure = null;
            foreach (ResolvedAssemblyReference assembly in assemblies)
            {
                Register(assembly);
                if (_strictRegistrationFailures.TryGetValue(
                        assembly.Registration,
                        out CandidateOpenFailure? failure)
                    || _registrationFailures.TryGetValue(
                        assembly.Registration,
                        out failure))
                {
                    unavailableAssembly ??= assembly;
                    unavailableFailure ??= failure;
                    continue;
                }

                ResolvedAssemblyCandidate candidate =
                    _candidates[assembly.Registration];
                if (seen.Add(candidate.Id))
                    candidates.Add(candidate);
            }

            if (candidates.Count > 1)
            {
                return new(
                    new AssemblyBindingOutcome.Ambiguous(
                        candidates.ToImmutable()),
                    Registrations: assemblies);
            }

            return unavailableFailure is not null
                ? new(
                    new AssemblyBindingOutcome.Unavailable(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.CandidateUnavailable)),
                    unavailableAssembly,
                    unavailableFailure,
                    assemblies)
                : InvalidBinding();
        }

        CachedBindingEvaluation RevalidateCachedBinding(
            CachedBindingEvaluation evaluation) =>
            evaluation.Outcome switch
            {
                AssemblyBindingOutcome.Resolved resolved =>
                    SelectOne(
                        resolved.Candidate.Assembly,
                        resolved.ShadowedAssemblies),
                AssemblyBindingOutcome.Ambiguous =>
                    SelectMany(evaluation.Registrations),
                _ => evaluation,
            };

        static CachedBindingEvaluation InvalidBinding() =>
            new(
                new AssemblyBindingOutcome.Rejected(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult)));

        bool TryBindingKey(
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            out BindingKey key,
            out TypeResolutionFailure? failure) =>
            TryCreateBindingKey(
                _candidates,
                _registrationFailures,
                _descriptors,
                _maxCandidates,
                target,
                origin,
                scope,
                out key,
                out failure);

        bool TryCandidate(
            AssemblyAcquisitionRegistration registration,
            out ResolvedAssemblyCandidate candidate,
            out TypeResolutionFailure? failure)
        {
            candidate = null!;
            if (_candidates.TryGetValue(
                    registration,
                    out ResolvedAssemblyCandidate? found)
                && found is not null)
            {
                candidate = found;
                failure = null;
                return true;
            }

            if (_registrationFailures.TryGetValue(
                    registration,
                    out CandidateOpenFailure? openFailure))
            {
                failure = CandidateFailure(
                    _descriptors[registration],
                    openFailure,
                    _maxCandidates);
                return false;
            }

            failure =
                new TypeResolutionFailure.UnregisteredAssembly(registration);
            candidate = null!;
            return false;
        }

        static AssemblyResolutionScope TightenScope(
            AssemblyResolutionScope current,
            AssemblyReferenceIdentity target) =>
            current == AssemblyResolutionScope.Platform
                || PlatformKeys.IsPlatform(target.PublicKeyToken)
                    ? AssemblyResolutionScope.Platform
                    : AssemblyResolutionScope.Any;

        static TypeResolutionOutcome.Rejected Rejected(
            TypeResolutionFailure failure,
            ImmutableArray<TypeForwardingHop>.Builder? hops = null) =>
            new(
                failure,
                hops?.ToImmutable()
                    ?? ImmutableArray<TypeForwardingHop>.Empty);

        bool UsesStrictlyRejectedCandidate(
            TypeResolutionOutcome outcome)
        {
            foreach (TypeForwardingHop hop in outcome.Hops)
            {
                if (IsStrictlyRejected(hop.SourceAssembly))
                    return true;
            }

            return outcome switch
            {
                TypeResolutionOutcome.Resolved resolved =>
                    IsStrictlyRejected(
                        resolved.Definition.Assembly),
                TypeResolutionOutcome.NotFound notFound =>
                    IsStrictlyRejected(
                        notFound.LastAssembly),
                TypeResolutionOutcome.Ambiguous
                {
                    Ambiguity:
                        TypeResolutionAmbiguity.AssemblyBinding
                            ambiguity,
                } => ambiguity.Candidates.Any(
                    IsStrictlyRejected),
                TypeResolutionOutcome.Ambiguous
                {
                    Ambiguity:
                        TypeResolutionAmbiguity.TypeDeclaration
                            ambiguity,
                } => IsStrictlyRejected(
                    ambiguity.Assembly),
                _ => false,
            };
        }

        bool IsStrictlyRejected(
            ResolvedAssemblyCandidate candidate) =>
            _strictRegistrationFailures.ContainsKey(
                candidate.Assembly.Registration);

        TypeResolutionOutcome Reproject(TypeResolutionOutcome outcome)
        {
            return outcome switch
            {
                TypeResolutionOutcome.Resolved resolved =>
                    Reproject(resolved),
                TypeResolutionOutcome.UnboundBinding unbound =>
                    new TypeResolutionOutcome.UnboundBinding(
                        Reproject(unbound.Binding),
                        unbound.Target,
                        unbound.Origin,
                        unbound.Scope,
                        unbound.Hops),
                TypeResolutionOutcome.Unavailable unavailable =>
                    new TypeResolutionOutcome.Unavailable(
                        Reproject(unavailable.Binding),
                        unavailable.Target,
                        unavailable.Origin,
                        unavailable.Scope,
                        unavailable.Failure,
                        unavailable.Hops),
                _ => outcome,
            };
        }

        TypeResolutionOutcome.Resolved Reproject(
            TypeResolutionOutcome.Resolved resolved)
        {
            ResolvedTypeDefinition definition = resolved.Definition;
            var key = new ResolvedTypeDefinitionKey(
                _acquisition.CatalogId,
                _generation,
                definition.Assembly.Id,
                definition.Address.Definition);
            return new TypeResolutionOutcome.Resolved(
                new ResolvedTypeDefinition(
                    key,
                    definition.Address,
                    definition.Assembly,
                    definition.Type,
                    definition.Kind,
                    definition.DeclaringAssemblyDefinesCoreLibraryRoot,
                    definition.GenericParameterCount,
                    definition.KindResolutionFailure,
                    definition.KindResolutionDependencyAssembly),
                resolved.Hops);
        }

        UnresolvedBindingReference Reproject(
            UnresolvedBindingReference binding) =>
            new(
                _acquisition.CatalogId,
                _generation,
                binding.Binding);
    }
}
