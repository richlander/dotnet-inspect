using System.Collections.Immutable;

namespace ILInspector.Metadata;

// A policy selection contains public acquisition descriptors. This internal
// value pairs the catalog-interned outcome with everything needed to reproduce
// that outcome in a later generation, including failed selected descriptors.
sealed record CachedBindingEvaluation(
    AssemblyBindingOutcome Outcome,
    ResolvedAssemblyReference? Assembly = null,
    CandidateOpenFailure? OpenFailure = null,
    ImmutableArray<ResolvedAssemblyReference> Registrations = default);

readonly record struct PolicyCacheKey(
    AssemblyBindingPolicyVersion PolicyVersion,
    object RequestKey);

/// <summary>
/// Resource and traversal limits shared by all generations in a
/// <see cref="TypeResolutionCatalog"/>.
/// </summary>
public sealed record TypeResolutionContextOptions
{
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
        TypeForwardResolver.DefaultMaxHops;

    internal InspectionAcquisitionPlanOptions AcquisitionOptions()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetainedImageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxConcurrentSourceOpens);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxForwarderHops);
        return new InspectionAcquisitionPlanOptions
        {
            MaxCandidates = MaxCandidates,
            MaxRetainedImageBytes = MaxRetainedImageBytes,
            MaxConcurrentSourceOpens = MaxConcurrentSourceOpens,
        };
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
    readonly SemaphoreSlim _generationGate = new(1, 1);
    readonly InspectionAcquisitionPlan _acquisition;
    readonly Dictionary<DeclarationCacheKey, TypeDeclarationResult>
        _declarations = [];
    readonly Dictionary<PolicyCacheKey, CachedBindingEvaluation>
        _bindings = [];
    readonly Dictionary<PolicyCacheKey, TypeResolutionOutcome>
        _resolutions = [];
    readonly TypeResolutionContextOptions _options;
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
            cancellationToken);

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
            cancellationToken);

    TypeResolutionContext CreateContextCore(
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<AssemblyBindingRequest> bindingRequests,
        IEnumerable<TypeResolutionRequest> requests,
        bool ownsCatalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(bindingRequests);
        ArgumentNullException.ThrowIfNull(requests);

        _generationGate.Wait(cancellationToken);
        try
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposed, this);

            return TypeResolutionContext.Build(
                this,
                policy,
                roots,
                bindingRequests,
                requests,
                _options,
                ownsCatalog,
                cancellationToken);
        }
        finally
        {
            _generationGate.Release();
        }
    }

    internal InspectionAcquisitionPlan Acquisition => _acquisition;
    internal int MaxCandidates => _options.MaxCandidates;
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

    /// <summary>Releases every retained candidate session owned by the catalog.</summary>
    public void Dispose()
    {
        _generationGate.Wait();
        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            _acquisition.Dispose();
        }
        finally
        {
            _generationGate.Release();
        }
    }

    readonly record struct DeclarationCacheKey(
        AssemblyCandidateId Candidate,
        MetadataTypeDefinitionName Type);
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
    readonly ImmutableDictionary<RequestKey, TypeResolutionOutcome> _outcomes;
    readonly ImmutableDictionary<
        ManifestRequestKey,
        TypeResolutionOutcome> _projectionFailures;
    readonly ImmutableDictionary<BindingKey, AssemblyBindingOutcome> _bindings;
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
            ManifestRequestKey,
            TypeResolutionOutcome> projectionFailures,
        ImmutableDictionary<BindingKey, AssemblyBindingOutcome> bindings)
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
    }

    /// <summary>Gets the owning catalog's inspection-lifetime identity.</summary>
    public AssemblyCatalogId Catalog => _catalog.Id;

    /// <summary>Gets this frozen manifest generation's identity.</summary>
    public AssemblyCatalogGenerationId Generation { get; }

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

    internal static TypeResolutionContext Build(
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy policy,
        IEnumerable<ResolvedAssemblyReference> roots,
        IEnumerable<AssemblyBindingRequest> bindingRequests,
        IEnumerable<TypeResolutionRequest> requests,
        TypeResolutionContextOptions options,
        bool ownsCatalog,
        CancellationToken cancellationToken)
    {
        var builder = new Builder(
            catalog,
            policy,
            options.MaxForwarderHops,
            options.MaxCandidates,
            ownsCatalog,
            cancellationToken);
        foreach (ResolvedAssemblyReference root in roots)
        {
            ArgumentNullException.ThrowIfNull(root);
            builder.Register(root);
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
                            ManifestRequestKey.From(request),
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
        BindingDomainKey domain;
        switch (origin)
        {
            case AssemblyBindingOrigin.GlobalOrigin:
                domain = BindingDomainKey.Global;
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

                domain = BindingDomainKey.FromCandidate(candidate!.Id);
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

    abstract record ManifestRequestKey(MetadataTypeDefinitionName Type)
    {
        internal static ManifestRequestKey From(TypeResolutionRequest request) =>
            request.Start switch
            {
                TypeResolutionStart.Assembly assembly =>
                    new Assembly(
                        assembly.Value.Registration,
                        assembly.Scope,
                        request.Type),
                TypeResolutionStart.Reference reference =>
                    new Binding(
                        reference.Value,
                        OriginKey.From(reference.Origin),
                        reference.Scope,
                        request.Type),
                TypeResolutionStart.CoreLibrary coreLibrary =>
                    new CoreLibrary(
                        coreLibrary.Origin.Registration,
                        coreLibrary.Scope,
                        request.Type),
                TypeResolutionStart.Module module =>
                    new Module(
                        module.Origin.Registration,
                        module.Name,
                        request.Type),
                _ => throw new InvalidOperationException(
                    "Unknown type-resolution start."),
            };

        internal sealed record Assembly(
            AssemblyAcquisitionRegistration Registration,
            AssemblyResolutionScope Scope,
            MetadataTypeDefinitionName Name) : ManifestRequestKey(Name);

        internal sealed record Binding(
            AssemblyReferenceIdentity Reference,
            OriginKey Origin,
            AssemblyResolutionScope Scope,
            MetadataTypeDefinitionName Name) : ManifestRequestKey(Name);

        internal sealed record CoreLibrary(
            AssemblyAcquisitionRegistration Registration,
            AssemblyResolutionScope Scope,
            MetadataTypeDefinitionName Name) : ManifestRequestKey(Name);

        internal sealed record Module(
            AssemblyAcquisitionRegistration Registration,
            string ModuleName,
            MetadataTypeDefinitionName Name) : ManifestRequestKey(Name);
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

    readonly record struct BindingDomainKey(
        bool IsGlobal,
        AssemblyCandidateId Candidate)
    {
        internal static BindingDomainKey Global => new(true, default);
        internal static BindingDomainKey FromCandidate(
            AssemblyCandidateId candidate) =>
            new(false, candidate);
    }

    sealed record BindingKey(
        BindingDomainKey Domain,
        AssemblyBindingTarget Target,
        AssemblyResolutionScope Scope);

    sealed class Builder
    {
        readonly TypeResolutionCatalog _catalog;
        readonly InspectionAcquisitionPlan _acquisition;
        readonly IAssemblyBindingPolicy _policy;
        readonly AssemblyBindingPolicyVersion _policyVersion;
        readonly int _maxForwarderHops;
        readonly int _maxCandidates;
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
            ResolvedAssemblyReference> _descriptors =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<AssemblyCandidateId, AssemblyInventorySnapshot>
            _inventories = [];
        readonly Dictionary<BindingKey, CachedBindingEvaluation> _bindings = [];
        readonly Dictionary<RequestKey, TypeResolutionOutcome> _outcomes = [];
        readonly Dictionary<
            ManifestRequestKey,
            TypeResolutionOutcome> _projectionFailures = [];
        readonly AssemblyCatalogGenerationId _generation =
            new();

        internal Builder(
            TypeResolutionCatalog catalog,
            IAssemblyBindingPolicy policy,
            int maxForwarderHops,
            int maxCandidates,
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
            _ownsCatalog = ownsCatalog;
            _cancellationToken = cancellationToken;
        }

        internal void Register(ResolvedAssemblyReference assembly)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_candidates.ContainsKey(assembly.Registration)
                || _registrationFailures.ContainsKey(assembly.Registration))
            {
                return;
            }

            _descriptors.Add(assembly.Registration, assembly);
            switch (_acquisition.Register(assembly))
            {
                case CandidateRegistrationResult.Ready ready:
                    _candidates.Add(assembly.Registration, ready.Candidate);
                    _inventories.Add(ready.Candidate.Id, ready.Inventory);
                    break;
                case CandidateRegistrationResult.Rejected rejected:
                    _registrationFailures.Add(
                        assembly.Registration,
                        rejected.Failure);
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
                ManifestRequestKey manifestKey =
                    ManifestRequestKey.From(request);
                _projectionFailures.TryAdd(
                    manifestKey,
                    projectionFailure!);
                return;
            }

            if (_outcomes.ContainsKey(key))
                return;

            if (_catalog.TryGetResolution(
                    _policyVersion,
                    key,
                    out TypeResolutionOutcome? cached))
            {
                SeedRecipe(key, cached!);
                _outcomes.Add(key, Reproject(cached!));
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

            _bindings.Add(key, evaluation!);
            RegisterEvaluationCandidates(evaluation!);
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

        internal TypeResolutionContext Freeze()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            EnsurePolicyVersion();

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
                _catalog.PromoteResolution(
                    _policyVersion,
                    pair.Key,
                    pair.Value);
            }
            return new TypeResolutionContext(
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
                    static pair => pair.Value.Outcome));
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
                        return Rejected(candidateFailure!, hops);
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
                        return referenceOutcome!;
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
                        return coreOutcome!;
                    }
                    scope = coreLibrary.Scope;
                    break;

                case TypeResolutionStart.Module module:
                    return Rejected(
                        new TypeResolutionFailure.UnsupportedModuleReference(
                            module.Name),
                        hops);

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
                    return Rejected(
                        new TypeResolutionFailure.ForwarderCycle(),
                        hops);
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
                        return Rejected(
                            new TypeResolutionFailure.CandidateOpenFailed(
                                current.Assembly,
                                sessionRejected.Failure),
                            hops);
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
                        var key = new ResolvedTypeDefinitionKey(
                            _acquisition.CatalogId,
                            _generation,
                            current.Id,
                            defined.Definition);
                        var address = new MetadataTypeDefinitionAddress(
                            inventory.ModuleVersionId,
                            defined.Definition);
                        return new TypeResolutionOutcome.Resolved(
                            new ResolvedTypeDefinition(
                                key,
                                address,
                                current,
                                request.Type),
                            hops.ToImmutable());

                    case TypeDeclarationResult.Missing:
                        return new TypeResolutionOutcome.NotFound(
                            current,
                            hops.ToImmutable());

                    case TypeDeclarationResult.Ambiguous ambiguous:
                        return new TypeResolutionOutcome.Ambiguous(
                            new TypeResolutionAmbiguity.TypeDeclaration(
                                current,
                                request.Type,
                                ambiguous.Candidates),
                            hops.ToImmutable());

                    case TypeDeclarationResult.Rejected rejected:
                        return Rejected(
                            new TypeResolutionFailure.DeclarationRejected(
                                rejected.Rejection),
                            hops);

                    case TypeDeclarationResult.ExportedFromModule module:
                        return Rejected(
                            new TypeResolutionFailure.UnsupportedModuleExport(
                                module.Module),
                            hops);

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
                            return Rejected(
                                new TypeResolutionFailure.HopBudgetExceeded(
                                    _maxForwarderHops),
                                hops);
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
                            return forwardedOutcome!;
                        }
                        break;

                    default:
                        throw new InvalidOperationException(
                            "Unknown type-declaration result.");
                }
            }
        }

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
                _bindings.Add(key, cached!);
                RegisterEvaluationCandidates(cached!);
                return cached!;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            if (!HasPolicyVersion())
                return CacheInvalidBinding(key);
            AssemblyBindingSelection? selection = _policy.Select(request);
            if (selection is null || !HasPolicyVersion())
                return CacheInvalidBinding(key);

            // Policy returns public descriptors. Registration turns those
            // selections into catalog candidates and a frozen Metadata-owned
            // outcome.
            CachedBindingEvaluation evaluation = selection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    SelectOne(selected.Assembly),
                AssemblyBindingSelection.Missing =>
                    new(new AssemblyBindingOutcome.Missing()),
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

        CachedBindingEvaluation CacheInvalidBinding(BindingKey key)
        {
            CachedBindingEvaluation evaluation = InvalidBinding();
            _bindings.Add(key, evaluation);
            return evaluation;
        }

        CachedBindingEvaluation SelectOne(ResolvedAssemblyReference assembly)
        {
            Register(assembly);
            if (_candidates.TryGetValue(
                    assembly.Registration,
                    out ResolvedAssemblyCandidate? candidate))
            {
                return new(
                    new AssemblyBindingOutcome.Resolved(candidate),
                    Registrations: [assembly]);
            }

            CandidateOpenFailure failure =
                _registrationFailures[assembly.Registration];
            return new(
                new AssemblyBindingOutcome.Unavailable(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)),
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
                if (_registrationFailures.TryGetValue(
                        assembly.Registration,
                        out CandidateOpenFailure? failure))
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

        bool HasPolicyVersion() =>
            ReferenceEquals(_policyVersion, _policy.Version);

        void EnsurePolicyVersion()
        {
            if (!HasPolicyVersion())
            {
                throw new InvalidOperationException(
                    "The binding policy changed version during discovery.");
            }
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

        TypeResolutionOutcome Reproject(TypeResolutionOutcome outcome)
        {
            if (outcome is not TypeResolutionOutcome.Resolved resolved)
                return outcome;

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
                    definition.Type),
                resolved.Hops);
        }
    }
}
