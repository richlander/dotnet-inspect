using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

public enum ResourceEffectInterfaceApplicationWorkDimension
{
    CandidateApplications,
    InterfaceImplementations,
    MethodImplementations,
    CandidateMethods,
    SignatureNodes,
    RetainedApplications,
}

public enum ResourceEffectInterfaceApplicationGapKind
{
    AmbiguousInterface,
    UnsupportedMetadata,
    IncompleteMetadata,
    WorkLimitExceeded,
}

public sealed record ResourceEffectInterfaceApplicationGap(
    ResourceEffectInterfaceApplicationGapKind Kind)
{
    public string? Detail { get; init; }
    public ResourceEffectInterfaceApplicationWorkDimension? WorkDimension
        { get; init; }
    public long? Limit { get; init; }
    public long? RequiredWork { get; init; }
}

public sealed class ResourceEffectInterfaceApplicationLimits
{
    public ResourceEffectInterfaceApplicationLimits(
        int maxCandidateApplications = 100_000,
        int maxInterfaceImplementations =
            MetadataSafetyPolicy.MaxRelationshipNodes,
        int maxMethodImplementations =
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        int maxCandidateMethods =
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        int maxSignatureNodes =
            MetadataSafetyPolicy.MaxSignatureTypeNodes,
        int maxRetainedApplications = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxCandidateApplications);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxInterfaceImplementations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxMethodImplementations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxCandidateMethods);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSignatureNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxRetainedApplications);

        MaxCandidateApplications = maxCandidateApplications;
        MaxInterfaceImplementations = maxInterfaceImplementations;
        MaxMethodImplementations = maxMethodImplementations;
        MaxCandidateMethods = maxCandidateMethods;
        MaxSignatureNodes = maxSignatureNodes;
        MaxRetainedApplications = maxRetainedApplications;
    }

    public int MaxCandidateApplications { get; }
    public int MaxInterfaceImplementations { get; }
    public int MaxMethodImplementations { get; }
    public int MaxCandidateMethods { get; }
    public int MaxSignatureNodes { get; }
    public int MaxRetainedApplications { get; }
}

public sealed class ResourceEffectInterfaceImplementationEvidence
{
    internal ResourceEffectInterfaceImplementationEvidence(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity assembly,
        Guid moduleVersionId,
        int declaringTypeToken,
        int interfaceImplementationToken,
        TypeRef closedInterfaceType)
    {
        Catalog = catalog;
        Generation = generation;
        Registration = registration;
        Assembly = assembly;
        ModuleVersionId = moduleVersionId;
        DeclaringTypeToken = declaringTypeToken;
        InterfaceImplementationToken = interfaceImplementationToken;
        ClosedInterfaceType = closedInterfaceType;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public int DeclaringTypeToken { get; }
    public int InterfaceImplementationToken { get; }
    public TypeRef ClosedInterfaceType { get; }
}

public abstract class ResourceEffectMethodImplementationEvidence
{
    private protected ResourceEffectMethodImplementationEvidence(
        int implementationMethodToken) =>
        ImplementationMethodToken = implementationMethodToken;

    public int ImplementationMethodToken { get; }

    public sealed class Explicit
        : ResourceEffectMethodImplementationEvidence
    {
        internal Explicit(
            int methodImplementationToken,
            int implementationMethodToken,
            MemberRef declaration)
            : base(implementationMethodToken)
        {
            MethodImplementationToken = methodImplementationToken;
            Declaration = declaration;
        }

        public int MethodImplementationToken { get; }
        public MemberRef Declaration { get; }
    }

    public sealed class Implicit
        : ResourceEffectMethodImplementationEvidence
    {
        internal Implicit(
            int implementationMethodToken,
            MemberRef implementation)
            : base(implementationMethodToken) =>
            Implementation = implementation;

        public MemberRef Implementation { get; }
    }
}

public sealed class ResourceEffectInterfaceApplicationEvidence
{
    internal ResourceEffectInterfaceApplicationEvidence(
        DirectCallDefinitionOccurrence interfaceDeclaration,
        DirectCallDefinitionOccurrence implementation,
        ResourceEffectInterfaceImplementationEvidence interfacePath,
        ResourceEffectMethodImplementationEvidence method)
    {
        InterfaceDeclaration = interfaceDeclaration;
        Implementation = implementation;
        InterfacePath = interfacePath;
        Method = method;
    }

    public DirectCallDefinitionOccurrence InterfaceDeclaration { get; }
    public DirectCallDefinitionOccurrence Implementation { get; }
    public ResourceEffectInterfaceImplementationEvidence InterfacePath
        { get; }
    public ResourceEffectMethodImplementationEvidence Method { get; }
}

public abstract class ResourceEffectInterfaceApplication
{
    private protected ResourceEffectInterfaceApplication(
        DirectCallDefinitionResolution.Resolved interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall)
    {
        InterfaceCall = interfaceCall;
        ImplementationCall = implementationCall;
    }

    public DirectCallDefinitionResolution.Resolved InterfaceCall { get; }
    public DirectCallDefinitionResolution.Resolved ImplementationCall
        { get; }

    public sealed class Applied : ResourceEffectInterfaceApplication
    {
        internal Applied(
            DirectCallDefinitionResolution.Resolved interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            ResourceEffectInterfaceApplicationEvidence evidence)
            : base(interfaceCall, implementationCall) =>
            Evidence = evidence;

        public ResourceEffectInterfaceApplicationEvidence Evidence { get; }
    }

    public sealed class NotApplicable : ResourceEffectInterfaceApplication
    {
        internal NotApplicable(
            DirectCallDefinitionResolution.Resolved interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall)
            : base(interfaceCall, implementationCall)
        {
        }
    }

    public sealed class Ambiguous : ResourceEffectInterfaceApplication
    {
        internal Ambiguous(
            DirectCallDefinitionResolution.Resolved interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            ResourceEffectInterfaceApplicationGap gap)
            : base(interfaceCall, implementationCall) =>
            Gap = gap;

        public ResourceEffectInterfaceApplicationGap Gap { get; }
    }

    public sealed class Unsupported : ResourceEffectInterfaceApplication
    {
        internal Unsupported(
            DirectCallDefinitionResolution.Resolved interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            ResourceEffectInterfaceApplicationGap gap)
            : base(interfaceCall, implementationCall) =>
            Gap = gap;

        public ResourceEffectInterfaceApplicationGap Gap { get; }
    }

    public sealed class Incomplete : ResourceEffectInterfaceApplication
    {
        internal Incomplete(
            DirectCallDefinitionResolution.Resolved interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            ResourceEffectInterfaceApplicationGap gap)
            : base(interfaceCall, implementationCall) =>
            Gap = gap;

        public ResourceEffectInterfaceApplicationGap Gap { get; }
    }
}

public sealed class ResourceEffectInterfaceApplicationIndex
{
    readonly ImmutableArray<ResourceEffectInterfaceApplication> _applications;

    internal ResourceEffectInterfaceApplicationIndex(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        ImmutableArray<ResourceEffectInterfaceApplication> applications,
        ResourceEffectInterfaceApplicationGap? globalGap)
    {
        Catalog = catalog;
        Generation = generation;
        _applications = applications;
        GlobalGap = globalGap;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public ImmutableArray<ResourceEffectInterfaceApplication> Applications =>
        _applications;
    public ResourceEffectInterfaceApplicationGap? GlobalGap { get; }

    public ImmutableArray<ResourceEffectInterfaceApplication> For(
        DirectCallDefinitionResolution.Resolved interfaceCall)
    {
        ArgumentNullException.ThrowIfNull(interfaceCall);
        if (interfaceCall.Catalog != Catalog
            || !ReferenceEquals(interfaceCall.Generation, Generation))
        {
            throw new ArgumentException(
                "The interface call belongs to a different catalog generation.",
                nameof(interfaceCall));
        }
        return
        [
            .. _applications.Where(application =>
                ReferenceEquals(
                    application.InterfaceCall,
                    interfaceCall)),
        ];
    }
}

internal sealed class ResourceEffectInterfaceApplicationPlan
{
    const byte CallingConventionMask = 0x0F;
    const byte Generic = 0x10;
    const byte HasThis = 0x20;
    const byte ExplicitThis = 0x40;

    readonly ImmutableDictionary<
        ConcreteTypeKey,
        PendingConcreteType> _concreteTypes;
    readonly ImmutableHashSet<ApplicationPairKey> _candidatePairs;
    readonly ImmutableArray<TypeResolutionRequest> _requests;
    readonly ResourceEffectInterfaceApplicationLimits _limits;
    readonly ResourceEffectInterfaceApplicationGap? _globalGap;

    ResourceEffectInterfaceApplicationPlan(
        ImmutableDictionary<ConcreteTypeKey, PendingConcreteType>
            concreteTypes,
        ImmutableHashSet<ApplicationPairKey> candidatePairs,
        ImmutableArray<TypeResolutionRequest> requests,
        ResourceEffectInterfaceApplicationLimits limits,
        ResourceEffectInterfaceApplicationGap? globalGap)
    {
        _concreteTypes = concreteTypes;
        _candidatePairs = candidatePairs;
        _requests = requests;
        _limits = limits;
        _globalGap = globalGap;
    }

    internal ImmutableArray<TypeResolutionRequest> Requests => _requests;

    internal static ResourceEffectInterfaceApplicationPlan Create(
        DirectCallDefinitionResolutionOutcome.Completed calls,
        ResourceEffectAdmission admission,
        ResourceEffectInterfaceApplicationLimits limits,
        CancellationToken cancellationToken)
    {
        var interfaces = calls.Results
            .OfType<DirectCallDefinitionResolution.Resolved>()
            .Where(call => call.Definition.IsInterfaceDefinition)
            .Where(call => IsSelectedInterfaceCall(
                admission,
                call))
            .ToImmutableArray();
        var implementations = calls.Results
            .OfType<DirectCallDefinitionResolution.Resolved>()
            .Where(call => !call.Definition.IsInterfaceDefinition)
            .ToImmutableArray();
        var pairs = ImmutableHashSet.CreateBuilder<ApplicationPairKey>();
        ResourceEffectInterfaceApplicationGap? globalGap = null;
        long candidateCount = 0;
        foreach (DirectCallDefinitionResolution.Resolved interfaceCall
            in interfaces)
        {
            foreach (DirectCallDefinitionResolution.Resolved implementation
                in implementations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidateCount++;
                if (candidateCount > limits.MaxCandidateApplications)
                {
                    globalGap ??= WorkGap(
                        ResourceEffectInterfaceApplicationWorkDimension
                            .CandidateApplications,
                        limits.MaxCandidateApplications,
                        candidateCount);
                    continue;
                }
                pairs.Add(new(
                    interfaceCall.PhysicalInvocation,
                    implementation.PhysicalInvocation));
            }
        }

        var concreteTypes =
            ImmutableDictionary.CreateBuilder<
                ConcreteTypeKey,
                PendingConcreteType>();
        var requests = new List<TypeResolutionRequest>();
        var work = new PlanningWork();
        foreach (DirectCallDefinitionResolution.Resolved implementation
            in implementations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConcreteTypeKey key = ConcreteTypeKey.For(implementation);
            if (concreteTypes.ContainsKey(key))
                continue;
            PendingConcreteType pending = ReadConcreteType(
                implementation,
                limits,
                work,
                cancellationToken);
            concreteTypes.Add(key, pending);
            requests.AddRange(pending.Requests);
        }

        return new ResourceEffectInterfaceApplicationPlan(
            concreteTypes.ToImmutable(),
            pairs.ToImmutable(),
            [
                .. requests.Distinct(
                    TypeResolutionRequestComparer.Instance),
            ],
            limits,
            globalGap);
    }

    static bool IsSelectedInterfaceCall(
        ResourceEffectAdmission admission,
        DirectCallDefinitionResolution.Resolved call)
    {
        foreach (AdmittedResourceEffectModel model in admission.Models)
        {
            foreach (AdmittedResourceEffectDeclaration declaration
                in model.Declarations)
            {
                if (ResourceEffectSelectorBinder.Bind(
                        declaration,
                        call)
                    is ResourceEffectSelectorBinding.Resolved)
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal ResourceEffectInterfaceApplicationIndex Resolve(
        TypeResolutionContext context,
        DirectCallDefinitionResolutionOutcome.Completed calls,
        CancellationToken cancellationToken)
    {
        if (context.Catalog != calls.Catalog
            || !ReferenceEquals(context.Generation, calls.Generation))
        {
            throw new ArgumentException(
                "Direct calls and interface application must use the same catalog generation.",
                nameof(calls));
        }

        var interfaces = calls.Results
            .OfType<DirectCallDefinitionResolution.Resolved>()
            .Where(call => call.Definition.IsInterfaceDefinition)
            .ToImmutableArray();
        var implementations = calls.Results
            .OfType<DirectCallDefinitionResolution.Resolved>()
            .Where(call => !call.Definition.IsInterfaceDefinition)
            .ToImmutableArray();
        var applications =
            ImmutableArray.CreateBuilder<
                ResourceEffectInterfaceApplication>();
        ResourceEffectInterfaceApplicationGap? globalGap = _globalGap;
        long retained = 0;
        foreach (DirectCallDefinitionResolution.Resolved interfaceCall
            in interfaces)
        {
            foreach (DirectCallDefinitionResolution.Resolved implementation
                in implementations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pair = new ApplicationPairKey(
                    interfaceCall.PhysicalInvocation,
                    implementation.PhysicalInvocation);
                if (!_candidatePairs.Contains(pair))
                    continue;
                retained++;
                if (retained > _limits.MaxRetainedApplications)
                {
                    globalGap ??= WorkGap(
                        ResourceEffectInterfaceApplicationWorkDimension
                            .RetainedApplications,
                        _limits.MaxRetainedApplications,
                        retained);
                    continue;
                }
                if (!_concreteTypes.TryGetValue(
                        ConcreteTypeKey.For(implementation),
                        out PendingConcreteType? concreteType))
                {
                    applications.Add(Incomplete(
                        interfaceCall,
                        implementation,
                        ResourceEffectInterfaceApplicationGapKind
                            .IncompleteMetadata));
                    continue;
                }
                applications.Add(
                    ResolvePair(
                        context,
                        interfaceCall,
                        implementation,
                        concreteType));
            }
        }
        return new ResourceEffectInterfaceApplicationIndex(
            context.Catalog,
            context.Generation,
            applications.ToImmutable(),
            globalGap);
    }

    static ResourceEffectInterfaceApplication ResolvePair(
        TypeResolutionContext context,
        DirectCallDefinitionResolution.Resolved interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall,
        PendingConcreteType concreteType)
    {
        if (concreteType.GlobalGap is { } typeGap)
        {
            return typeGap.Kind switch
            {
                ResourceEffectInterfaceApplicationGapKind
                    .UnsupportedMetadata =>
                    new ResourceEffectInterfaceApplication.Unsupported(
                        interfaceCall,
                        implementationCall,
                        typeGap),
                _ => new ResourceEffectInterfaceApplication.Incomplete(
                    interfaceCall,
                    implementationCall,
                    typeGap),
            };
        }

        CatalogTypeShape interfaceDeclaringType =
            interfaceCall.Definition.Correspondence.DeclaringType;
        var matchedInterfaces =
            new List<PendingInterfaceImplementation>();
        ResolutionDisposition interfaceFailure =
            ResolutionDisposition.None;
        string? interfaceFailureDetail = null;
        foreach (PendingInterfaceImplementation candidate
            in concreteType.Interfaces)
        {
            CatalogMemberJoinProjection projection =
                candidate.TypePlan.Project(context);
            if (projection is not CatalogMemberJoinProjection.Issued issued)
            {
                if (CouldNameType(
                        candidate.ClosedInterfaceType,
                        interfaceCall.Definition.Member.DeclaringType))
                {
                    interfaceFailure = Stronger(
                        interfaceFailure,
                        Classify(projection));
                    interfaceFailureDetail = ProjectionDetail(
                        projection);
                }
                continue;
            }
            if (issued.Key.Kind
                != CatalogMemberCorrespondenceKind.Exact)
            {
                if (CouldNameType(
                        candidate.ClosedInterfaceType,
                        interfaceCall.Definition.Member.DeclaringType))
                {
                    interfaceFailure = Stronger(
                        interfaceFailure,
                        ResolutionDisposition.Incomplete);
                    interfaceFailureDetail =
                        "InterfaceImpl projection was indeterminate.";
                }
                continue;
            }
            if (issued.Key.DeclaringType != interfaceDeclaringType)
            {
                continue;
            }
            if (!GenericOwnerFramesMatch(
                    interfaceCall,
                    implementationCall,
                    interfaceDeclaringType))
            {
                interfaceFailure = Stronger(
                    interfaceFailure,
                    ResolutionDisposition.Incomplete);
                continue;
            }
            matchedInterfaces.Add(candidate);
        }
        if (matchedInterfaces.Count > 1)
        {
            return new ResourceEffectInterfaceApplication.Ambiguous(
                interfaceCall,
                implementationCall,
                new(
                    ResourceEffectInterfaceApplicationGapKind
                        .AmbiguousInterface));
        }
        if (interfaceFailure != ResolutionDisposition.None)
        {
            return Failure(
                interfaceCall,
                implementationCall,
                interfaceFailure,
                "InterfaceImpl projection was not exact: "
                + interfaceFailureDetail);
        }
        if (matchedInterfaces.Count == 0)
        {
            return new ResourceEffectInterfaceApplication.NotApplicable(
                interfaceCall,
                implementationCall);
        }

        PendingInterfaceImplementation interfacePath =
            matchedInterfaces[0];
        var explicitMatches = new List<PendingMethodImplementation>();
        ResolutionDisposition methodImplFailure =
            ResolutionDisposition.None;
        foreach (PendingMethodImplementation candidate
            in concreteType.MethodImplementations)
        {
            if (candidate.Declaration.Kind == MemberKind.Unsupported)
            {
                methodImplFailure = Stronger(
                    methodImplFailure,
                    ResolutionDisposition.Unsupported);
                continue;
            }
            if (!string.Equals(
                    candidate.Declaration.Name,
                    interfaceCall.Definition.Correspondence.Name,
                    StringComparison.Ordinal))
            {
                continue;
            }
            CatalogMemberJoinProjection projection =
                candidate.DeclarationPlan.Project(context);
            if (projection is not CatalogMemberJoinProjection.Issued issued)
            {
                methodImplFailure = Stronger(
                    methodImplFailure,
                    Classify(projection));
                continue;
            }
            if (issued.Key.Kind
                != CatalogMemberCorrespondenceKind.Exact)
            {
                methodImplFailure = Stronger(
                    methodImplFailure,
                    ResolutionDisposition.Incomplete);
                continue;
            }
            if (issued.Key == interfaceCall.Definition.Correspondence)
            {
                explicitMatches.Add(candidate);
            }
        }
        if (methodImplFailure != ResolutionDisposition.None)
        {
            return Failure(
                interfaceCall,
                implementationCall,
                methodImplFailure,
                "A potentially matching MethodImpl declaration was not exact.");
        }
        if (explicitMatches.Count > 1)
        {
            return new ResourceEffectInterfaceApplication.Ambiguous(
                interfaceCall,
                implementationCall,
                new(
                    ResourceEffectInterfaceApplicationGapKind
                        .AmbiguousInterface));
        }
        if (explicitMatches.Count == 1)
        {
            PendingMethodImplementation explicitMatch =
                explicitMatches[0];
            if (explicitMatch.BodyToken == 0
                || !concreteType.Methods.TryGetValue(
                    explicitMatch.BodyToken,
                    out PendingMethod? explicitBody))
            {
                return new ResourceEffectInterfaceApplication.Unsupported(
                    interfaceCall,
                    implementationCall,
                    new(
                        ResourceEffectInterfaceApplicationGapKind
                            .UnsupportedMetadata));
            }
            CatalogMemberJoinProjection bodyProjection =
                explicitBody.Plan.Project(context);
            if (bodyProjection is not CatalogMemberJoinProjection.Issued
                    bodyIssued)
            {
                return Failure(
                    interfaceCall,
                    implementationCall,
                    Classify(bodyProjection),
                    "The authoritative MethodImpl body could not be projected.");
            }
            if (bodyIssued.Key.Kind
                != CatalogMemberCorrespondenceKind.Exact)
            {
                return Failure(
                    interfaceCall,
                    implementationCall,
                    ResolutionDisposition.Incomplete,
                    "The authoritative MethodImpl body projection was indeterminate.");
            }
            if (!SlotShapeMatches(
                    interfaceCall.Definition.Correspondence,
                    bodyIssued.Key,
                    requireName: false))
            {
                return new ResourceEffectInterfaceApplication.Unsupported(
                    interfaceCall,
                    implementationCall,
                    new(
                        ResourceEffectInterfaceApplicationGapKind
                            .UnsupportedMetadata));
            }
            if (explicitMatch.BodyToken
                != implementationCall.Definition.MetadataToken)
            {
                return new ResourceEffectInterfaceApplication.NotApplicable(
                    interfaceCall,
                    implementationCall);
            }
            return Applied(
                context,
                interfaceCall,
                implementationCall,
                concreteType,
                interfacePath,
                new ResourceEffectMethodImplementationEvidence.Explicit(
                    explicitMatch.RowToken,
                    explicitMatch.BodyToken,
                    explicitMatch.Declaration));
        }

        var implicitMatches = new List<PendingMethod>();
        ResolutionDisposition implicitFailure =
            ResolutionDisposition.None;
        foreach (PendingMethod candidate in concreteType.Methods.Values)
        {
            if (!string.Equals(
                    candidate.Member.Name,
                    interfaceCall.Definition.Correspondence.Name,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (!candidate.IsImplicitCandidate)
                continue;
            CatalogMemberJoinProjection projection =
                candidate.Plan.Project(context);
            if (projection is not CatalogMemberJoinProjection.Issued issued)
            {
                implicitFailure = Stronger(
                    implicitFailure,
                    Classify(projection));
                continue;
            }
            if (issued.Key.Kind
                != CatalogMemberCorrespondenceKind.Exact)
            {
                implicitFailure = Stronger(
                    implicitFailure,
                    ResolutionDisposition.Incomplete);
                continue;
            }
            if (SlotShapeMatches(
                    interfaceCall.Definition.Correspondence,
                    issued.Key,
                    requireName: true))
            {
                implicitMatches.Add(candidate);
            }
        }
        if (implicitFailure != ResolutionDisposition.None)
        {
            return Failure(
                interfaceCall,
                implementationCall,
                implicitFailure,
                "A potentially matching implicit MethodDef was not exact.");
        }
        if (implicitMatches.Count > 1)
        {
            return new ResourceEffectInterfaceApplication.Ambiguous(
                interfaceCall,
                implementationCall,
                new(
                    ResourceEffectInterfaceApplicationGapKind
                        .AmbiguousInterface));
        }
        if (implicitMatches.Count == 0)
        {
            return new ResourceEffectInterfaceApplication.Incomplete(
                interfaceCall,
                implementationCall,
                new(
                    ResourceEffectInterfaceApplicationGapKind
                        .IncompleteMetadata)
                {
                    Detail =
                        "No same-type implementation was found; inherited "
                        + "and default-interface implementation search is "
                        + "outside this relation.",
                });
        }
        if (implicitMatches[0].Token
            != implementationCall.Definition.MetadataToken)
        {
            return new ResourceEffectInterfaceApplication.NotApplicable(
                interfaceCall,
                implementationCall);
        }
        PendingMethod implicitMatch = implicitMatches[0];
        return Applied(
            context,
            interfaceCall,
            implementationCall,
            concreteType,
            interfacePath,
            new ResourceEffectMethodImplementationEvidence.Implicit(
                implicitMatch.Token,
                implicitMatch.Member));
    }

    static ResourceEffectInterfaceApplication Applied(
        TypeResolutionContext context,
        DirectCallDefinitionResolution.Resolved interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall,
        PendingConcreteType concreteType,
        PendingInterfaceImplementation interfacePath,
        ResourceEffectMethodImplementationEvidence method) =>
        new ResourceEffectInterfaceApplication.Applied(
            interfaceCall,
            implementationCall,
            new ResourceEffectInterfaceApplicationEvidence(
                interfaceCall.Definition,
                implementationCall.Definition,
                new ResourceEffectInterfaceImplementationEvidence(
                    context.Catalog,
                    context.Generation,
                    concreteType.Assembly.Registration,
                    concreteType.Assembly.Identity,
                    concreteType.ModuleVersionId,
                    concreteType.TypeToken,
                    interfacePath.RowToken,
                    interfacePath.ClosedInterfaceType),
                method));

    static ResourceEffectInterfaceApplication Failure(
        DirectCallDefinitionResolution.Resolved interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall,
        ResolutionDisposition failure,
        string detail)
    {
        ResourceEffectInterfaceApplicationGap gap = new(
            failure == ResolutionDisposition.Ambiguous
                ? ResourceEffectInterfaceApplicationGapKind
                    .AmbiguousInterface
                : failure == ResolutionDisposition.Unsupported
                    ? ResourceEffectInterfaceApplicationGapKind
                        .UnsupportedMetadata
                    : ResourceEffectInterfaceApplicationGapKind
                        .IncompleteMetadata)
        {
            Detail = detail,
        };
        return failure switch
        {
            ResolutionDisposition.Ambiguous =>
                new ResourceEffectInterfaceApplication.Ambiguous(
                    interfaceCall,
                    implementationCall,
                    gap),
            ResolutionDisposition.Unsupported =>
                new ResourceEffectInterfaceApplication.Unsupported(
                    interfaceCall,
                    implementationCall,
                    gap),
            _ => new ResourceEffectInterfaceApplication.Incomplete(
                interfaceCall,
                implementationCall,
                gap),
        };
    }

    static ResourceEffectInterfaceApplication.Incomplete Incomplete(
        DirectCallDefinitionResolution.Resolved interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall,
        ResourceEffectInterfaceApplicationGapKind kind) =>
        new(
            interfaceCall,
            implementationCall,
            new ResourceEffectInterfaceApplicationGap(kind));

    static PendingConcreteType ReadConcreteType(
        DirectCallDefinitionResolution.Resolved implementation,
        ResourceEffectInterfaceApplicationLimits limits,
        PlanningWork work,
        CancellationToken cancellationToken)
    {
        ResolvedAssemblyReference assembly =
            implementation.Definition.AssemblyReference;
        try
        {
            using Stream stream = assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return PendingConcreteType.Unsupported(assembly);
            MetadataReader reader = peReader.GetMetadataReader();
            Guid mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (mvid != implementation.Definition.ModuleVersionId
                || AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                    != assembly.Identity)
            {
                return PendingConcreteType.Incomplete(assembly);
            }
            EntityHandle methodEntity = MetadataTokens.EntityHandle(
                implementation.Definition.MetadataToken);
            if (methodEntity.Kind != HandleKind.MethodDefinition)
                return PendingConcreteType.Unsupported(assembly);
            MethodDefinition definition = reader.GetMethodDefinition(
                (MethodDefinitionHandle)methodEntity);
            TypeDefinitionHandle typeHandle = definition.GetDeclaringType();
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            ImmutableArray<TypeRef> typeArguments =
                implementation.Call.Callee.DeclaringType.Kind
                    == TypeRefKind.GenericInstance
                    ? implementation.Call.Callee.DeclaringType.TypeArguments
                    : [];
            if (!MemberResolver.HasExactGenericParameters(
                    reader,
                    type.GetGenericParameters(),
                    typeArguments.Length))
            {
                return PendingConcreteType.Unsupported(assembly);
            }
            GenericScope scope = new(
                LibraryBodyAsyncSiblingSignatureMatcher
                    .GenericParameterNames(
                        reader,
                        type.GetGenericParameters()),
                []);
            var interfaces =
                ImmutableArray.CreateBuilder<
                    PendingInterfaceImplementation>();
            var methodImplementations =
                ImmutableArray.CreateBuilder<
                    PendingMethodImplementation>();
            var methods =
                ImmutableDictionary.CreateBuilder<int, PendingMethod>();
            var requests = new List<TypeResolutionRequest>();

            foreach (InterfaceImplementationHandle handle
                in type.GetInterfaceImplementations())
            {
                cancellationToken.ThrowIfCancellationRequested();
                work.InterfaceImplementations++;
                if (work.InterfaceImplementations
                    > limits.MaxInterfaceImplementations)
                {
                    return PendingConcreteType.Limit(
                        assembly,
                        ResourceEffectInterfaceApplicationWorkDimension
                            .InterfaceImplementations,
                        limits.MaxInterfaceImplementations,
                        work.InterfaceImplementations);
                }
                InterfaceImplementation row =
                    reader.GetInterfaceImplementation(handle);
                TypeRef closedInterface =
                    DecodeType(reader, row.Interface, scope)
                        .Instantiate(typeArguments, []);
                if (!work.Charge(closedInterface, limits.MaxSignatureNodes))
                {
                    return PendingConcreteType.Limit(
                        assembly,
                        ResourceEffectInterfaceApplicationWorkDimension
                            .SignatureNodes,
                        limits.MaxSignatureNodes,
                        work.SignatureNodes);
                }
                MemberRef marker = Marker(closedInterface);
                CatalogMemberCorrespondencePlan plan =
                    CatalogMemberCorrespondencePlan.Create(
                        assembly,
                        marker,
                        requiresOpenSignature: false);
                requests.AddRange(plan.Requests);
                interfaces.Add(new(
                    MetadataTokens.GetToken(handle),
                    closedInterface,
                    plan));
            }

            foreach (MethodDefinitionHandle handle in type.GetMethods())
            {
                cancellationToken.ThrowIfCancellationRequested();
                work.CandidateMethods++;
                if (work.CandidateMethods > limits.MaxCandidateMethods)
                {
                    return PendingConcreteType.Limit(
                        assembly,
                        ResourceEffectInterfaceApplicationWorkDimension
                            .CandidateMethods,
                        limits.MaxCandidateMethods,
                        work.CandidateMethods);
                }
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                MemberRef member = InstantiateMember(
                    MemberResolver.ResolveMethod(
                        reader,
                        handle,
                        scope),
                    typeArguments);
                if (!work.Charge(member, limits.MaxSignatureNodes))
                {
                    return PendingConcreteType.Limit(
                        assembly,
                        ResourceEffectInterfaceApplicationWorkDimension
                            .SignatureNodes,
                        limits.MaxSignatureNodes,
                        work.SignatureNodes);
                }
                CatalogMemberCorrespondencePlan plan =
                    CatalogMemberCorrespondencePlan.Create(
                        assembly,
                        member,
                        requiresOpenSignature: true);
                requests.AddRange(plan.Requests);
                MethodAttributes attributes = method.Attributes;
                methods.Add(
                    MetadataTokens.GetToken(handle),
                    new PendingMethod(
                        MetadataTokens.GetToken(handle),
                        member,
                        plan,
                        IsImplicitCandidate(attributes, member)));
            }

            foreach (MethodImplementationHandle handle
                in type.GetMethodImplementations())
            {
                cancellationToken.ThrowIfCancellationRequested();
                work.MethodImplementations++;
                if (work.MethodImplementations
                    > limits.MaxMethodImplementations)
                {
                    return PendingConcreteType.Limit(
                        assembly,
                        ResourceEffectInterfaceApplicationWorkDimension
                            .MethodImplementations,
                        limits.MaxMethodImplementations,
                        work.MethodImplementations);
                }
                MethodImplementation row =
                    reader.GetMethodImplementation(handle);
                MemberRef declaration = InstantiateMember(
                    MemberResolver.ResolveMethod(
                        reader,
                        row.MethodDeclaration,
                        scope),
                    typeArguments);
                if (!work.Charge(
                        declaration,
                        limits.MaxSignatureNodes))
                {
                    return PendingConcreteType.Limit(
                        assembly,
                        ResourceEffectInterfaceApplicationWorkDimension
                            .SignatureNodes,
                        limits.MaxSignatureNodes,
                        work.SignatureNodes);
                }
                CatalogMemberCorrespondencePlan plan =
                    CatalogMemberCorrespondencePlan.Create(
                        assembly,
                        declaration,
                        requiresOpenSignature: true);
                requests.AddRange(plan.Requests);
                methodImplementations.Add(new(
                    MetadataTokens.GetToken(handle),
                    row.MethodBody.Kind
                        == HandleKind.MethodDefinition
                            ? MetadataTokens.GetToken(row.MethodBody)
                            : 0,
                    declaration,
                    plan));
            }

            return new PendingConcreteType(
                assembly,
                mvid,
                MetadataTokens.GetToken(typeHandle),
                interfaces.ToImmutable(),
                methodImplementations.ToImmutable(),
                methods.ToImmutable(),
                [
                    .. requests.Distinct(
                        TypeResolutionRequestComparer.Instance),
                ],
                GlobalGap: null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return PendingConcreteType.Incomplete(assembly);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return PendingConcreteType.Unsupported(assembly);
        }
    }

    static TypeRef DecodeType(
        MetadataReader reader,
        EntityHandle handle,
        GenericScope scope) =>
        handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    (TypeDefinitionHandle)handle,
                    0),
            HandleKind.TypeReference =>
                TypeRefDecoder.Instance.GetTypeFromReference(
                    reader,
                    (TypeReferenceHandle)handle,
                    0),
            HandleKind.TypeSpecification =>
                TypeRefDecoder.Instance.GetTypeFromSpecification(
                    reader,
                    scope,
                    (TypeSpecificationHandle)handle,
                    0),
            _ => TypeRef.Unsupported(
                $"interface implementation handle kind {handle.Kind}"),
        };

    static MemberRef InstantiateMember(
        MemberRef member,
        ImmutableArray<TypeRef> typeArguments)
    {
        TypeRef declaring = member.DeclaringType.Instantiate(
            typeArguments,
            []);
        ImmutableArray<TypeRef> declaringArguments =
            declaring.Kind == TypeRefKind.GenericInstance
                ? declaring.TypeArguments
                : typeArguments;
        return member with
        {
            DeclaringType = declaring,
            ParameterTypes =
            [
                .. member.OpenSignatureParameters.Select(parameter =>
                    parameter.Instantiate(declaringArguments, [])),
            ],
            ReturnType = member.OpenSignatureReturn.Instantiate(
                declaringArguments,
                []),
        };
    }

    static MemberRef Marker(TypeRef declaringType) =>
        new(
            declaringType,
            "$interface",
            [],
            declaringType,
            MemberKind.Method)
        {
            HasThis = true,
            SignatureHeader = HasThis,
            RequiredParameterCount = 0,
        };

    static bool IsImplicitCandidate(
        MethodAttributes attributes,
        MemberRef member) =>
        member.Kind == MemberKind.Method
        && member.HasThis
        && (attributes & MethodAttributes.MemberAccessMask)
            == MethodAttributes.Public
        && (attributes & MethodAttributes.Virtual) != 0
        && (attributes & MethodAttributes.Static) == 0;

    static bool SlotShapeMatches(
        CatalogMemberJoinKey slot,
        CatalogMemberJoinKey candidate,
        bool requireName) =>
        (!requireName
            || string.Equals(
                slot.Name,
                candidate.Name,
                StringComparison.Ordinal))
        && slot.MemberKind == candidate.MemberKind
        && slot.GenericArity == candidate.GenericArity
        && slot.HasThis == candidate.HasThis
        && slot.SignatureHeader == candidate.SignatureHeader
        && slot.RequiredParameterCount
            == candidate.RequiredParameterCount
        && slot.ParameterTypes.SequenceEqual(candidate.ParameterTypes)
        && slot.ReturnType == candidate.ReturnType;

    static bool GenericOwnerFramesMatch(
        DirectCallDefinitionResolution.Resolved interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall,
        CatalogTypeShape interfaceType)
    {
        if (!ContainsGenericParameter(interfaceType))
            return true;
        return Equals(
            interfaceCall.GenericScopes,
            implementationCall.GenericScopes);
    }

    static bool ContainsGenericParameter(CatalogTypeShape type)
    {
        if (type.Kind
            is CatalogTypeShapeKind.GenericParameter
                or CatalogTypeShapeKind.MethodGenericParameter)
        {
            return true;
        }
        if (type.ElementType is not null
            && ContainsGenericParameter(type.ElementType))
        {
            return true;
        }
        return type.Components.Any(ContainsGenericParameter);
    }

    static bool CouldNameType(TypeRef candidate, TypeRef expected)
    {
        TypeRef candidateDefinition =
            candidate.Kind == TypeRefKind.GenericInstance
                ? candidate.ElementType!
                : candidate;
        TypeRef expectedDefinition =
            expected.Kind == TypeRefKind.GenericInstance
                ? expected.ElementType!
                : expected;
        return candidateDefinition.Kind == TypeRefKind.Unsupported
            || expectedDefinition.Kind == TypeRefKind.Unsupported
            || string.Equals(
                candidateDefinition.Namespace,
                expectedDefinition.Namespace,
                StringComparison.Ordinal)
            && string.Equals(
                candidateDefinition.Name,
                expectedDefinition.Name,
                StringComparison.Ordinal);
    }

    static ResolutionDisposition Classify(
        CatalogMemberJoinProjection projection)
    {
        var incomplete =
            (CatalogMemberJoinProjection.Incomplete)projection;
        ResolutionDisposition disposition =
            ResolutionDisposition.Incomplete;
        foreach (MemberCorrespondenceFailure failure
            in incomplete.Failures)
        {
            if (failure is MemberCorrespondenceFailure.Resolution
                    {
                        Outcome: TypeResolutionOutcome.Ambiguous
                    })
            {
                disposition = Stronger(
                    disposition,
                    ResolutionDisposition.Ambiguous);
            }
            else if (failure is
                MemberCorrespondenceFailure.UnsupportedTypeShape
                    or MemberCorrespondenceFailure.MalformedTypeShape
                    or MemberCorrespondenceFailure
                        .InvalidRequiredParameterCount
                    or MemberCorrespondenceFailure.SourceMismatch
                    or MemberCorrespondenceFailure
                        .OpenSignatureUnavailable)
            {
                disposition = Stronger(
                    disposition,
                    ResolutionDisposition.Unsupported);
            }
            else if (failure is MemberCorrespondenceFailure.Resolution
                    {
                        Outcome: TypeResolutionOutcome.Rejected rejected
                    }
                && DirectCallDefinitionResolver
                    .ClassifyRejectedTypeResolution(rejected.Failure)
                    == DirectCallTypeResolutionKind.Unsupported)
            {
                disposition = Stronger(
                    disposition,
                    ResolutionDisposition.Unsupported);
            }
        }
        return disposition;
    }

    static string ProjectionDetail(
        CatalogMemberJoinProjection projection) =>
        projection is CatalogMemberJoinProjection.Incomplete incomplete
            ? string.Join(
                ", ",
                incomplete.Failures.Select(
                    failure => failure.GetType().Name))
            : projection.GetType().Name;

    static ResolutionDisposition Stronger(
        ResolutionDisposition left,
        ResolutionDisposition right) =>
        (ResolutionDisposition)Math.Max((int)left, (int)right);

    static ResourceEffectInterfaceApplicationGap WorkGap(
        ResourceEffectInterfaceApplicationWorkDimension dimension,
        long limit,
        long requiredWork) =>
        new(
            ResourceEffectInterfaceApplicationGapKind.WorkLimitExceeded)
        {
            WorkDimension = dimension,
            Limit = limit,
            RequiredWork = requiredWork,
        };

    readonly record struct ConcreteTypeKey(
        AssemblyAcquisitionRegistration Registration,
        Guid ModuleVersionId,
        int MethodToken,
        TypeRef DeclaringType)
    {
        internal static ConcreteTypeKey For(
            DirectCallDefinitionResolution.Resolved call) =>
            new(
                call.Definition.Registration,
                call.Definition.ModuleVersionId,
                call.Definition.MetadataToken,
                call.Call.Callee.DeclaringType);
    }

    readonly record struct ApplicationPairKey(
        GraphNodeStorageKey InterfaceCall,
        GraphNodeStorageKey ImplementationCall);

    sealed record PendingInterfaceImplementation(
        int RowToken,
        TypeRef ClosedInterfaceType,
        CatalogMemberCorrespondencePlan TypePlan);

    sealed record PendingMethodImplementation(
        int RowToken,
        int BodyToken,
        MemberRef Declaration,
        CatalogMemberCorrespondencePlan DeclarationPlan);

    sealed record PendingMethod(
        int Token,
        MemberRef Member,
        CatalogMemberCorrespondencePlan Plan,
        bool IsImplicitCandidate);

    sealed record PendingConcreteType(
        ResolvedAssemblyReference Assembly,
        Guid ModuleVersionId,
        int TypeToken,
        ImmutableArray<PendingInterfaceImplementation> Interfaces,
        ImmutableArray<PendingMethodImplementation> MethodImplementations,
        ImmutableDictionary<int, PendingMethod> Methods,
        ImmutableArray<TypeResolutionRequest> Requests,
        ResourceEffectInterfaceApplicationGap? GlobalGap)
    {
        internal static PendingConcreteType Unsupported(
            ResolvedAssemblyReference assembly) =>
            Failed(
                assembly,
                new(
                    ResourceEffectInterfaceApplicationGapKind
                        .UnsupportedMetadata)
                {
                    Detail = "The concrete implementation type metadata was unsupported.",
                });

        internal static PendingConcreteType Incomplete(
            ResolvedAssemblyReference assembly) =>
            Failed(
                assembly,
                new(
                    ResourceEffectInterfaceApplicationGapKind
                        .IncompleteMetadata)
                {
                    Detail = "The concrete implementation type metadata was incomplete.",
                });

        internal static PendingConcreteType Limit(
            ResolvedAssemblyReference assembly,
            ResourceEffectInterfaceApplicationWorkDimension dimension,
            long limit,
            long requiredWork) =>
            Failed(
                assembly,
                WorkGap(dimension, limit, requiredWork));

        static PendingConcreteType Failed(
            ResolvedAssemblyReference assembly,
            ResourceEffectInterfaceApplicationGap gap) =>
            new(
                assembly,
                Guid.Empty,
                0,
                [],
                [],
                ImmutableDictionary<int, PendingMethod>.Empty,
                [],
                gap);
    }

    sealed class PlanningWork
    {
        internal long InterfaceImplementations;
        internal long MethodImplementations;
        internal long CandidateMethods;
        internal long SignatureNodes;

        internal bool Charge(TypeRef type, long maximum)
        {
            var pending = new Stack<TypeRef>();
            pending.Push(type);
            while (pending.TryPop(out TypeRef? current))
            {
                SignatureNodes++;
                if (SignatureNodes > maximum)
                    return false;
                if (current.ElementType is not null)
                    pending.Push(current.ElementType);
                foreach (TypeRef argument in current.TypeArguments)
                    pending.Push(argument);
                if (current.ModifierType is not null)
                    pending.Push(current.ModifierType);
                if (current.UnmodifiedType is not null)
                    pending.Push(current.UnmodifiedType);
                if (current.FunctionPointerSignature is { } signature)
                {
                    pending.Push(signature.ReturnType);
                    foreach (TypeRef parameter
                        in signature.ParameterTypes)
                    {
                        pending.Push(parameter);
                    }
                }
            }
            return true;
        }

        internal bool Charge(MemberRef member, long maximum)
        {
            if (!Charge(member.DeclaringType, maximum)
                || !Charge(member.OpenSignatureReturn, maximum))
            {
                return false;
            }
            foreach (TypeRef parameter
                in member.OpenSignatureParameters)
            {
                if (!Charge(parameter, maximum))
                    return false;
            }
            foreach (TypeRef argument in member.TypeArguments)
            {
                if (!Charge(argument, maximum))
                    return false;
            }
            return true;
        }
    }

    enum ResolutionDisposition
    {
        None,
        Incomplete,
        Unsupported,
        Ambiguous,
    }
}

internal sealed class ResourceEffectInterfaceApplicationExtension(
    ResourceEffectAdmission admission,
    ResourceEffectInterfaceApplicationLimits limits)
    : IDirectCallDefinitionGenerationExtension
{
    ResourceEffectInterfaceApplicationPlan? _plan;

    internal ResourceEffectInterfaceApplicationIndex? Index { get; private set; }

    public IEnumerable<TypeResolutionRequest> Plan(
        DirectCallDefinitionResolutionOutcome.Completed provisional,
        CancellationToken cancellationToken)
    {
        _plan = ResourceEffectInterfaceApplicationPlan.Create(
            provisional,
            admission,
            limits,
            cancellationToken);
        return _plan.Requests;
    }

    public void Complete(
        TypeResolutionContext context,
        DirectCallDefinitionResolutionOutcome.Completed completed,
        CancellationToken cancellationToken)
    {
        if (_plan is null)
            throw new InvalidOperationException(
                "Interface application planning did not run.");
        Index = _plan.Resolve(context, completed, cancellationToken);
    }
}
