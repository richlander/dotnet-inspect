using System.Collections.Immutable;

using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Queries;

/// <summary>
/// One explicitly versioned package, one target framework, and one exact Type
/// selection.
/// </summary>
public sealed record ExactTypeInspectionRequest
{
    public ExactTypeInspectionRequest(
        string packageId,
        string version,
        string targetFramework,
        string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (!NuGetVersion.TryParse(version, out NuGetVersion? parsedVersion))
        {
            throw new ArgumentException(
                "Exact Type inspection requires an explicit package version.",
                nameof(version));
        }
        if (targetFramework.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Exact Type inspection requires one explicit target framework.",
                nameof(targetFramework));
        }
        if (TypeMatcher.IsTypeGlobPattern(type))
        {
            throw new ArgumentException(
                "Exact Type inspection does not accept a Type glob.",
                nameof(type));
        }

        PackageId = packageId;
        Version = parsedVersion.ToNormalizedString();
        TargetFramework = targetFramework;
        Type = type;
    }

    public string PackageId { get; }

    public string Version { get; }

    public string TargetFramework { get; }

    public string Type { get; }
}

public enum ExactTypeInspectionOutcome
{
    Available,
    NotFound,
    Ambiguous,
    Unavailable,
}

public enum ExactTypeInspectionFailureKind
{
    ContextLoad,
    ParticipantRejected,
    MetadataMalformed,
    BindingPolicyUnsupported,
    TypeResolutionUnavailable,
    SupplierUnavailable,
    InspectionIncomplete,
    ProjectionTruncated,
}

/// <summary>
/// Exact Metadata assembly identity detached from its acquisition capability.
/// </summary>
public sealed record ExactTypeAssemblyIdentity(
    AssemblyReferenceIdentity Identity,
    Guid ModuleVersionId);

/// <summary>One exact Metadata forwarding edge followed for the selected Type.</summary>
public sealed record ExactTypeForwardingHop(
    AssemblyReferenceIdentity Source,
    AssemblyReferenceIdentity Target);

/// <summary>Typed non-success or incompleteness evidence for exact Type inspection.</summary>
public sealed record ExactTypeInspectionFailure(
    ExactTypeInspectionFailureKind Kind,
    string Detail,
    AssemblyReferenceIdentity? Assembly = null);

/// <summary>
/// Detached Metadata API-surface failure without compatibility paths or
/// internal definition handles.
/// </summary>
public sealed record ExactTypeApiInspectionFailure(
    string Operation,
    int SubjectToken,
    MetadataTypeNameFailureMechanism Mechanism,
    string Kind,
    string Detail,
    AssemblyReferenceIdentity? SubjectAssembly,
    AssemblyReferenceIdentity? DependencyAssembly)
{
    internal static ExactTypeApiInspectionFailure From(
        ApiSurfaceInspectionFailure failure) =>
        new(
            failure.Operation,
            failure.SubjectToken,
            failure.Mechanism,
            failure.Kind,
            failure.Detail,
            failure.SubjectAssembly,
            failure.DependencyAssembly);
}

/// <summary>Detached generic-parameter facts for one exact Type.</summary>
public sealed record ExactTypeParameter(
    string Name,
    string? Variance,
    ImmutableArray<string> Constraints);

/// <summary>
/// One member inventory entry. This slice carries identity and kind rather
/// than the richer declaration and body facts owned by later operations.
/// </summary>
public sealed record ExactTypeMember(
    string Name,
    string Kind,
    string? Signature);

/// <summary>
/// Detached declaration facts and member inventory for one exact Type.
/// </summary>
public sealed record ExactTypeApi(
    string FullName,
    string? Namespace,
    string Name,
    string Kind,
    string? Accessibility,
    ImmutableArray<string> Attributes,
    bool IsSealed,
    bool IsAbstract,
    bool IsStatic,
    bool IsByRefLike,
    bool IsReadOnly,
    string? BaseType,
    ImmutableArray<string> Interfaces,
    ImmutableArray<string> DerivedTypes,
    ImmutableArray<ExactTypeParameter> TypeParameters,
    ImmutableArray<ExactTypeMember> Members,
    string? EnumUnderlyingType,
    bool IsForwarded)
{
    internal static ExactTypeApi From(ApiType type) =>
        new(
            type.FullName,
            type.Namespace,
            type.Name,
            type.Kind,
            type.Accessibility,
            [.. type.Attributes],
            type.IsSealed,
            type.IsAbstract,
            type.IsStatic,
            type.IsByRefLike,
            type.IsReadOnly,
            type.BaseType,
            [.. type.Interfaces],
            [.. type.DerivedTypes],
            [
                .. type.TypeParameters.Select(parameter =>
                    new ExactTypeParameter(
                        parameter.Name,
                        parameter.Variance,
                        [.. parameter.Constraints])),
            ],
            [
                .. type.Members.Select(member =>
                    new ExactTypeMember(
                        member.Name,
                        member.Kind,
                        member.Signature)),
            ],
            type.EnumUnderlyingType,
            type.IsForwarded);
}

/// <summary>
/// Detached exact Type API facts and the Metadata identity that supplied them.
/// </summary>
public sealed record ExactTypeInspectionResult(
    ExactTypeInspectionOutcome Outcome,
    string RequestedType,
    string? MatchedType,
    ExactTypeApi? Type,
    ExactTypeAssemblyIdentity? RequestedAssembly,
    ExactTypeAssemblyIdentity? SupplierAssembly,
    ImmutableArray<ExactTypeForwardingHop> ForwardingHops,
    ImmutableArray<string> Suggestions,
    ImmutableArray<ExactTypeApiInspectionFailure> InspectionFailures,
    ImmutableArray<ExactTypeInspectionFailure> Failures)
{
    public bool IsAvailable =>
        Outcome == ExactTypeInspectionOutcome.Available;

    public bool IsComplete =>
        IsAvailable
        && Failures.IsEmpty
        && InspectionFailures.All(static failure =>
            failure.Operation == ApiSurface.ConstraintResolutionOperation);

    internal static ExactTypeInspectionResult ContextUnavailable(
        ExactTypeInspectionRequest request,
        IEnumerable<WorkspaceContextLoadFailure> failures) =>
        new(
            ExactTypeInspectionOutcome.Unavailable,
            request.Type,
            MatchedType: null,
            Type: null,
            RequestedAssembly: null,
            SupplierAssembly: null,
            ForwardingHops: [],
            Suggestions: [],
            InspectionFailures: [],
            Failures:
            [
                .. failures.Select(failure =>
                    new ExactTypeInspectionFailure(
                        ExactTypeInspectionFailureKind.ContextLoad,
                        $"{failure.Kind}: {failure.Message}")),
            ]);

    internal static ExactTypeInspectionResult RuntimeUnavailable(
        ExactTypeInspectionRequest request,
        string detail) =>
        new(
            ExactTypeInspectionOutcome.Unavailable,
            request.Type,
            MatchedType: null,
            Type: null,
            RequestedAssembly: null,
            SupplierAssembly: null,
            ForwardingHops: [],
            Suggestions: [],
            InspectionFailures: [],
            Failures:
            [
                new ExactTypeInspectionFailure(
                    ExactTypeInspectionFailureKind.TypeResolutionUnavailable,
                    detail),
            ]);
}

/// <summary>
/// Live exact-Type context constructed under one candidate realization.
/// </summary>
internal sealed class ExactTypeInspectionContext
{
    internal ExactTypeInspectionContext(
        WorkspaceContextLoadOutcome.Loaded context)
    {
        Realization = context.Workspace;
        Context = context;
    }

    internal InspectionWorkspaceIdentity Realization { get; }

    internal WorkspaceContextLoadOutcome.Loaded Context { get; }
}

/// <summary>
/// Resolves and projects one exact Type through an admitted Workspace
/// realization operation.
/// </summary>
internal static class ExactTypeInspectionQuery
{
    sealed record Candidate(
        int Order,
        AssemblyContextParticipant Participant,
        AssemblyApiSurface Surface,
        MetadataTypeDefinitionName Definition);

    sealed record Projection(
        int Order,
        AssemblyContextParticipant Participant,
        AssemblyContextEntry<AssemblyApiSurface> Entry);

    sealed record ResolvedCandidate(
        Candidate Candidate,
        TypeResolutionOutcome.Resolved Resolution);

    internal static ExactTypeInspectionResult Execute(
        WorkspaceRealizationOperationLease authority,
        ExactTypeInspectionContext context,
        ExactTypeInspectionRequest request,
        ApiSurfaceProjectionLimits? projectionLimits = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(authority.Realization, context.Realization)
            || !ReferenceEquals(
                authority.Definition.Workspace,
                context.Realization))
        {
            throw new ArgumentException(
                "The exact Type context does not belong to the admitted realization.",
                nameof(context));
        }

        WorkspaceContextLoadOutcome.Loaded loaded = context.Context;
        ImmutableArray<AssemblyContextParticipant> participants =
            PackageParticipants(loaded, request);
        if (participants.IsEmpty)
        {
            return ExactTypeInspectionResult.RuntimeUnavailable(
                request,
                "The admitted realization does not contain the requested package coordinate.");
        }

        AssemblyContextApiSurfaceResult? boundedProjection =
            projectionLimits is null
                ? null
                : AssemblyContextApiSurfaceQuery.ExecuteBoundedResolved(
                    loaded.Group,
                    ApiSurfaceScope.PublicWithNonPublicTypes,
                    projectionLimits,
                    participants);
        ImmutableArray<Projection> projections =
            boundedProjection is null
                ? [
                    .. participants.Select(
                        (participant, order) => new Projection(
                            order,
                            participant,
                            AssemblyContextApiSurfaceQuery
                                .ExecuteParticipantResolved(
                                loaded.Group,
                                participant,
                                ApiSurfaceScope.PublicWithNonPublicTypes))),
                ]
                : [
                    .. boundedProjection.Assemblies.Assemblies.Select(
                        (entry, order) => new Projection(
                            order,
                            participants[order],
                            entry)),
                ];
        ImmutableArray<ExactTypeInspectionFailure> participantFailures =
            ParticipantFailures(projections);
        if (boundedProjection?.Truncation is { } truncation)
        {
            participantFailures =
                participantFailures.Add(
                    new ExactTypeInspectionFailure(
                        ExactTypeInspectionFailureKind.ProjectionTruncated,
                        "API-surface projection was truncated by the "
                            + $"{truncation.Limit} bound of "
                            + $"{truncation.Bound}; "
                            + $"{truncation.OmittedParticipants} "
                            + "participant(s) were omitted."));
        }
        ImmutableArray<ApiSurfaceInspectionFailure> inspectionFailures =
            InspectionFailures(projections);
        ImmutableArray<ApiSurfaceInspectionFailure> lookupFailures =
        [
            .. inspectionFailures.Where(static failure =>
                failure.Operation
                    != ApiSurface.ConstraintResolutionOperation),
        ];
        ImmutableArray<ExactTypeApiInspectionFailure>
            detachedInspectionFailures =
        [
            .. lookupFailures.Select(
                ExactTypeApiInspectionFailure.From),
        ];
        ImmutableArray<ExactTypeInspectionFailure> incompleteness =
            Incompleteness(lookupFailures);
        ImmutableArray<Candidate> declarations =
            Declarations(projections);
        string[] declarationNames =
        [
            .. declarations
                .Select(candidate =>
                    candidate.Definition.ToEscapedFullName())
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        string? exactName = declarationNames.FirstOrDefault(name =>
            name.Equals(
                request.Type,
                StringComparison.OrdinalIgnoreCase));
        string[] matchingNames = exactName is not null
            ? [exactName]
            :
            [
                .. declarationNames.Where(name =>
                    TypeMatcher.MatchesTypeFilter(
                        name,
                        request.Type)),
            ];
        if (matchingNames.Length == 0)
        {
            if (participantFailures.Length > 0
                || lookupFailures.Any(failure =>
                    MayAffectTypeLookup(
                        failure,
                        request.Type)))
            {
                return new ExactTypeInspectionResult(
                    ExactTypeInspectionOutcome.Unavailable,
                    request.Type,
                    MatchedType: null,
                    Type: null,
                    RequestedAssembly: null,
                    SupplierAssembly: null,
                    ForwardingHops: [],
                    Suggestions: [],
                    InspectionFailures: detachedInspectionFailures,
                    Failures:
                    [
                        .. participantFailures,
                        .. incompleteness,
                    ]);
            }

            LookupResult lookup =
                TypeMatcher.Lookup(declarationNames, request.Type);
            return new ExactTypeInspectionResult(
                ExactTypeInspectionOutcome.NotFound,
                request.Type,
                MatchedType: null,
                Type: null,
                RequestedAssembly: null,
                SupplierAssembly: null,
                ForwardingHops: [],
                Suggestions: [.. lookup.Suggestions],
                InspectionFailures: detachedInspectionFailures,
                Failures:
                [
                    .. participantFailures,
                    .. incompleteness,
                ]);
        }

        ImmutableArray<Candidate> matching =
        [
            .. declarations.Where(candidate =>
                matchingNames.Contains(
                    candidate.Definition.ToEscapedFullName(),
                    StringComparer.OrdinalIgnoreCase)),
        ];
        string? matchedType = matchingNames.Length == 1
            ? matchingNames[0]
            : null;
        if (boundedProjection?.Truncation is not null)
        {
            return new ExactTypeInspectionResult(
                ExactTypeInspectionOutcome.Unavailable,
                request.Type,
                matchedType,
                Type: null,
                RequestedAssembly: null,
                SupplierAssembly: null,
                ForwardingHops: [],
                Suggestions: [],
                InspectionFailures: detachedInspectionFailures,
                Failures:
                [
                    .. participantFailures,
                    .. incompleteness,
                ]);
        }

        var resolved = ImmutableArray.CreateBuilder<ResolvedCandidate>();
        var resolutionFailures =
            ImmutableArray.CreateBuilder<ExactTypeInspectionFailure>();
        bool ambiguous = false;
        foreach (Candidate candidate in matching)
        {
            AssemblyContextTypeResolutionResult resolution =
                AssemblyContextTypeResolutionQuery.Execute(
                    loaded.Group,
                    candidate.Participant,
                    candidate.Definition,
                    AssemblyResolutionScope.Any);
            switch (resolution)
            {
                case AssemblyContextTypeResolutionResult.Available
                {
                    Outcome: TypeResolutionOutcome.Resolved available,
                }:
                    resolved.Add(new ResolvedCandidate(candidate, available));
                    break;
                case AssemblyContextTypeResolutionResult.Available
                {
                    Outcome: TypeResolutionOutcome.Ambiguous,
                }:
                    ambiguous = true;
                    break;
                case AssemblyContextTypeResolutionResult.Available available:
                    resolutionFailures.Add(
                        new ExactTypeInspectionFailure(
                            ExactTypeInspectionFailureKind
                                .TypeResolutionUnavailable,
                            available.Outcome.GetType().Name,
                            available.Outcome.TerminalAssemblyIdentity));
                    break;
                case AssemblyContextTypeResolutionResult.Rejected rejected:
                    resolutionFailures.Add(
                        new ExactTypeInspectionFailure(
                            ExactTypeInspectionFailureKind.ParticipantRejected,
                            rejected.Failure.Detail,
                            rejected.Assembly.Identity));
                    break;
                case AssemblyContextTypeResolutionResult
                    .UnsupportedBindingPolicy unsupported:
                    resolutionFailures.Add(
                        new ExactTypeInspectionFailure(
                            ExactTypeInspectionFailureKind
                                .BindingPolicyUnsupported,
                            "The participant binding policy does not support acquisition-free exact Type resolution.",
                            unsupported.Assembly.Identity));
                    break;
            }
        }

        if (resolutionFailures.Count > 0)
        {
            return new ExactTypeInspectionResult(
                ExactTypeInspectionOutcome.Unavailable,
                request.Type,
                matchedType,
                Type: null,
                RequestedAssembly: null,
                SupplierAssembly: null,
                ForwardingHops: [],
                Suggestions: [],
                InspectionFailures: detachedInspectionFailures,
                Failures:
                [
                    .. participantFailures,
                    .. resolutionFailures,
                    .. incompleteness,
                ]);
        }
        if (ambiguous || DistinctTerminalCount(resolved) > 1)
        {
            return new ExactTypeInspectionResult(
                ExactTypeInspectionOutcome.Ambiguous,
                request.Type,
                matchedType,
                Type: null,
                RequestedAssembly: null,
                SupplierAssembly: null,
                ForwardingHops: [],
                Suggestions: [],
                InspectionFailures: detachedInspectionFailures,
                Failures:
                [
                    .. participantFailures,
                    .. resolutionFailures,
                    .. incompleteness,
                ]);
        }
        if (resolved.Count == 0)
        {
            return new ExactTypeInspectionResult(
                ExactTypeInspectionOutcome.Unavailable,
                request.Type,
                matchedType,
                Type: null,
                RequestedAssembly: null,
                SupplierAssembly: null,
                ForwardingHops: [],
                Suggestions: [],
                InspectionFailures: detachedInspectionFailures,
                Failures:
                [
                    .. participantFailures,
                    .. resolutionFailures,
                    .. incompleteness,
                ]);
        }

        ResolvedCandidate selected = resolved
            .OrderBy(static candidate => candidate.Candidate.Order)
            .First();
        ResolvedTypeDefinition terminal =
            selected.Resolution.Definition;
        Projection? supplier = projections.FirstOrDefault(projection =>
            ReferenceEquals(
                projection.Participant.Assembly.Registration,
                terminal.Assembly.Assembly.Registration));
        if (supplier?.Entry
            is not AssemblyContextEntry<AssemblyApiSurface>.Available
                supplierSurface)
        {
            return new ExactTypeInspectionResult(
                ExactTypeInspectionOutcome.Unavailable,
                request.Type,
                matchedType,
                Type: null,
                RequestedAssembly:
                    AssemblyIdentity(
                        loaded.Group,
                        selected.Candidate.Participant),
                SupplierAssembly: null,
                ForwardingHops: ForwardingHops(selected.Resolution),
                Suggestions: [],
                InspectionFailures: detachedInspectionFailures,
                Failures:
                [
                    .. participantFailures,
                    .. resolutionFailures,
                    .. incompleteness,
                    new ExactTypeInspectionFailure(
                        ExactTypeInspectionFailureKind.SupplierUnavailable,
                        "The resolved supplier participant did not produce an API surface.",
                        terminal.Assembly.Assembly.Identity),
                ]);
        }

        ApiType? type = supplierSurface.Value.Surface.Types.SingleOrDefault(
            candidate => candidate.DefinitionName is { } definition
                && definition.Equals(terminal.Type));
        if (type is null)
        {
            return new ExactTypeInspectionResult(
                ExactTypeInspectionOutcome.Unavailable,
                request.Type,
                matchedType,
                Type: null,
                RequestedAssembly:
                    AssemblyIdentity(
                        loaded.Group,
                        selected.Candidate.Participant),
                SupplierAssembly:
                    AssemblyIdentity(
                        supplier.Participant,
                        terminal.Address.ModuleVersionId),
                ForwardingHops: ForwardingHops(selected.Resolution),
                Suggestions: [],
                InspectionFailures: detachedInspectionFailures,
                Failures:
                [
                    .. participantFailures,
                    .. resolutionFailures,
                    .. incompleteness,
                    new ExactTypeInspectionFailure(
                        ExactTypeInspectionFailureKind.SupplierUnavailable,
                        "The resolved supplier API surface did not contain the exact terminal Type definition.",
                        terminal.Assembly.Assembly.Identity),
                ]);
        }

        type.IsForwarded = !selected.Resolution.Hops.IsDefaultOrEmpty;
        ImmutableArray<ApiSurfaceInspectionFailure>
            selectedInspectionFailures =
                SelectedInspectionFailures(
                    inspectionFailures,
                    supplierSurface.Value.Surface,
                    type);
        return new ExactTypeInspectionResult(
            ExactTypeInspectionOutcome.Available,
            request.Type,
            selected.Candidate.Definition.ToEscapedFullName(),
            ExactTypeApi.From(type),
            AssemblyIdentity(
                loaded.Group,
                selected.Candidate.Participant),
            AssemblyIdentity(
                supplier.Participant,
                terminal.Address.ModuleVersionId),
            ForwardingHops(selected.Resolution),
            Suggestions: [],
            [
                .. selectedInspectionFailures.Select(
                    ExactTypeApiInspectionFailure.From),
            ],
            [
                .. participantFailures,
                .. resolutionFailures,
                .. incompleteness,
            ]);
    }

    static ImmutableArray<AssemblyContextParticipant> PackageParticipants(
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request)
    {
        var registrations =
            loaded.Members
                .Where(member =>
                    member.Realized
                        is RealizedMemberCoordinate.Package package
                    && package.PackageId.Equals(
                        request.PackageId,
                        StringComparison.OrdinalIgnoreCase)
                    && package.Version.Equals(
                        request.Version,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        package.Framework,
                        request.TargetFramework,
                        StringComparison.OrdinalIgnoreCase))
                .Select(member =>
                    member.Participant.Assembly.Registration)
                .ToHashSet(ReferenceEqualityComparer.Instance);
        return
        [
            .. loaded.Group.Participants.Where(participant =>
                registrations.Contains(
                    participant.Assembly.Registration)),
        ];
    }

    static ImmutableArray<Candidate> Declarations(
        ImmutableArray<Projection> projections)
    {
        var declarations = ImmutableArray.CreateBuilder<Candidate>();
        foreach (Projection projection in projections)
        {
            if (projection.Entry
                is not AssemblyContextEntry<AssemblyApiSurface>.Available
                    available)
            {
                continue;
            }

            foreach (ApiType type in available.Value.Surface.Types)
            {
                if (type.DefinitionName is { } definition)
                {
                    declarations.Add(
                        new Candidate(
                            projection.Order,
                            projection.Participant,
                            available.Value,
                            definition));
                }
            }
            foreach (TypeForwarder forwarder
                in available.Value.Surface.TypeForwarders)
            {
                if (forwarder.DefinitionName is { } definition)
                {
                    declarations.Add(
                        new Candidate(
                            projection.Order,
                            projection.Participant,
                            available.Value,
                            definition));
                }
            }
        }

        return declarations.DrainToImmutable();
    }

    static ImmutableArray<ExactTypeInspectionFailure> ParticipantFailures(
        ImmutableArray<Projection> projections)
    {
        var failures =
            ImmutableArray.CreateBuilder<ExactTypeInspectionFailure>();
        foreach (Projection projection in projections)
        {
            switch (projection.Entry)
            {
                case AssemblyContextEntry<AssemblyApiSurface>.Rejected
                    rejected:
                    failures.Add(new ExactTypeInspectionFailure(
                        ExactTypeInspectionFailureKind.ParticipantRejected,
                        rejected.Failure.Detail,
                        rejected.Subject.Identity));
                    break;
                case AssemblyContextEntry<AssemblyApiSurface>.Failed failed:
                    failures.Add(new ExactTypeInspectionFailure(
                        ExactTypeInspectionFailureKind.MetadataMalformed,
                        failed.Error.Message,
                        failed.Subject.Identity));
                    break;
            }
        }

        return failures.DrainToImmutable();
    }

    static ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures(
        ImmutableArray<Projection> projections) =>
    [
        .. projections
            .SelectMany(projection =>
                projection.Entry
                    is AssemblyContextEntry<AssemblyApiSurface>.Available
                        available
                    ? available.Value.InspectionFailures
                    : [])
            .Distinct(),
    ];

    static bool MayAffectTypeLookup(
        ApiSurfaceInspectionFailure failure,
        string requestedType)
    {
        if (failure.OwningTypeDefinition is { } owner)
        {
            return TypeMatcher.MatchesTypeFilter(
                owner.ToEscapedFullName(),
                requestedType);
        }
        if (!failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
        {
            return failure.AffectedTypeDefinitions.Any(
                affected => TypeMatcher.MatchesTypeFilter(
                    affected.ToEscapedFullName(),
                    requestedType));
        }

        return true;
    }

    static ImmutableArray<ApiSurfaceInspectionFailure>
        SelectedInspectionFailures(
            ImmutableArray<ApiSurfaceInspectionFailure> inspectionFailures,
            ApiSurface surface,
            ApiType type)
    {
        var retainedTokens = new HashSet<int>();
        Add(type.MetadataToken);
        foreach (ApiMember member in type.Members)
        {
            Add(member.MetadataToken);
            Add(member.GetterToken);
            Add(member.SetterToken);
            Add(member.AdderToken);
            Add(member.RemoverToken);
        }

        var projected = new ApiSurface();
        projected.MergeInspectionFailuresFrom(
            surface,
            subject => retainedTokens.Contains(subject.SubjectToken),
            includeNonConstraintFailures: false);
        return
        [
            .. inspectionFailures.Where(static failure =>
                failure.Operation
                    != ApiSurface.ConstraintResolutionOperation),
            .. projected.InspectionFailures,
        ];

        void Add(int? token)
        {
            if (token is int value)
                retainedTokens.Add(value);
        }
    }

    static ImmutableArray<ExactTypeInspectionFailure> Incompleteness(
        ImmutableArray<ApiSurfaceInspectionFailure> inspectionFailures) =>
    [
        .. inspectionFailures
            .Where(static failure =>
                failure.Operation
                    != ApiSurface.ConstraintResolutionOperation)
            .Select(failure =>
                new ExactTypeInspectionFailure(
                    ExactTypeInspectionFailureKind.InspectionIncomplete,
                    $"{failure.Operation}: {failure.Kind}: {failure.Detail}",
                    failure.SubjectAssembly)),
    ];

    static int DistinctTerminalCount(
        ImmutableArray<ResolvedCandidate>.Builder resolved) =>
        resolved
            .Select(candidate => (
                candidate.Resolution.Definition.Address,
                candidate.Resolution.Definition.Assembly.Assembly.Identity))
            .Distinct()
            .Count();

    static ExactTypeAssemblyIdentity AssemblyIdentity(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        AssemblyImageAccessResult<Guid> access =
            group.UseAssemblySession(
                participant.Assembly,
                static session => session.ModuleVersionId());
        return access switch
        {
            AssemblyImageAccessResult<Guid>.Available available =>
                AssemblyIdentity(participant, available.Value),
            AssemblyImageAccessResult<Guid>.Rejected rejected =>
                throw new InvalidOperationException(
                    "The exact Type assembly identity could not be read: "
                    + rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "The exact Type assembly identity could not be read."),
        };
    }

    static ExactTypeAssemblyIdentity AssemblyIdentity(
        AssemblyContextParticipant participant,
        Guid moduleVersionId) =>
        new(participant.Assembly.Identity, moduleVersionId);

    static ImmutableArray<ExactTypeForwardingHop> ForwardingHops(
        TypeResolutionOutcome.Resolved resolution) =>
    [
        .. resolution.Hops.Select(hop =>
            new ExactTypeForwardingHop(
                hop.SourceAssembly.Assembly.Identity,
                hop.TargetReference)),
    ];
}
