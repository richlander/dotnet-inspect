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
    SelectorBindings,
    InterfaceMethods,
    MetadataAssociations,
    SlotComparisons,
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
        int maxRetainedApplications = 100_000,
        int maxSelectorBindings = 100_000,
        int maxInterfaceMethods = MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        int maxMetadataAssociations = MethodSemanticsReadBudget.DefaultMaximumRetainedAssociations,
        int maxSlotComparisons = 100_000)
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSelectorBindings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInterfaceMethods);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataAssociations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSlotComparisons);

        MaxCandidateApplications = maxCandidateApplications;
        MaxInterfaceImplementations = maxInterfaceImplementations;
        MaxMethodImplementations = maxMethodImplementations;
        MaxCandidateMethods = maxCandidateMethods;
        MaxSignatureNodes = maxSignatureNodes;
        MaxRetainedApplications = maxRetainedApplications;
        MaxSelectorBindings = maxSelectorBindings;
        MaxInterfaceMethods = maxInterfaceMethods;
        MaxMetadataAssociations = maxMetadataAssociations;
        MaxSlotComparisons = maxSlotComparisons;
    }

    public int MaxCandidateApplications { get; }
    public int MaxInterfaceImplementations { get; }
    public int MaxMethodImplementations { get; }
    public int MaxCandidateMethods { get; }
    public int MaxSignatureNodes { get; }
    public int MaxRetainedApplications { get; }
    public int MaxSelectorBindings { get; }
    public int MaxInterfaceMethods { get; }
    public int MaxMetadataAssociations { get; }
    public int MaxSlotComparisons { get; }
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

public sealed class ResourceEffectClosedInterfaceSlot
{
    internal ResourceEffectClosedInterfaceSlot(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        MemberRef member,
        ClosedSlotKey slot)
    {
        Catalog = catalog;
        Generation = generation;
        Member = member;
        DeclaringType = slot.DeclaringType;
        ParameterTypes = slot.ParameterTypes;
        ReturnType = slot.ReturnType;
        DeclaringGenericScopes = slot.DeclaringScopes;
        ParameterGenericScopes = slot.ParameterScopes;
        ReturnGenericScopes = slot.ReturnScopes;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public MemberRef Member { get; }
    public CatalogTypeShape DeclaringType { get; }
    public ImmutableArray<CatalogTypeShape> ParameterTypes { get; }
    public CatalogTypeShape ReturnType { get; }
    // Generic leaves in structural preorder. Null is a symbolic slot variable;
    // a scope identifies a variable introduced by the concrete invocation.
    public ImmutableArray<ResolvedResourceEffectGenericScope?> DeclaringGenericScopes { get; }
    public ImmutableArray<ResolvedResourceEffectGenericScope?> ParameterGenericScopes { get; }
    public ImmutableArray<ResolvedResourceEffectGenericScope?> ReturnGenericScopes { get; }
}

public sealed class ResourceEffectInterfaceApplicationEvidence
{
    internal ResourceEffectInterfaceApplicationEvidence(
        DirectCallDefinitionOccurrence interfaceDeclaration,
        DirectCallDefinitionOccurrence implementation,
        ResourceEffectInterfaceImplementationEvidence interfacePath,
        ResourceEffectMethodImplementationEvidence method,
        ResourceEffectClosedInterfaceSlot closedSlot)
    {
        InterfaceDeclaration = interfaceDeclaration;
        Implementation = implementation;
        InterfacePath = interfacePath;
        Method = method;
        ClosedSlot = closedSlot;
    }

    public DirectCallDefinitionOccurrence InterfaceDeclaration { get; }
    public DirectCallDefinitionOccurrence Implementation { get; }
    public ResourceEffectInterfaceImplementationEvidence InterfacePath
        { get; }
    public ResourceEffectMethodImplementationEvidence Method { get; }
    public ResourceEffectClosedInterfaceSlot ClosedSlot { get; }
}

public abstract class ResourceEffectInterfaceApplication
{
    private protected ResourceEffectInterfaceApplication(
        DirectCallDefinitionResolution.Resolved? interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall)
    {
        SelectorOccurrence = interfaceCall;
        ImplementationCall = implementationCall;
    }

    internal DirectCallDefinitionResolution.Resolved? SelectorOccurrence { get; }
    public DirectCallDefinitionOccurrence? InterfaceDeclaration =>
        SelectorOccurrence?.Definition;
    public DirectCallDefinitionResolution.Resolved ImplementationCall
        { get; }

    public sealed class Applied : ResourceEffectInterfaceApplication
    {
        internal Applied(
            DirectCallDefinitionResolution.Resolved? interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            DirectCallDefinitionResolution.Resolved occurrenceBindingCall,
            ResourceEffectInterfaceApplicationEvidence evidence)
            : base(interfaceCall, implementationCall) =>
            (OccurrenceBindingCall, Evidence) =
                (occurrenceBindingCall, evidence);

        internal DirectCallDefinitionResolution.Resolved
            OccurrenceBindingCall { get; }
        public ResourceEffectInterfaceApplicationEvidence Evidence { get; }
    }

    public sealed class NotApplicable : ResourceEffectInterfaceApplication
    {
        internal NotApplicable(
            DirectCallDefinitionResolution.Resolved? interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall)
            : base(interfaceCall, implementationCall)
        {
        }
    }

    public sealed class Ambiguous : ResourceEffectInterfaceApplication
    {
        internal Ambiguous(
            DirectCallDefinitionResolution.Resolved? interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            ResourceEffectInterfaceApplicationGap gap)
            : base(interfaceCall, implementationCall) =>
            Gap = gap;

        public ResourceEffectInterfaceApplicationGap Gap { get; }
    }

    public sealed class Unsupported : ResourceEffectInterfaceApplication
    {
        internal Unsupported(
            DirectCallDefinitionResolution.Resolved? interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            ResourceEffectInterfaceApplicationGap gap)
            : base(interfaceCall, implementationCall) =>
            Gap = gap;

        public ResourceEffectInterfaceApplicationGap Gap { get; }
    }

    public sealed class Incomplete : ResourceEffectInterfaceApplication
    {
        internal Incomplete(
            DirectCallDefinitionResolution.Resolved? interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            ResourceEffectInterfaceApplicationGap gap)
            : base(interfaceCall, implementationCall) =>
            Gap = gap;

        public ResourceEffectInterfaceApplicationGap Gap { get; }
    }
}

public sealed class ResourceEffectInterfaceApplicationIndex
{
    readonly ImmutableDictionary<AdmittedResourceEffectDeclaration,
        ImmutableArray<ResourceEffectInterfaceApplication>> _applications;
    readonly ImmutableHashSet<AdmittedResourceEffectDeclaration>
        _globalGapDeclarations;

    internal ResourceEffectInterfaceApplicationIndex(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        ResourceEffectAdmissionReceipt admissionReceipt,
        ResourceEffectOccurrencePopulationReceipt populationReceipt,
        ImmutableDictionary<AdmittedResourceEffectDeclaration,
            ImmutableArray<ResourceEffectInterfaceApplication>> applications,
        ResourceEffectInterfaceApplicationGap? globalGap,
        ResourceEffectInterfaceApplicationGap? coverageGap = null,
        ImmutableHashSet<AdmittedResourceEffectDeclaration>?
            globalGapDeclarations = null)
    {
        Catalog = catalog;
        Generation = generation;
        AdmissionReceipt = admissionReceipt;
        PopulationReceipt = populationReceipt;
        _applications = applications;
        GlobalGap = globalGap;
        CoverageGap = coverageGap;
        _globalGapDeclarations = globalGapDeclarations
            ?? (globalGap is null
                ? ImmutableHashSet.Create<
                    AdmittedResourceEffectDeclaration>(
                        ReferenceEqualityComparer.Instance)
                : applications.Keys.ToImmutableHashSet<
                    AdmittedResourceEffectDeclaration>(
                    ReferenceEqualityComparer.Instance));
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public ResourceEffectAdmissionReceipt AdmissionReceipt { get; }
    public ResourceEffectOccurrencePopulationReceipt PopulationReceipt { get; }
    public ImmutableArray<ResourceEffectInterfaceApplication> Applications =>
        [.. _applications.Values.SelectMany(value => value)];
    public ResourceEffectInterfaceApplicationGap? GlobalGap { get; }
    public ResourceEffectInterfaceApplicationGap? CoverageGap { get; }

    public ImmutableArray<ResourceEffectInterfaceApplication> For(
        AdmittedResourceEffectDeclaration declaration)
        {
        ArgumentNullException.ThrowIfNull(declaration);
        return _applications.TryGetValue(declaration, out var applications)
            ? applications : [];
    }

    internal ResourceEffectInterfaceApplicationGap? GlobalGapFor(
        AdmittedResourceEffectDeclaration declaration) =>
        GlobalGap is not null
            && _globalGapDeclarations.Contains(declaration)
                ? GlobalGap
                : null;

    internal static ResourceEffectInterfaceApplicationIndex Incomplete(
        ResourceEffectAdmission admission,
        DirectCallDefinitionResolutionOutcome.Completed calls,
        ResourceEffectInterfaceApplicationGap gap) =>
        new(
            calls.Catalog,
            calls.Generation,
            admission.Receipt,
            ResourceEffectResolver.CreatePopulationReceipt(calls),
            ImmutableDictionary.Create<
                AdmittedResourceEffectDeclaration,
                ImmutableArray<ResourceEffectInterfaceApplication>>(
                    ReferenceEqualityComparer.Instance),
            globalGap: null,
            coverageGap: gap);
}

internal sealed partial class ResourceEffectInterfaceApplicationPlan
{
    const byte HasThis = 0x20;

    readonly ImmutableDictionary<
        ConcreteTypeKey,
        PendingConcreteType> _concreteTypes;
    readonly ImmutableArray<TypeResolutionRequest> _requests;
    readonly ResourceEffectInterfaceApplicationLimits _limits;
    readonly ResourceEffectAdmission _admission;
    readonly PlanningWork _work;
    readonly ImmutableArray<PendingConcreteType> _orderedConcreteTypes;
    readonly ResourceEffectInterfaceApplicationGap? _coverageGap;
    ResourceEffectInterfaceApplicationGap? _globalGap;

    ResourceEffectInterfaceApplicationPlan(
        ImmutableDictionary<ConcreteTypeKey, PendingConcreteType>
            concreteTypes,
        ImmutableArray<PendingConcreteType> orderedConcreteTypes,
        ImmutableArray<TypeResolutionRequest> requests,
        ResourceEffectInterfaceApplicationLimits limits,
        ResourceEffectAdmission admission,
        PlanningWork work,
        ResourceEffectInterfaceApplicationGap? coverageGap,
        ResourceEffectInterfaceApplicationGap? globalGap)
    {
        _concreteTypes = concreteTypes;
        _orderedConcreteTypes = orderedConcreteTypes;
        _requests = requests;
        _limits = limits;
        _admission = admission;
        _work = work;
        _coverageGap = coverageGap;
        _globalGap = globalGap;
    }

    internal ImmutableArray<TypeResolutionRequest> Requests => _requests;

    internal static ResourceEffectInterfaceApplicationPlan Create(
        DirectCallDefinitionResolutionOutcome.Completed calls,
        ResourceEffectAdmission admission,
        ResourceEffectInterfaceApplicationLimits limits,
        CancellationToken cancellationToken)
    {
        var implementations = calls.Results
            .OfType<DirectCallDefinitionResolution.Resolved>()
            .Where(call => !call.Definition.IsInterfaceDefinition)
            .OrderBy(ResourceEffectResolver.OccurrenceKey, StringComparer.Ordinal)
            .ToImmutableArray();
        ResourceEffectInterfaceApplicationGap? coverageGap =
            calls.Results.Any(result =>
                result is not DirectCallDefinitionResolution.Resolved)
                ? new(
                    ResourceEffectInterfaceApplicationGapKind
                        .IncompleteMetadata)
                {
                    Detail =
                        "At least one direct-call occurrence could not be "
                        + "resolved, so interface-application coverage is "
                        + "incomplete.",
        }
                : null;
        ResourceEffectInterfaceApplicationGap? globalGap = null;

        var concreteTypes =
            ImmutableDictionary.CreateBuilder<
                ConcreteTypeKey,
                PendingConcreteType>();
        var requests = new List<TypeResolutionRequest>();
        var orderedConcreteTypes = ImmutableArray.CreateBuilder<PendingConcreteType>();
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
            orderedConcreteTypes.Add(pending);
            requests.AddRange(pending.Requests);
            if (pending.GlobalGap?.Kind
                == ResourceEffectInterfaceApplicationGapKind.WorkLimitExceeded)
            {
                globalGap = pending.GlobalGap;
                break;
            }
        }

        return new ResourceEffectInterfaceApplicationPlan(
            concreteTypes.ToImmutable(),
            orderedConcreteTypes.ToImmutable(),
            [
                .. requests.Distinct(
                    TypeResolutionRequestComparer.Instance),
            ],
            limits,
            admission,
            work,
            coverageGap,
            globalGap);
    }

    ResourceEffectInterfaceApplication ResolvePair(
        TypeResolutionContext context,
        DirectCallDefinitionResolution.Resolved interfaceCall,
        DirectCallDefinitionResolution.Resolved implementationCall,
        PendingConcreteType concreteType,
        ClosedSlotKey slot,
        MemberRef closedSlot)
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
            slot.DeclaringType;
        var matchedInterfaces =
            new List<PendingInterfaceImplementation>();
        ResolutionDisposition interfaceFailure =
            ResolutionDisposition.None;
        string? interfaceFailureDetail = null;
        foreach (PendingInterfaceImplementation candidate
            in concreteType.Interfaces)
        {
            if (!ChargeComparison(Marker(candidate.ClosedInterfaceType)))
                return new ResourceEffectInterfaceApplication.Incomplete(
                    interfaceCall, implementationCall, _globalGap!);
            ClosedSlotProjection projection =
                candidate.ClosedPlan.Project(context);
            if (projection is not ClosedSlotProjection.Issued issued)
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
            if (issued.Key.DeclaringType != interfaceDeclaringType
                || !issued.Key.DeclaringScopes.SequenceEqual(slot.DeclaringScopes))
            {
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
            if (!ChargeComparison(candidate.Declaration))
                return new ResourceEffectInterfaceApplication.Incomplete(
                    interfaceCall, implementationCall, _globalGap!);
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
            ClosedSlotProjection projection;
            if (candidate.DeclarationDefinitionToken != 0
                && ReferenceEquals(concreteType.Assembly.Registration,
                    interfaceCall.Definition.Registration)
                && candidate.DeclarationDefinitionToken
                    == interfaceCall.Definition.MetadataToken)
            {
                MemberRef declaration = InstantiateMember(
                    candidate.Declaration, interfacePath.ClosedInterfaceType.TypeArguments)
                    with
                { DeclaringType = interfacePath.ClosedInterfaceType };
                projection = new ResourceEffectClosedSlotPlan(
                    concreteType.Assembly, declaration, concreteType.Origins,
                    concreteType.GenericScopes).Project(context);
            }
            else
            {
                projection = candidate.DeclarationPlan.Project(context);
            }
            if (projection is not ClosedSlotProjection.Issued issued)
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
            if (issued.Key.DeclaringType == slot.DeclaringType
                && issued.Key.DeclaringScopes.SequenceEqual(slot.DeclaringScopes)
                && SlotShapeMatches(slot, issued.Key, requireName: true))
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
                    out PendingMethod? explicitBody)
                || explicitBody.Unsupported)
            {
                return new ResourceEffectInterfaceApplication.Unsupported(
                    interfaceCall,
                    implementationCall,
                    new(
                        ResourceEffectInterfaceApplicationGapKind
                            .UnsupportedMetadata));
            }
            ClosedSlotProjection bodyProjection =
                explicitBody.Plan.Project(context);
            if (bodyProjection is not ClosedSlotProjection.Issued
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
                    slot,
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
                closedSlot,
                slot,
                new ResourceEffectMethodImplementationEvidence.Explicit(
                    explicitMatch.RowToken,
                    explicitMatch.BodyToken,
                    explicitMatch.Declaration));
        }

        var implicitMatches = new List<PendingMethod>();
        ResolutionDisposition implicitFailure =
            ResolutionDisposition.None;
        foreach (PendingMethod candidate in concreteType.Methods.Values.OrderBy(method => method.Token))
        {
            if (!ChargeComparison(candidate.Member))
                return new ResourceEffectInterfaceApplication.Incomplete(
                    interfaceCall, implementationCall, _globalGap!);
            if (!string.Equals(
                    candidate.MetadataName,
                    interfaceCall.Definition.Correspondence.Name,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (!candidate.IsImplicitCandidate)
                continue;
            if (candidate.Unsupported)
            {
                implicitFailure = Stronger(implicitFailure, ResolutionDisposition.Unsupported);
                continue;
            }
            ClosedSlotProjection projection =
                candidate.Plan.Project(context);
            if (projection is not ClosedSlotProjection.Issued issued)
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
                    slot,
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
            closedSlot,
            slot,
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
        MemberRef closedSlot,
        ClosedSlotKey slot,
        ResourceEffectMethodImplementationEvidence method) =>
        new ResourceEffectInterfaceApplication.Applied(
            interfaceCall,
            implementationCall,
            CreateOccurrenceBindingCall(
                context,
                interfaceCall,
                implementationCall,
                concreteType),
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
                method,
                new ResourceEffectClosedInterfaceSlot(
                    context.Catalog, context.Generation, closedSlot, slot)));

    static DirectCallDefinitionResolution.Resolved
        CreateOccurrenceBindingCall(
            TypeResolutionContext context,
            DirectCallDefinitionResolution.Resolved interfaceCall,
            DirectCallDefinitionResolution.Resolved implementationCall,
            PendingConcreteType concreteType)
    {
        MemberRef open = implementationCall.Definition.Member;
        MemberRef original = implementationCall.Call.Callee;
        ImmutableArray<TypeRef> typeArguments =
            original.DeclaringType.Kind == TypeRefKind.GenericInstance
                ? original.DeclaringType.TypeArguments
                : [];
        ImmutableArray<TypeRef> methodArguments =
            original.TypeArguments;
        MemberRef normalized = original with
        {
            ParameterTypes =
            [
                .. open.OpenSignatureParameters.Select(parameter =>
                    parameter.Instantiate(
                        typeArguments,
                        methodArguments)),
            ],
            ReturnType = open.OpenSignatureReturn.Instantiate(
                typeArguments,
                methodArguments),
        };
        var origins = new Dictionary<TypeRef, ResolvedAssemblyReference>(
            concreteType.Origins,
            ReferenceEqualityComparer.Instance);
        AddOrigins(
            original.DeclaringType,
            implementationCall.Participant.Assembly,
            origins);
        foreach (TypeRef argument in methodArguments)
        {
            AddOrigins(
                argument,
                implementationCall.Participant.Assembly,
                origins);
        }
        DirectCallTypeResolutionSnapshot resolutions =
            DirectCallDefinitionResolver.CreateTypeResolutionSnapshot(
                context,
                implementationCall.Definition.AssemblyReference,
                normalized,
                origins)
            .With(interfaceCall.TypeResolutions);
        return new DirectCallDefinitionResolution.Resolved(
            implementationCall.Catalog,
            implementationCall.Generation,
            implementationCall.Participant,
            implementationCall.Call with { Callee = normalized },
            implementationCall.Definition,
            implementationCall.GenericScopes,
            resolutions);
    }

    static ResourceEffectInterfaceApplication Failure(
        DirectCallDefinitionResolution.Resolved? interfaceCall,
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
            var origins = new Dictionary<TypeRef, ResolvedAssemblyReference>(
                ReferenceEqualityComparer.Instance);
            foreach (TypeRef argument in typeArguments)
                AddOrigins(argument, implementation.Participant.Assembly, origins);

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
                        Marker(GenericMemberIdentity.OpenDeclaringType(closedInterface)),
                        requiresOpenSignature: false);
                requests.AddRange(plan.Requests);
                var closedPlan = new ResourceEffectClosedSlotPlan(
                    assembly, marker, origins, implementation.GenericScopes);
                requests.AddRange(closedPlan.Requests);
                interfaces.Add(new(
                    MetadataTokens.GetToken(handle),
                    closedInterface,
                    plan,
                    closedPlan));
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
                var plan = new ResourceEffectClosedSlotPlan(
                    assembly, member, origins, implementation.GenericScopes);
                requests.AddRange(plan.Requests);
                MethodAttributes attributes = method.Attributes;
                methods.Add(
                    MetadataTokens.GetToken(handle),
                    new PendingMethod(
                        MetadataTokens.GetToken(handle),
                        member,
                        plan,
                        reader.GetString(method.Name),
                        IsImplicitCandidate(attributes),
                        member.Kind == MemberKind.Unsupported
                            || !MemberResolver.HasExactGenericParameters(
                                reader, method.GetGenericParameters(), member.GenericArity)));
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
                var plan = new ResourceEffectClosedSlotPlan(
                    assembly, declaration, origins, implementation.GenericScopes);
                requests.AddRange(plan.Requests);
                methodImplementations.Add(new(
                    MetadataTokens.GetToken(handle),
                    row.MethodBody.Kind
                        == HandleKind.MethodDefinition
                            ? MetadataTokens.GetToken(row.MethodBody)
                            : 0,
                    declaration,
                    plan,
                    row.MethodDeclaration.Kind == HandleKind.MethodDefinition
                        ? MetadataTokens.GetToken(row.MethodDeclaration) : 0));
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
                GlobalGap: null,
                origins,
                implementation.GenericScopes);
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

    static bool IsImplicitCandidate(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask)
            == MethodAttributes.Public
        && (attributes & MethodAttributes.Virtual) != 0
        && (attributes & MethodAttributes.Static) == 0;

    static bool SlotShapeMatches(
        ClosedSlotKey slot,
        ClosedSlotKey candidate,
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
        && slot.ReturnType == candidate.ReturnType
        && slot.ParameterScopes.SequenceEqual(candidate.ParameterScopes)
        && slot.ReturnScopes.SequenceEqual(candidate.ReturnScopes);

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
        ClosedSlotProjection projection)
    {
        var incomplete =
            (ClosedSlotProjection.Incomplete)projection;
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
        ClosedSlotProjection projection) =>
        projection is ClosedSlotProjection.Incomplete incomplete
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
        GraphNodeStorageKey PhysicalInvocation)
    {
        internal static ConcreteTypeKey For(
            DirectCallDefinitionResolution.Resolved call) =>
            new(call.PhysicalInvocation);
    }

    sealed record PendingInterfaceImplementation(
        int RowToken,
        TypeRef ClosedInterfaceType,
        CatalogMemberCorrespondencePlan TypePlan,
        ResourceEffectClosedSlotPlan ClosedPlan);

    sealed record PendingMethodImplementation(
        int RowToken,
        int BodyToken,
        MemberRef Declaration,
        ResourceEffectClosedSlotPlan DeclarationPlan,
        int DeclarationDefinitionToken);

    sealed record PendingMethod(
        int Token,
        MemberRef Member,
        ResourceEffectClosedSlotPlan Plan,
        string MetadataName,
        bool IsImplicitCandidate,
        bool Unsupported);

    sealed record PendingConcreteType(
        ResolvedAssemblyReference Assembly,
        Guid ModuleVersionId,
        int TypeToken,
        ImmutableArray<PendingInterfaceImplementation> Interfaces,
        ImmutableArray<PendingMethodImplementation> MethodImplementations,
        ImmutableDictionary<int, PendingMethod> Methods,
        ImmutableArray<TypeResolutionRequest> Requests,
        ResourceEffectInterfaceApplicationGap? GlobalGap,
        Dictionary<TypeRef, ResolvedAssemblyReference> Origins,
        DirectCallGenericScopeOwners? GenericScopes)
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
                gap,
                new(ReferenceEqualityComparer.Instance),
                null);
    }

    sealed class PlanningWork
    {
        internal long InterfaceImplementations;
        internal long MethodImplementations;
        internal long CandidateMethods;
        internal long SignatureNodes;
        internal long SlotComparisons;

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
                || !Charge(member.ReturnType, maximum))
            {
                return false;
            }
            foreach (TypeRef parameter
                in member.ParameterTypes)
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

    public IEnumerable<TypeResolutionRequest> PlanDefinitions(
        TypeResolutionContext context,
        CancellationToken cancellationToken) =>
        (_plan ?? throw new InvalidOperationException("Interface planning did not run."))
            .PlanDefinitions(context, cancellationToken);
}
