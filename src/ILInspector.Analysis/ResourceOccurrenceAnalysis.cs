using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>The physical operation represented by a resource occurrence.</summary>
public enum ResourceOccurrenceOperationKind
{
    Acquisition,
    Release,
    DirectCallBoundary,
    Storage,
    ReturnToCaller,
    UnsupportedValueFlow,
}

/// <summary>
/// Detached resolved type evidence. Generic-scope ownership is represented by
/// its kind rather than by acquisition-bound graph storage.
/// </summary>
public sealed record ResourceOccurrenceType(
    TypeRef Type,
    AssemblyReferenceIdentity? DefiningAssembly,
    DefinitionJoinKind? DefinitionKind,
    ResourceEffectGenericVariableKind? GenericScopeKind,
    ResourceOccurrenceType? Element,
    ImmutableArray<ResourceOccurrenceType> Arguments,
    ImmutableArray<ResourceOccurrenceTypeForwardingHop> Forwarding)
{
    internal static ResourceOccurrenceType From(
        ResolvedResourceEffectType type) =>
        new(
            type.Type,
            type.DefiningAssembly,
            type.Definition?.Kind,
            type.GenericScope?.Kind,
            type.Element is null ? null : From(type.Element),
            [.. type.Arguments.Select(From)],
            [
                .. type.Forwarding.Select(hop =>
                    new ResourceOccurrenceTypeForwardingHop(
                        hop.SourceAssembly.Assembly.Identity,
                        hop.TargetReference,
                        hop.Declarations)),
            ]);
}

public sealed record ResourceOccurrenceTypeForwardingHop(
    AssemblyReferenceIdentity SourceAssembly,
    AssemblyReferenceIdentity TargetReference,
    ImmutableArray<ExportedTypeToken> Declarations);

public sealed record ResourceOccurrenceResourceKind(
    ResourceKindIdentity Identity,
    ImmutableArray<ResourceOccurrenceType> Arguments)
{
    internal static ResourceOccurrenceResourceKind From(
        ResolvedResourceKindReference kind) =>
        new(kind.Identity, [.. kind.Arguments.Select(ResourceOccurrenceType.From)]);
}

public sealed record ResourceOccurrenceGenericBinding(
    ResourceEffectGenericVariable Variable,
    ResourceOccurrenceType Value);

/// <summary>The owner-issued identity of one resource obligation.</summary>
public abstract record ResourceOccurrenceRoot
{
    private protected ResourceOccurrenceRoot(
        ImmutableArray<ResourceOccurrenceResourceKind> resourceKinds)
    {
        ResourceKinds = ImmutableArrayValueEquality.RequireInitialized(
            resourceKinds,
            nameof(resourceKinds));
        if (ResourceKinds.IsEmpty)
        {
            throw new ArgumentException(
                "A resource root requires at least one resource kind.",
                nameof(resourceKinds));
        }
    }

    public ImmutableArray<ResourceOccurrenceResourceKind> ResourceKinds
    { get; }

    public sealed record Acquisition : ResourceOccurrenceRoot
    {
        public Acquisition(
            ResourceOccurrenceCallSite call,
            ImmutableArray<ResourceOccurrenceResourceKind> resourceKinds,
            ImmutableArray<ResourceOccurrenceAuthority> authorities)
            : base(resourceKinds)
        {
            Call = call ?? throw new ArgumentNullException(nameof(call));
            Authorities = ImmutableArrayValueEquality.RequireInitialized(
                authorities,
                nameof(authorities));
        }

        public ResourceOccurrenceCallSite Call { get; }
        public ImmutableArray<ResourceOccurrenceAuthority> Authorities
        { get; }
    }

    public sealed record IncomingArgument : ResourceOccurrenceRoot
    {
        public IncomingArgument(
            MethodIdentity method,
            int argumentIndex,
            ImmutableArray<ResourceOccurrenceResourceKind> resourceKinds)
            : base(resourceKinds)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(argumentIndex);
            Method = method
                ?? throw new ArgumentNullException(nameof(method));
            ArgumentIndex = argumentIndex;
        }

        public MethodIdentity Method { get; }
        public int ArgumentIndex { get; }
    }
}

/// <summary>Detached identity of one physical direct-call instruction.</summary>
public sealed record ResourceOccurrenceCallSite(
    MethodIdentity Method,
    int ILOffset,
    int OperandToken,
    CallKind Kind,
    MemberRef Callee)
{
    internal static ResourceOccurrenceCallSite From(DirectCall call) =>
        new(
            call.EvidenceMethod,
            call.ILOffset,
            call.OperandToken,
            call.Kind,
            call.Callee);
}

/// <summary>
/// One resolved declaration retained without reader, resolver, or acquisition
/// authority.
/// </summary>
public sealed record ResourceOccurrenceResolvedCallee(
    AssemblyReferenceIdentity Assembly,
    Guid ModuleVersionId,
    int MetadataToken,
    MemberRef Member,
    DirectCallDefinitionSemantics Semantics,
    bool IsInterfaceDefinition,
    ImmutableArray<ResourceOccurrenceTypeForwardingHop> Forwarding);

public sealed record ResourceOccurrenceEffect(
    ResourceOccurrenceResolvedCallee Callee,
    ResourceEffect Effect,
    ImmutableArray<ResourceOccurrenceGenericBinding> GenericBindings,
    ImmutableArray<ResourceOccurrenceResourceKind> ResourceKinds,
    ImmutableArray<ResourceOccurrenceType> AuthorityKeyArguments,
    ResourceEffectCompletion? Completion,
    ResourceEffectGuard? Guard,
    ResourceOccurrenceType? GuardExpectedType,
    ImmutableArray<ResourceDeclarationProvenance> Provenances)
{
    internal static ResourceOccurrenceEffect From(
        ResolvedResourceEffect effect)
    {
        DirectCallDefinitionOccurrence definition =
            effect.DirectCall.Definition;
        return new(
            new(
                definition.Assembly,
                definition.ModuleVersionId,
                definition.MetadataToken,
                definition.Member,
                definition.Semantics,
                definition.IsInterfaceDefinition,
                [
                    .. definition.Forwarding.Select(hop =>
                        new ResourceOccurrenceTypeForwardingHop(
                            hop.SourceAssembly.Assembly.Identity,
                            hop.TargetReference,
                            hop.Declarations)),
                ]),
            effect.Effect,
            [
                .. effect.GenericBindings.Select(binding =>
                    new ResourceOccurrenceGenericBinding(
                        binding.Variable,
                        ResourceOccurrenceType.From(binding.Value))),
            ],
            [.. effect.ResourceKinds.Select(ResourceOccurrenceResourceKind.From)],
            [.. effect.AuthorityKeyArguments.Select(ResourceOccurrenceType.From)],
            effect.Applicability.Completion,
            effect.Applicability.Guard,
            effect.GuardExpectedType is null
                ? null
                : ResourceOccurrenceType.From(effect.GuardExpectedType),
            effect.Provenances);
    }
}

public sealed record ResourceOccurrenceAuthority(
    ResourceOccurrenceCallSite Call,
    ResourceOccurrenceEffect Effect);

public sealed record ResourceOccurrenceStorageSite(
    int FieldToken,
    bool IsStatic,
    TypeRef? DeclaringType,
    string? FieldName,
    FieldIdentity? Identity);

/// <summary>
/// One physical operation associated with one obligation root. Effects at the
/// same call and root are grouped before publication.
/// </summary>
public sealed record ResourceOccurrence(
    ResourceOccurrenceRoot Root,
    MethodIdentity Method,
    int ILOffset,
    ImmutableArray<ResourceOccurrenceOperationKind> Operations,
    ImmutableArray<ResourceOccurrenceEffect> Effects)
{
    public ResourceOccurrenceCallSite? Call { get; init; }
    public ResourceOccurrenceStorageSite? Storage { get; init; }
}

public enum ResourceOccurrenceLimitationKind
{
    EffectResolution,
    BodyAnalysis,
    ValueFlow,
    UnsupportedEffect,
}

/// <summary>A typed, root-local or method-level completeness limitation.</summary>
public sealed record ResourceOccurrenceLimitation(
    ResourceOccurrenceLimitationKind Kind,
    string Message)
{
    public ResourceOccurrenceRoot? Root { get; init; }
    public MethodIdentity? Method { get; init; }
    public int? ILOffset { get; init; }
    public ResourceOccurrenceCallSite? Call { get; init; }
    public ResourceEffect? Effect { get; init; }
    public ResourceEffectResolutionGapKind? EffectResolutionGap
    { get; init; }
    public ResourceEffectResolutionRejectionKind? EffectResolutionRejection
    { get; init; }
    public ImmutableArray<ResourceOccurrenceResourceKind> ResourceKinds
    { get; init; } = [];
}

/// <summary>Detached terminal resource facts for one physical method body.</summary>
public sealed record ResourceOccurrenceAnalysisResult(
    MethodIdentity Method,
    ImmutableArray<ResourceOccurrenceRoot> Roots,
    ImmutableArray<ResourceOccurrence> Occurrences,
    ImmutableArray<ResourceOccurrenceLimitation> Limitations)
{
    public bool IsComplete => Limitations.IsEmpty;
}

/// <summary>
/// Produces terminal-resource facts from resolved effects and value provenance
/// derived from the same exact method body.
/// </summary>
internal static class ResourceOccurrenceAnalysisService
{
    internal static ResourceOccurrenceAnalysisResult Analyze(
        MethodBodyAnalysisContext context,
        ImmutableArray<DirectCall> calls,
        ImmutableArray<MethodResultSink> resultSinks,
        ImmutableArray<FieldStoreFact> fieldStores,
        ImmutableArray<ResolvedResourceEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(context);
        MethodIdentity method = context.Method;
        calls = RequireInitialized(calls, nameof(calls));
        resultSinks = RequireInitialized(resultSinks, nameof(resultSinks));
        fieldStores = RequireInitialized(fieldStores, nameof(fieldStores));
        effects = RequireInitialized(effects, nameof(effects));

        Dictionary<int, DirectCall> callsByOffset = calls
            .Where(call => call.EvidenceMethod == method)
            .ToDictionary(call => call.ILOffset);
        var roots = new Dictionary<ResourceRootKey, ResourceOccurrenceRoot>();
        var limitations = new List<ResourceOccurrenceLimitation>();

        foreach (ResolvedResourceEffect effect in effects
            .Where(effect =>
                effect.DirectCall.Call.EvidenceMethod == method
                && effect.Effect is ResourceEffect.Acquire)
            .OrderBy(effect => effect.DirectCall.Call.ILOffset)
            .ThenBy(effect =>
                    ResourceDomainKey(effect.ResourceKinds).SortKey,
                StringComparer.Ordinal))
        {
            DirectCall call = effect.DirectCall.Call;
            if (!IsSupportedAcquisitionTarget(effect))
            {
                limitations.Add(
                    new ResourceOccurrenceLimitation(
                        ResourceOccurrenceLimitationKind.ValueFlow,
                        "Resource Occurrence V1 requires an acquisition "
                        + "target that resolves to the call result.")
                    {
                        Method = method,
                        Call = ResourceOccurrenceCallSite.From(call),
                        Effect = effect.Effect,
                    });
                continue;
            }
            var key = ResourceRootKey.Acquisition(
                call.ILOffset,
                ResourceDomainKey(effect.ResourceKinds));
            roots.TryAdd(
                key,
                new ResourceOccurrenceRoot.Acquisition(
                    ResourceOccurrenceCallSite.From(call),
                    [
                        .. effect.ResourceKinds.Select(
                            ResourceOccurrenceResourceKind.From),
                    ],
                    Authorities(effect, effects)));
        }

        foreach (ResolvedResourceEffect effect in effects
            .Where(effect =>
                effect.DirectCall.Call.EvidenceMethod == method))
        {
            ResourceEffectLocation? source = Source(effect.Effect);
            if (source is null)
                continue;
            if (effect.ResourceKinds.IsEmpty)
                continue;

            DirectCall call = effect.DirectCall.Call;
            ResolvedValueSet? value = ValueAt(
                call,
                effect.Binding.Location(source));
            if (value is not { IsResolved: true })
                continue;

            foreach (ResolvedValueSource valueSource in value.Sources
                .Where(source =>
                    source.Kind == ResolvedValueSourceKind.Argument))
            {
                var key = ResourceRootKey.Argument(
                    valueSource.ArgumentIndex,
                    ResourceDomainKey(effect.ResourceKinds));
                roots.TryAdd(
                    key,
                    new ResourceOccurrenceRoot.IncomingArgument(
                        method,
                        valueSource.ArgumentIndex,
                        [
                            .. effect.ResourceKinds.Select(
                                ResourceOccurrenceResourceKind.From),
                        ]));
            }
        }

        var groups = new Dictionary<
            (ResourceRootKey Root, int ILOffset),
            OccurrenceBuilder>();
        foreach (IGrouping<int, ResolvedResourceEffect> callEffects in effects
            .Where(effect =>
                effect.DirectCall.Call.EvidenceMethod == method)
            .GroupBy(effect => effect.DirectCall.Call.ILOffset)
            .OrderBy(group => group.Key))
        {
            ImmutableArray<ResolvedResourceEffect> callEffectArray =
                [.. callEffects];
            DirectCall call =
                callEffectArray[0].DirectCall.Call;
            var affectedByEffect =
                new Dictionary<
                    ResolvedResourceEffect,
                    ImmutableArray<ResourceRootKey>>(
                        ReferenceEqualityComparer.Instance);
            foreach (ResolvedResourceEffect effect in callEffectArray)
            {
                ResourceEffectLocation? source = Source(effect.Effect);
                if (source is not null
                    && ValueAt(
                        call,
                        effect.Binding.Location(source))
                        is not { IsResolved: true })
                {
                    limitations.Add(
                        new ResourceOccurrenceLimitation(
                            ResourceOccurrenceLimitationKind.ValueFlow,
                            "The resource effect source could not be assigned "
                            + "to a terminal-resource root.")
                        {
                            Method = method,
                            Call = ResourceOccurrenceCallSite.From(call),
                            Effect = effect.Effect,
                            ResourceKinds =
                            [
                                .. effect.ResourceKinds.Select(
                                    ResourceOccurrenceResourceKind.From),
                            ],
                        });
                }

                ImmutableArray<ResourceRootKey> affectedRoots =
                    effect.Effect is ResourceEffect.Acquire
                        ? AcquisitionRoots(effect)
                        : effect.Effect is ResourceEffect.Operation
                            ? BoundaryRoots(call, roots)
                            : AffectedRoots(effect, roots);
                if (source is not null
                    && ValueAt(
                        call,
                        effect.Binding.Location(source))
                        is { IsResolved: true }
                    && affectedRoots.IsEmpty)
                {
                    limitations.Add(
                        new ResourceOccurrenceLimitation(
                            ResourceOccurrenceLimitationKind.ValueFlow,
                            "The resource effect source did not carry an "
                            + "established terminal-resource obligation.")
                        {
                            Method = method,
                            Call = ResourceOccurrenceCallSite.From(call),
                            Effect = effect.Effect,
                        });
                }
                affectedByEffect.Add(effect, affectedRoots);
            }

            ImmutableArray<ResourceRootKey> callRoots =
            [
                .. affectedByEffect.Values
                    .SelectMany(static keys => keys)
                    .Distinct()
                    .Order(),
            ];
            foreach (ResourceRootKey rootKey in callRoots)
            {
                if (!roots.TryGetValue(rootKey, out ResourceOccurrenceRoot? root))
                    continue;

                var groupKey = (rootKey, call.ILOffset);
                if (!groups.TryGetValue(groupKey, out OccurrenceBuilder? group))
                {
                    group = new(root, call);
                    groups.Add(groupKey, group);
                }

                foreach (ResolvedResourceEffect effect in callEffectArray)
                {
                    ImmutableArray<ResourceRootKey> effectRoots =
                        affectedByEffect[effect];
                    bool isRootScoped =
                        effect.Effect is ResourceEffect.Acquire
                        || Source(effect.Effect) is not null;
                    if (isRootScoped
                        && !effectRoots.Contains(rootKey))
                    {
                        continue;
                    }
                    if (!isRootScoped
                        && !effect.ResourceKinds.IsEmpty
                        && !rootKey.Domain.Equals(
                            ResourceDomainKey(effect.ResourceKinds)))
                    {
                        continue;
                    }

                    group.Effects.Add(ResourceOccurrenceEffect.From(effect));
                    group.Operations.Add(
                        effect.Effect switch
                        {
                            ResourceEffect.Acquire =>
                                ResourceOccurrenceOperationKind.Acquisition,
                            ResourceEffect.Release =>
                                ResourceOccurrenceOperationKind.Release,
                            _ => ResourceOccurrenceOperationKind
                                .DirectCallBoundary,
                        });

                    if (!IsSupported(effect.Effect))
                    {
                        group.Operations.Add(
                            ResourceOccurrenceOperationKind
                                .UnsupportedValueFlow);
                        limitations.Add(
                            new ResourceOccurrenceLimitation(
                                ResourceOccurrenceLimitationKind
                                    .UnsupportedEffect,
                                $"The {effect.Effect.GetType().Name} effect "
                                + "is not part of Resource Occurrence V1.")
                            {
                                Root = root,
                                Method = method,
                                Call = ResourceOccurrenceCallSite.From(call),
                                Effect = effect.Effect,
                            });
                    }
                }
            }
        }

        foreach (DirectCall call in callsByOffset.Values
            .OrderBy(call => call.ILOffset))
        {
            AddBoundaryOccurrences(
                method,
                call,
                roots,
                groups,
                limitations);
        }

        foreach (FieldStoreFact store in fieldStores
            .Where(store => store.EvidenceMethod == method)
            .OrderBy(store => store.ILOffset))
        {
            if (!store.Value.IsResolved)
            {
                AddUnresolvedValueLimitation(
                    method,
                    store.ILOffset,
                    call: null,
                    "A field-store value could not be assigned to a "
                    + "terminal-resource root.",
                    roots,
                    limitations);
                continue;
            }
            foreach (ResourceRootKey rootKey in MatchingRoots(
                store.Value,
                roots))
            {
                if (!roots.TryGetValue(rootKey, out ResourceOccurrenceRoot? root))
                    continue;
                var group = new OccurrenceBuilder(root, store.ILOffset);
                group.Operations.Add(ResourceOccurrenceOperationKind.Storage);
                group.Storage = new(
                    store.FieldToken,
                    store.IsStatic,
                    store.DeclaringType,
                    store.FieldName,
                    store.Identity);
                groups[(rootKey, store.ILOffset)] = group;
                if (store.Identity is null)
                {
                    limitations.Add(
                        new ResourceOccurrenceLimitation(
                            ResourceOccurrenceLimitationKind.BodyAnalysis,
                            "The resource storage target could not be "
                            + "resolved to an exact field identity.")
                        {
                            Root = root,
                            Method = method,
                            ILOffset = store.ILOffset,
                        });
                }
            }
        }

        foreach (MethodResultSink sink in resultSinks
            .Where(sink =>
                sink.EvidenceMethod == method
                && sink.Kind == MethodResultSinkKind.MethodReturn)
            .OrderBy(sink => sink.ILOffset))
        {
            if (sink.ResolvedValue is not { } value)
                continue;
            if (method.ReturnType.Equals(
                TypeRef.CoreLib("System", "Void")))
            {
                continue;
            }
            if (!value.IsResolved)
            {
                AddUnresolvedValueLimitation(
                    method,
                    sink.ILOffset,
                    call: null,
                    "A returned value could not be assigned to a "
                    + "terminal-resource root.",
                    roots,
                    limitations);
                continue;
            }
            foreach (ResourceRootKey rootKey in MatchingRoots(value, roots))
            {
                if (!roots.TryGetValue(rootKey, out ResourceOccurrenceRoot? root))
                    continue;
                var group = new OccurrenceBuilder(root, sink.ILOffset);
                group.Operations.Add(
                    ResourceOccurrenceOperationKind.ReturnToCaller);
                groups[(rootKey, sink.ILOffset)] = group;
            }
        }

        return new(
            method,
            [
                .. roots
                    .OrderBy(entry => entry.Key)
                    .Select(entry => entry.Value),
            ],
            [
                .. groups
                    .OrderBy(entry => entry.Key.ILOffset)
                    .ThenBy(entry => entry.Key.Root)
                    .Select(entry => entry.Value.Build()),
            ],
            [
                .. limitations
                    .OrderBy(limitation =>
                        limitation.Call?.ILOffset
                        ?? limitation.ILOffset
                        ?? -1)
                    .ThenBy(limitation => limitation.Message,
                        StringComparer.Ordinal),
            ]);
    }

    static void AddBoundaryOccurrences(
        MethodIdentity method,
        DirectCall call,
        Dictionary<ResourceRootKey, ResourceOccurrenceRoot> roots,
        Dictionary<(ResourceRootKey Root, int ILOffset), OccurrenceBuilder>
            groups,
        List<ResourceOccurrenceLimitation> limitations)
    {
        ImmutableArray<ResolvedValueSet> values =
        [
            .. call.ResolvedArgumentValues.Concat(
                call.ResolvedReceiverValue is { } receiver
                    ? [receiver]
                    : Enumerable.Empty<ResolvedValueSet>()),
        ];
        if (values.Any(value => !value.IsResolved))
        {
            AddUnresolvedValueLimitation(
                method,
                call.ILOffset,
                ResourceOccurrenceCallSite.From(call),
                "A direct-call boundary value could not be assigned to a "
                + "terminal-resource root.",
                roots,
                limitations);
        }
        foreach (ResourceRootKey rootKey in BoundaryRoots(call, roots))
        {
            if (!roots.TryGetValue(rootKey, out ResourceOccurrenceRoot? root))
                continue;
            var key = (rootKey, call.ILOffset);
            if (!groups.TryGetValue(key, out OccurrenceBuilder? group))
            {
                group = new(root, call);
                groups.Add(key, group);
            }
            group.Operations.Add(
                ResourceOccurrenceOperationKind.DirectCallBoundary);
        }
    }

    static ImmutableArray<ResourceRootKey> BoundaryRoots(
        DirectCall call,
        Dictionary<ResourceRootKey, ResourceOccurrenceRoot> roots) =>
        [
            .. call.ResolvedArgumentValues
                .Concat(
                    call.ResolvedReceiverValue is { } receiver
                        ? [receiver]
                        : Enumerable.Empty<ResolvedValueSet>())
                .SelectMany(value => MatchingRoots(value, roots))
                .Distinct()
                .Order(),
        ];

    static void AddUnresolvedValueLimitation(
        MethodIdentity method,
        int offset,
        ResourceOccurrenceCallSite? call,
        string message,
        Dictionary<ResourceRootKey, ResourceOccurrenceRoot> roots,
        List<ResourceOccurrenceLimitation> limitations)
    {
        if (roots.Count == 0)
            return;

        limitations.Add(
            new ResourceOccurrenceLimitation(
                ResourceOccurrenceLimitationKind.ValueFlow,
                message)
            {
                Method = method,
                ILOffset = offset,
                Call = call,
            });
    }

    static ImmutableArray<ResourceRootKey> AffectedRoots(
        ResolvedResourceEffect effect,
        Dictionary<ResourceRootKey, ResourceOccurrenceRoot> roots)
    {
        ResourceEffectLocation? source = Source(effect.Effect);
        if (source is null)
            return [];

        ResolvedValueSet? value = ValueAt(
            effect.DirectCall.Call,
            effect.Binding.Location(source));
        return value is null
            ? []
            : [
                .. MatchingRoots(
                    value,
                    roots,
                    effect.ResourceKinds.IsEmpty
                        ? null
                        : ResourceDomainKey(effect.ResourceKinds)),
            ];
    }

    static ImmutableArray<ResourceRootKey> AcquisitionRoots(
        ResolvedResourceEffect effect) =>
        IsSupportedAcquisitionTarget(effect)
            ?
            [
                ResourceRootKey.Acquisition(
                    effect.DirectCall.Call.ILOffset,
                    ResourceDomainKey(effect.ResourceKinds)),
            ]
            : [];

    static IEnumerable<ResourceRootKey> MatchingRoots(
        ResolvedValueSet value,
        Dictionary<ResourceRootKey, ResourceOccurrenceRoot> roots,
        ResourceDomain? domain = null)
    {
        if (!value.IsResolved)
            yield break;

        foreach (ResolvedValueSource source in value.Sources)
        {
            foreach (ResourceRootKey key in roots.Keys)
            {
                if ((domain is null || key.Domain.Equals(domain))
                    && ((source.IsCallResult
                        && key.Kind == ResourceRootKind.Acquisition
                        && key.Coordinate == source.ILOffset)
                    || (source.Kind == ResolvedValueSourceKind.Argument
                        && key.Kind == ResourceRootKind.Argument
                        && key.Coordinate == source.ArgumentIndex)))
                {
                    yield return key;
                }
            }
        }
    }

    static ResolvedValueSet? ValueAt(
        DirectCall call,
        ResolvedResourceEffectLocation location) =>
        location switch
        {
            ResolvedResourceEffectLocation.Boundary
            {
                Kind: ResolvedResourceEffectBoundaryLocationKind.Receiver
            } => call.ResolvedReceiverValue,
            ResolvedResourceEffectLocation.Boundary
            {
                Kind: ResolvedResourceEffectBoundaryLocationKind.Parameter,
                ParameterIndex: int index,
            } when index < call.ResolvedArgumentValues.Count =>
                call.ResolvedArgumentValues[index],
            ResolvedResourceEffectLocation.Boundary
            {
                Kind: ResolvedResourceEffectBoundaryLocationKind.Return
                    or ResolvedResourceEffectBoundaryLocationKind.Constructed
            } => new(
                [
                    new ResolvedValueSource(
                        call.Kind == CallKind.NewObject
                            ? ResolvedValueSourceKind.NewObjectResult
                            : ResolvedValueSourceKind.CallResult,
                        call.ILOffset),
                ],
                isResolved: true),
            _ => null,
        };

    static ResourceEffectLocation? Source(ResourceEffect effect) =>
        effect switch
        {
            ResourceEffect.Move value => value.Source,
            ResourceEffect.Consume value => value.Source,
            ResourceEffect.Release value => value.Source,
            ResourceEffect.Borrow value => value.Source,
            ResourceEffect.Derive value => value.Source,
            ResourceEffect.Pass value => value.Source,
            ResourceEffect.Independent value => value.Source,
            ResourceEffect.Accept value => value.Source,
            ResourceEffect.Outcome value => value.Source,
            _ => null,
        };

    static bool IsSupported(ResourceEffect effect) =>
        effect is ResourceEffect.Acquire
            or ResourceEffect.Release
            or ResourceEffect.Authority
            or ResourceEffect.Resource
            or ResourceEffect.Operation;

    static bool IsSupportedAcquisitionTarget(
        ResolvedResourceEffect effect) =>
        effect.Effect is ResourceEffect.Acquire acquire
        && effect.Binding.Location(acquire.Target)
            is ResolvedResourceEffectLocation.Boundary
            {
                Kind: ResolvedResourceEffectBoundaryLocationKind.Return
                    or ResolvedResourceEffectBoundaryLocationKind.Constructed,
            };

    static ImmutableArray<ResourceOccurrenceAuthority> Authorities(
        ResolvedResourceEffect acquisition,
        ImmutableArray<ResolvedResourceEffect> effects)
    {
        if (acquisition.Effect
                is not ResourceEffect.Acquire
                {
                    Correspondence: { } correspondence,
                })
        {
            return [];
        }

        ResolvedValueSet? value = ValueAt(
            acquisition.DirectCall.Call,
            acquisition.Binding.Location(correspondence));
        if (value is not { IsResolved: true })
            return [];

        HashSet<int> authorityOffsets =
        [
            .. value.Sources
                .Where(source => source.IsCallResult)
                .Select(source => source.ILOffset),
        ];
        ResourceDomain domain = ResourceDomainKey(
            acquisition.ResourceKinds);
        return
        [
            .. effects
                .Where(effect =>
                    effect.DirectCall.Call.EvidenceMethod
                        == acquisition.DirectCall.Call.EvidenceMethod
                    && authorityOffsets.Contains(
                        effect.DirectCall.Call.ILOffset)
                    && AuthorityTargetsCorrespondence(effect, value)
                    && ResourceDomainKey(effect.ResourceKinds).Equals(
                        domain))
                .OrderBy(effect => effect.DirectCall.Call.ILOffset)
                .Select(effect =>
                    new ResourceOccurrenceAuthority(
                        ResourceOccurrenceCallSite.From(
                            effect.DirectCall.Call),
                        ResourceOccurrenceEffect.From(effect))),
        ];
    }

    static bool AuthorityTargetsCorrespondence(
        ResolvedResourceEffect effect,
        ResolvedValueSet correspondence)
    {
        if (effect.Effect is not ResourceEffect.Authority authority)
            return false;

        ResolvedValueSet? target = ValueAt(
            effect.DirectCall.Call,
            effect.Binding.Location(authority.Target));
        return target is { IsResolved: true }
            && target.Sources.All(targetSource =>
                targetSource.IsCallResult
                && correspondence.Sources.Any(source =>
                    source.Kind == targetSource.Kind
                    && source.ILOffset == targetSource.ILOffset));
    }

    static ResourceDomain ResourceDomainKey(
        ImmutableArray<ResolvedResourceKindReference> kinds) =>
        new(kinds);

    static ImmutableArray<T> RequireInitialized<T>(
        ImmutableArray<T> values,
        string name)
    {
        if (values.IsDefault)
            throw new ArgumentException("The array must be initialized.", name);
        return values;
    }

    enum ResourceRootKind
    {
        Acquisition,
        Argument,
    }

    readonly record struct ResourceRootKey(
        ResourceRootKind Kind,
        int Coordinate,
        ResourceDomain Domain) : IComparable<ResourceRootKey>
    {
        internal static ResourceRootKey Acquisition(
            int offset,
            ResourceDomain domain) =>
            new(ResourceRootKind.Acquisition, offset, domain);

        internal static ResourceRootKey Argument(
            int index,
            ResourceDomain domain) =>
            new(ResourceRootKind.Argument, index, domain);

        public int CompareTo(ResourceRootKey other)
        {
            int kind = Kind.CompareTo(other.Kind);
            if (kind != 0)
                return kind;
            int coordinate = Coordinate.CompareTo(other.Coordinate);
            return coordinate != 0
                ? coordinate
                : StringComparer.Ordinal.Compare(
                    Domain.SortKey,
                    other.Domain.SortKey);
        }
    }

    sealed class ResourceDomain :
        IEquatable<ResourceDomain>
    {
        readonly ImmutableArray<ResolvedResourceKindReference> _kinds;

        internal ResourceDomain(
            ImmutableArray<ResolvedResourceKindReference> kinds)
        {
            _kinds =
            [
                .. kinds.OrderBy(
                    KindSortKey,
                    StringComparer.Ordinal),
            ];
            SortKey = string.Join(
                "|",
                _kinds.Select(KindSortKey));
        }

        internal string SortKey { get; }

        public bool Equals(ResourceDomain? other) =>
            other is not null
            && _kinds.SequenceEqual(other._kinds);

        public override bool Equals(object? obj) =>
            obj is ResourceDomain other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            ImmutableArrayValueEquality.AddToHash(ref hash, _kinds);
            return hash.ToHashCode();
        }

        static string KindSortKey(ResolvedResourceKindReference kind) =>
            kind.Identity.Value
            + "<"
            + string.Join(",", kind.Arguments.Select(TypeSortKey))
            + ">";

        static string TypeSortKey(ResolvedResourceEffectType type) =>
            $"{type.DefiningAssembly}|{type.Definition?.Kind}|{type.Type}|"
            + $"{type.GenericScope?.Kind}|"
            + (type.Element is null ? "" : TypeSortKey(type.Element))
            + "<"
            + string.Join(",", type.Arguments.Select(TypeSortKey))
            + ">";
    }

    sealed class OccurrenceBuilder
    {
        internal OccurrenceBuilder(
            ResourceOccurrenceRoot root,
            DirectCall call)
            : this(root, call.ILOffset) =>
            Call = ResourceOccurrenceCallSite.From(call);

        internal OccurrenceBuilder(
            ResourceOccurrenceRoot root,
            int offset)
        {
            Root = root;
            Offset = offset;
        }

        internal ResourceOccurrenceRoot Root { get; }
        internal int Offset { get; }
        internal ResourceOccurrenceCallSite? Call { get; }
        internal ResourceOccurrenceStorageSite? Storage { get; set; }
        internal HashSet<ResourceOccurrenceOperationKind> Operations
        { get; } = [];
        internal List<ResourceOccurrenceEffect> Effects { get; } = [];

        internal ResourceOccurrence Build() =>
            new(
                Root,
                Root switch
                {
                    ResourceOccurrenceRoot.Acquisition acquisition =>
                        acquisition.Call.Method,
                    ResourceOccurrenceRoot.IncomingArgument argument =>
                        argument.Method,
                    _ => throw new InvalidOperationException(
                        "Unknown resource root."),
                },
                Offset,
                [.. Operations.Order()],
                [.. Effects])
            {
                Call = Call,
                Storage = Storage,
            };
    }
}
