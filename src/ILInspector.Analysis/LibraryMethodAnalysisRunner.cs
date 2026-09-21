using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ControlFlow;
using Inspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal interface ILibraryMethodAnalysisResolver :
    IMethodAllocationResolver,
    IOptimizationOpportunityResolver
{
}

internal interface ILibraryMethodAnalysisInfrastructure
{
    MetadataReader Reader { get; }

    PEReader PeReader { get; }

    string AssemblyName { get; }

    Guid Mvid { get; }

    GenericScope CreateScope(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition);

    GenericScope CreatePresenceScope(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition,
        UnsafePresenceWorkBudget workBudget);

    MethodIdentity CreateMethodIdentity(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        GenericScope scope);

    MethodIdentity CreatePresenceMethodIdentity(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        GenericScope scope,
        UnsafePresenceWorkBudget workBudget);

    ILibraryMethodAnalysisResolver CreateMethodAnalysisResolver(
        GenericScope scope,
        MethodIdentity caller,
        MethodInstructions instructions);

    IMethodCallResolver CreateCallResolver(
        GenericScope scope,
        MethodIdentity caller);

    CallerUnsafeMode? ResolveSameImageCallerUnsafeMode(
        int operandToken,
        MemberRef member,
        UnsafePresenceWorkBudget workBudget);

    bool MayResolveSameImageCall(
        int operandToken,
        UnsafePresenceWorkBudget workBudget);

    MemberRef ResolveMethod(
        int token,
        GenericScope scope,
        MethodDefinitionHandle caller);

    MemberRef ResolvePresenceMethod(
        int token,
        GenericScope scope,
        UnsafePresenceWorkBudget workBudget);

    string? CalliReturnDetail(
        int token,
        GenericScope scope);

    bool IsAllocatingValueTypeBox(
        int token,
        GenericScope scope);

    bool HasGeneratedCodeAttribute(
        CustomAttributeHandleCollection attributes);

    bool HasCompilerGeneratedAttribute(
        CustomAttributeHandleCollection attributes);

    void ValidateAsyncSource(
        MethodIdentity method,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated);

    AsyncBodyAttribution? ResolveAsyncBody(
        MethodIdentity method,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated);

    bool IsAuthenticatedAsyncStateMachineExecutionMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition);

    ImmutableArray<OptimizationOpportunity>
        CollectAsyncSiblingOpportunities(
            MethodBodyAnalysisContext context,
            ImmutableArray<DirectCall>.Builder calls,
            MethodDefinition methodDefinition,
            bool typeSourceGenerated,
            ref MethodIdentity? asyncSource);

    bool TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out AuthenticatedSourceOwner sourceOwner,
        IReadOnlySet<int>? ownerMethodScope,
        Func<TypeRef, bool>? ownerTypeScope,
        bool directlySelectedBody);

    MethodIdentity? ResolveDeclaredMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodIdentity method,
        bool typeSourceGenerated,
        IReadOnlySet<int>? ownerMethodScope,
        Func<TypeRef, bool>? ownerTypeScope,
        IReadOnlySet<int>? requestedMethodScope,
        bool directlySelectedBody);

    DeclaredOwnerResolution ResolveUltimateDeclaredMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodIdentity method,
        bool typeSourceGenerated,
        out AuthenticatedSourceOwner? immediateOwner,
        out AuthenticatedSourceOwner? ultimateOwner);

    bool DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method);
}

internal enum DeclaredOwnerResolution
{
    None,
    Resolved,
    Unresolved,
    Rejected,
}

// Method-local output is merged by LibraryBodyAnalysisAccumulator in metadata
// order. BuildCallTree_PreservesRecoverableBodyAnalysisFailure gates the
// partial call/evidence publication and diagnostic behavior.
internal sealed class LibraryMethodAnalysisResult
{
    public bool HasCaller;
    public MethodIdentity? Caller;
    public MethodIdentity? DeclaredMethod;
    public int Token;
    public CallerUnsafeMode Mode;
    public bool IsLeverage;
    public bool HasBody;
    public ImmutableArray<UnsafeEvidence> UnsafeEvidence;
    public ImmutableArray<DirectCall> Calls;
    public ImmutableArray<StringMaterializationOccurrence>
        StringMaterializations;
    public ImmutableArray<MethodResultSink> ResultSinks;
    public ImmutableArray<FieldStoreFact> FieldStores;
    public ImmutableArray<FieldLoadFact> FieldLoads;
    public ImmutableArray<MethodReturnFlow> ReturnFlows;
    public MethodLocalThrowEvidence? LocalThrows;
    // Reachable whole-value writes or unrecognized by-ref escapes of this
    // method's current instance, consumed only by the assembly-level proof.
    public ImmutableArray<int> CurrentInstanceMutations;
    // Set before metadata/body classification; only a proven bodiless method
    // can opt out of the unscoped absence census.
    public bool RequiresCompleteFieldAccessCensus;
    // Set only after MethodCallAnalysis has collected every field access.
    public bool FieldAccessCensusComplete;
    public ImmutableArray<AllocationOccurrence> Allocations;
    public ImmutableArray<UnsafetyOccurrence> Unsafety;
    public ImmutableArray<OptimizationOpportunity> Opportunities;
    public bool Suppressed;
    public bool ScopeExcluded;
    public bool HasSignals;
    public BodySignals Signals;
    public MethodBodyImplementationMetrics? ImplementationProfile;
    public LeakTriageResult? LeakTriage;
    public ArrayPoolOwnershipMethodEvidence? OwnershipFlow;
    public AnalysisDiagnostic? Diagnostic;
    public MethodIdentity? DeclaredSource;
    public MethodBodyAnalysisContext? ResourceOccurrenceContext;
}

internal readonly record struct UnsafeEvidencePresenceMethodResult(
    bool HasEvidence,
    AnalysisDiagnostic? Diagnostic);

internal enum UnsafeCallProbeResult
{
    NoCandidate,
    RequiresResolution,
    Evidence,
    Incomplete,
}

/// <summary>
/// Runs the ordered topic producers for one method while the assembly builder
/// retains scheduling and primary-image lifetime. The primary metadata
/// resolver owns metadata-dependent judgments and adapters.
/// </summary>
internal sealed class LibraryMethodAnalysisRunner(
    ILibraryMethodAnalysisInfrastructure infrastructure,
    LibraryBodyExceptionTypeClassifier? exceptionTypes = null)
{
    readonly ILibraryMethodAnalysisInfrastructure _infrastructure =
        infrastructure;
    readonly UnsafeSignatureMarkerCache _unsafeSignatureMarkers =
        new(infrastructure.Reader);
    readonly UnsafePresenceWorkBudget _unsafePresenceWork =
        new();

    internal UnsafeEvidencePresenceMethodResult ProbeUnsafeEvidence(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle)
    {
        MetadataReader reader = _infrastructure.Reader;
        MethodIdentity? caller = null;
        try
        {
            var methodDefinition =
                reader.GetMethodDefinition(methodHandle);
            GenericScope? scope = null;
            GenericScope Scope()
                => scope ??= _infrastructure.CreatePresenceScope(
                    typeDefinition,
                    methodDefinition,
                    _unsafePresenceWork);
            MethodIdentity Caller()
                => caller ??=
                    _infrastructure.CreatePresenceMethodIdentity(
                        typeHandle,
                        methodHandle,
                        methodDefinition,
                        Scope(),
                        _unsafePresenceWork);

            bool hasUnsafeSignature =
                SignatureMayContainUnsafeType(
                    methodDefinition.Signature);
            if (hasUnsafeSignature
                && !SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    methodDefinition.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                throw new BadImageFormatException(
                    "An unsafe method signature exceeds the safe decoding limits.");
            }
            if ((MayBeUnsafeApiType(reader, typeHandle)
                    || hasUnsafeSignature)
                && MethodSafetyAnalysis.HasUnsafeDeclaration(
                    Caller()))
            {
                return new(true, null);
            }
            if (methodDefinition.RelativeVirtualAddress == 0
                || !HasManagedIlBody(
                    methodDefinition.ImplAttributes))
            {
                return new(false, null);
            }

            var body = _infrastructure.PeReader.GetMethodBody(
                methodDefinition.RelativeVirtualAddress);
            if (!body.LocalSignature.IsNil)
            {
                var localSignature =
                    reader.GetStandaloneSignature(
                        body.LocalSignature);
                UnsafeSignatureMarkers markers =
                    _unsafeSignatureMarkers.GetMarkers(
                        localSignature.Signature);
                if (markers != UnsafeSignatureMarkers.None)
                {
                    if (!SignatureBlobGuard.IsSafeToDecode(
                            reader,
                            localSignature.Signature,
                            SignatureBlobGuard.Kind
                                .LocalVariables))
                    {
                        throw new BadImageFormatException(
                            "An unsafe local signature exceeds the safe decoding limits.");
                    }
                    ImmutableArray<TypeRef> localTypes =
                        DecodeLocalTypes(body, Scope());
                    if (MethodSafetyAnalysis.HasUnsafeLocals(
                            localTypes))
                    {
                        return new(true, null);
                    }
                }
            }

            bool hasEvidence = false;
            InstructionDecoder.Visit(
                body,
                (operation, operandToken, instructionSize) =>
                {
                    _unsafePresenceWork.ReserveIlBytes(
                        instructionSize);
                    switch (operation)
                    {
                        case ILOpCode.Call:
                        case ILOpCode.Callvirt:
                        case ILOpCode.Newobj:
                        case ILOpCode.Ldftn:
                        case ILOpCode.Ldvirtftn:
                            UnsafeCallProbeResult callProbe =
                                ProbeUnsafeCall(
                                    reader,
                                    operandToken);
                            if (callProbe
                                == UnsafeCallProbeResult.Evidence)
                            {
                                hasEvidence = true;
                                return false;
                            }
                            if (callProbe
                                == UnsafeCallProbeResult.Incomplete)
                            {
                                throw new BadImageFormatException(
                                    "An unsafe call signature exceeds the safe decoding limits.");
                            }
                            if (callProbe
                                == UnsafeCallProbeResult
                                    .RequiresResolution)
                            {
                                MemberRef member =
                                    _infrastructure
                                        .ResolvePresenceMethod(
                                            operandToken,
                                            Scope(),
                                            _unsafePresenceWork);
                                CallerUnsafeMode? targetCallerUnsafeMode =
                                    operation is
                                        ILOpCode.Call
                                        or ILOpCode.Callvirt
                                        or ILOpCode.Newobj
                                            ? _infrastructure
                                                .ResolveSameImageCallerUnsafeMode(
                                                    operandToken,
                                                    member,
                                                    _unsafePresenceWork)
                                            : null;
                                if (MethodSafetyAnalysis.IsUnsafeCall(
                                        member,
                                        targetCallerUnsafeMode))
                                {
                                    hasEvidence = true;
                                    return false;
                                }
                                if (member.Kind
                                    == MemberKind.Unsupported)
                                {
                                    throw new BadImageFormatException(
                                        "An unsafe call signature could not be decoded.");
                                }
                            }
                            return true;

                        case ILOpCode.Calli:
                            hasEvidence = true;
                            return false;

                        default:
                            if (MethodSafetyAnalysis.IsUnsafeOperation(
                                operation,
                                includeIndirectOperations: false))
                            {
                                hasEvidence = true;
                                return false;
                            }
                            return true;
                    }
                }
            );

            return new(hasEvidence, null);
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return new(
                false,
                new AnalysisDiagnostic(
                    MetadataTokens.GetToken(methodHandle),
                    MethodLabel(
                        typeHandle,
                        methodHandle),
                    $"{ex.GetType().Name}: {ex.Message}",
                    DeclaringType: caller?.DeclaringType));
        }
    }

    UnsafeCallProbeResult ProbeUnsafeCall(
        MetadataReader reader,
        int token)
    {
        EntityHandle handle =
            MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MethodSpecification:
                {
                    var specification =
                        reader.GetMethodSpecification(
                            (MethodSpecificationHandle)handle);
                    UnsafeCallProbeResult target =
                        ProbeUnsafeCall(
                            reader,
                            MetadataTokens.GetToken(
                                specification.Method));
                    if (target is
                        UnsafeCallProbeResult.Evidence
                        or UnsafeCallProbeResult.Incomplete)
                    {
                        return target;
                    }

                    UnsafeCallProbeResult signature =
                        ProbeUnsafeSignature(
                            reader,
                            specification.Signature,
                            SignatureBlobGuard.Kind
                                .MethodSpecification);
                    if (signature
                        == UnsafeCallProbeResult.Incomplete)
                    {
                        return signature;
                    }
                    return target
                        == UnsafeCallProbeResult.RequiresResolution
                            ? target
                            : signature;
                }

            case HandleKind.MethodDefinition:
                {
                    var method =
                        reader.GetMethodDefinition(
                            (MethodDefinitionHandle)handle);
                    UnsafeCallProbeResult result =
                        ProbeUnsafeSignature(
                            reader,
                            method.Signature,
                            SignatureBlobGuard.Kind.Method);
                    return result
                        == UnsafeCallProbeResult.Incomplete
                            ? result
                            : UnsafeCallProbeResult.RequiresResolution;
                }

            case HandleKind.MemberReference:
                {
                    var member =
                        reader.GetMemberReference(
                            (MemberReferenceHandle)handle);
                    bool parentRequiresResolution =
                        _infrastructure
                            .MayResolveSameImageCall(
                                token,
                                _unsafePresenceWork);
                    if (MayBeUnsafeApiType(
                            reader,
                            member.Parent))
                    {
                        parentRequiresResolution = true;
                    }
                    UnsafeCallProbeResult signature =
                        ProbeUnsafeSignature(
                            reader,
                            member.Signature,
                            SignatureBlobGuard.Kind.Method);
                    if (signature
                        == UnsafeCallProbeResult.Incomplete)
                    {
                        return signature;
                    }
                    return parentRequiresResolution
                        ? UnsafeCallProbeResult.RequiresResolution
                        : signature;
                }

            default:
                return UnsafeCallProbeResult.Incomplete;
        }
    }

    UnsafeCallProbeResult ProbeUnsafeSignature(
        MetadataReader reader,
        BlobHandle signature,
        SignatureBlobGuard.Kind kind)
    {
        if (!SignatureMayContainUnsafeType(signature))
            return UnsafeCallProbeResult.NoCandidate;
        return SignatureBlobGuard.IsSafeToDecode(
            reader,
            signature,
            kind)
                ? UnsafeCallProbeResult.RequiresResolution
                : UnsafeCallProbeResult.Incomplete;
    }

    static bool MayBeUnsafeApiType(
        MetadataReader reader,
        EntityHandle handle)
    {
        StringHandle namespaceHandle;
        StringHandle nameHandle;
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                {
                    var type =
                        reader.GetTypeDefinition(
                            (TypeDefinitionHandle)handle);
                    namespaceHandle = type.Namespace;
                    nameHandle = type.Name;
                    break;
                }

            case HandleKind.TypeReference:
                {
                    var type =
                        reader.GetTypeReference(
                            (TypeReferenceHandle)handle);
                    namespaceHandle = type.Namespace;
                    nameHandle = type.Name;
                    break;
                }

            default:
                return false;
        }

        return reader.StringComparer.Equals(
                nameHandle,
                "Unsafe")
            && reader.StringComparer.Equals(
                namespaceHandle,
                "System.Runtime.CompilerServices");
    }

    bool SignatureMayContainUnsafeType(
        BlobHandle signature)
    {
        UnsafeSignatureMarkers markers =
            _unsafeSignatureMarkers.GetMarkers(signature);
        UnsafeSignatureMarkers relevant =
            UnsafeSignatureMarkers.Pointer
            | UnsafeSignatureMarkers.FunctionPointer;
        return (markers & relevant) != 0;
    }

    internal LibraryMethodAnalysisResult Analyze(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        bool typeSourceGenerated,
        MethodDefinitionHandle methodHandle,
        LibraryBodyAnalysisPlan plan)
    {
        bool includeMethodEvidence = plan.Includes(
            LibraryBodyAnalysisFeatures.MethodEvidence);
        bool includeAllocations = plan.Includes(
            LibraryBodyAnalysisFeatures.Allocations);
        bool includeOpportunities = plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);
        bool includeAsyncSiblingOpportunities = plan.Includes(
            LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities);
        bool includeImplementationProfiles = plan.Includes(
            LibraryBodyAnalysisFeatures.ImplementationProfiles);
        bool includeLeakTriage = plan.Includes(
            LibraryBodyAnalysisFeatures.LeakTriage);
        bool includeOwnershipFlow = plan.Includes(
            LibraryBodyAnalysisFeatures.OwnershipFlow);
        bool includeJsonWireContractFlow = plan.Includes(
            LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        bool includeCallValueFlow = plan.RequiresCallValueFlow;
        bool includeLocalThrows = plan.Includes(
            LibraryBodyAnalysisFeatures.LocalThrows);
        LibraryBodyExceptionTypeClassifier? localExceptionTypes =
            includeLocalThrows
                ? exceptionTypes ?? throw new InvalidOperationException(
                    "Local throws require exception-type qualification.")
                : null;
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        Func<TypeRef, bool>? bodyTypeScope = plan.TypeScope;
        IReadOnlySet<int>? requestedMethodScope =
            plan.RequestedMethodScope;
        if (!includeMethodEvidence)
        {
            return includeLeakTriage
                ? AnalyzeLeakTriageMethod(
                    typeHandle,
                    typeDefinition,
                    methodHandle)
                : new LibraryMethodAnalysisResult();
        }

        var result = new LibraryMethodAnalysisResult
        {
            RequiresCompleteFieldAccessCensus =
                includeJsonWireContractFlow
                && !plan.IsScoped,
            LocalThrows = includeLocalThrows
                ? new MethodLocalThrowEvidence.Unavailable(
                    MetadataTokens.GetToken(methodHandle),
                    LocalThrowUnavailableReason.AnalysisFailed,
                    [])
                : null,
        };
        ImmutableArray<LocalThrowSite>.Builder? localThrowSites =
            includeLocalThrows ? ImmutableArray.CreateBuilder<LocalThrowSite>() : null;
        bool isReferenceAssembly = false;
        var evidence =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var calls =
            ImmutableArray.CreateBuilder<DirectCall>();
        Dictionary<int, CallReceiverSource>?
            stringReceiverSources =
                includeOpportunities
                    ? []
                    : null;
        ImmutableArray<MethodResultSink>.Builder? resultSinks =
            includeCallValueFlow
                ? ImmutableArray.CreateBuilder<MethodResultSink>()
                : null;
        ImmutableArray<FieldStoreFact>.Builder? fieldStores =
            includeCallValueFlow
                ? ImmutableArray.CreateBuilder<FieldStoreFact>()
                : null;
        ImmutableArray<FieldLoadFact>.Builder? fieldLoads =
            includeCallValueFlow
                ? ImmutableArray.CreateBuilder<FieldLoadFact>()
                : null;
        ImmutableArray<int>.Builder? currentInstanceMutations =
            includeCallValueFlow
                ? ImmutableArray.CreateBuilder<int>()
                : null;
        ImmutableArray<MethodReturnFlow>.Builder? returnFlows =
            includeCallValueFlow
                ? ImmutableArray.CreateBuilder<MethodReturnFlow>()
                : null;
        MetadataReader reader = _infrastructure.Reader;
        LeakTriageFailureKind leakFailureKind =
            LeakTriageFailureKind.MethodMetadata;
        try
        {
            var methodDefinition =
                reader.GetMethodDefinition(methodHandle);
            result.Token = MetadataTokens.GetToken(methodHandle);
            result.HasBody =
                methodDefinition.RelativeVirtualAddress != 0
                && HasManagedIlBody(
                    methodDefinition.ImplAttributes);
            var scope = _infrastructure.CreateScope(
                typeDefinition,
                methodDefinition);
            var caller = _infrastructure.CreateMethodIdentity(
                typeHandle,
                methodHandle,
                methodDefinition,
                scope);
            result.HasCaller = true;
            result.Caller = caller;
            result.Token = caller.MetadataToken;
            if (localExceptionTypes is not null)
            {
                isReferenceAssembly = localExceptionTypes.IsReferenceAssembly;
                if (isReferenceAssembly)
                {
                    result.LocalThrows = new MethodLocalThrowEvidence.Unavailable(
                        caller.MetadataToken,
                        LocalThrowUnavailableReason.ReferenceAssembly,
                        []);
                }
            }
            // Tally the unsafe mode for every method, including bodiless
            // extern/abstract members (P/Invokes are a major source).
            result.Mode = caller.CallerUnsafeMode;
            var declarationSafety =
                MethodSafetyAnalysis.InspectDeclaration(
                    caller,
                    evidence);
            bool hasUnsafeApiMember =
                declarationSafety.HasUnsafeApiMember;
            bool hasUnsafeSignature =
                declarationSafety.HasUnsafeSignature;
            if (CallerUnsafeModeFacts.RequiresUnsafe(
                    caller.CallerUnsafeMode)
                || hasUnsafeApiMember)
            {
                result.IsLeverage = true;
            }
            if (!result.HasBody)
            {
                SetLocalThrowUnavailable(LocalThrowUnavailableReason.NoManagedBody);
                result.RequiresCompleteFieldAccessCensus =
                    false;
                if (includeAsyncSiblingOpportunities
                    && (bodyScope is null
                        || bodyScope.Contains(
                            caller.MetadataToken))
                    && (bodyTypeScope is null
                        || bodyTypeScope(
                            caller.DeclaringType)))
                {
                    _infrastructure.ValidateAsyncSource(
                        caller,
                        methodDefinition,
                        typeSourceGenerated);
                }
                return result;
            }

            // Allocation evidence can survive a later recoverable failure,
            // so classification below replaces this pessimistic state only
            // after its metadata and scope checks complete.
            result.Suppressed = includeOpportunities;
            result.ScopeExcluded =
                includeOpportunities
                && bodyTypeScope is not null;
            // Scoped builds decode only selected method bodies; every other method is still
            // indexed as an identity (above) but its body is not decoded/scanned. MethodScope
            // selects by method token; TypeScope selects by declaring type. Reverse/aggregate
            // sections leave both scopes null.
            if (bodyScope is not null
                && !bodyScope.Contains(caller.MetadataToken))
            {
                SetLocalThrowUnavailable(LocalThrowUnavailableReason.ScopeExcluded);
                return result;
            }
            bool directlySelectedType =
                bodyTypeScope?.Invoke(
                    caller.DeclaringType)
                    == true;
            bool directlySelectedMethod =
                requestedMethodScope?.Contains(
                    caller.MetadataToken)
                    == true;
            if (bodyTypeScope is not null)
            {
                ImmutableArray<TypeRef> sourceTypes = [];
                bool mappedEvidence =
                    plan.TypeScopeEvidenceSources
                        ?.TryGetValue(
                            caller.MetadataToken,
                            out sourceTypes)
                    == true;
                if (!directlySelectedType
                    && (!mappedEvidence
                        || !sourceTypes.Any(
                            bodyTypeScope)))
                {
                    SetLocalThrowUnavailable(LocalThrowUnavailableReason.ScopeExcluded);
                    return result;
                }
            }
            MethodIdentity? opportunityDeclaredMethod = null;
            MethodIdentity? unresolvedOpportunityOwner = null;
            AuthenticatedSourceOwner? immediateOwnerEvidence = null;
            AuthenticatedSourceOwner? ultimateOwnerEvidence = null;
            bool requiresDeclaredOwner =
                CompilerGeneratedNames.RequiresDeclaredOwner(
                    caller,
                    _infrastructure
                        .IsAuthenticatedAsyncStateMachineExecutionMethod(
                            methodHandle,
                            methodDefinition));
            AsyncBodyAttribution? asyncBody = null;
            bool opportunityOwnershipResolved = true;
            DeclaredOwnerResolution ownerResolution =
                DeclaredOwnerResolution.None;
            try
            {
                bool directlySelectedBody =
                    directlySelectedMethod
                    || directlySelectedType;
                MethodIdentity? declaredMethod =
                    _infrastructure.ResolveDeclaredMethod(
                        methodHandle,
                        methodDefinition,
                        caller,
                        typeSourceGenerated,
                        bodyScope,
                        bodyTypeScope,
                        requestedMethodScope,
                        directlySelectedBody);
                result.DeclaredMethod = declaredMethod;
                asyncBody =
                    _infrastructure.ResolveAsyncBody(
                        caller,
                        methodDefinition,
                        typeSourceGenerated);
                MethodIdentity? ultimateOwner =
                    declaredMethod;
                ownerResolution =
                    declaredMethod is null
                        ? DeclaredOwnerResolution.None
                        : DeclaredOwnerResolution.Resolved;
                bool needsUltimateResolution =
                    includeOpportunities
                    || declaredMethod is not null
                        && CompilerGeneratedNames
                            .IsLocalFunctionOrLambda(
                                declaredMethod.Name)
                    || includeAsyncSiblingOpportunities
                        && bodyTypeScope is not null;
                if (needsUltimateResolution)
                {
                    ownerResolution =
                        _infrastructure
                            .ResolveUltimateDeclaredMethod(
                                methodHandle,
                                methodDefinition,
                                caller,
                                typeSourceGenerated,
                                out immediateOwnerEvidence,
                                out ultimateOwnerEvidence);
                    ultimateOwner =
                        ultimateOwnerEvidence?.Method;
                }
                if (ownerResolution
                    == DeclaredOwnerResolution.Resolved)
                {
                    result.DeclaredMethod = ultimateOwner;
                    result.DeclaredSource = ultimateOwner;
                }
                else if (ownerResolution
                    is DeclaredOwnerResolution.Unresolved
                        or DeclaredOwnerResolution.Rejected)
                {
                    unresolvedOpportunityOwner =
                        ownerResolution
                            == DeclaredOwnerResolution.Unresolved
                            ? immediateOwnerEvidence?.Method
                            : null;
                    result.DeclaredMethod = null;
                }
                opportunityOwnershipResolved =
                    ownerResolution
                        is DeclaredOwnerResolution.None
                            or DeclaredOwnerResolution.Resolved;
                if (bodyTypeScope is not null)
                {
                    // Evidence admission follows the selected type, but a
                    // recommendation belongs to the ultimate declared owner.
                    opportunityDeclaredMethod =
                        ultimateOwner;
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                result.DeclaredMethod = null;
                opportunityOwnershipResolved = false;
                result.Diagnostic = new AnalysisDiagnostic(
                    MetadataTokens.GetToken(methodHandle),
                    MethodLabel(
                        typeHandle,
                        methodHandle),
                    $"{ex.GetType().Name}: {ex.Message}",
                    DeclaringType: caller.DeclaringType);
            }
            leakFailureKind =
                LeakTriageFailureKind.BodyAcquisition;
            MethodBodyData metadataBody = RequireMethodBody(
                _infrastructure.PeReader,
                caller.MetadataToken);
            var body = _infrastructure.PeReader.GetMethodBody(
                methodDefinition.RelativeVirtualAddress);
            var il = metadataBody.IL.ToArray();
            if (includeLeakTriage)
            {
                if (!SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    methodDefinition.Signature,
                    SignatureBlobGuard.Kind.Method))
                {
                    result.LeakTriage =
                        LeakTriageAnalyzer.Failed(
                            caller.MetadataToken,
                            LeakTriageFailureKind.MethodMetadata,
                            "SignatureLimit");
                }
                else
                {
                    result.LeakTriage =
                        LeakTriageAnalyzer.AnalyzeMethodDetailed(
                            LeakTriageAnalyzer
                                .CreateAssemblyScanMethodIdentity(
                                    caller),
                            metadataBody,
                            token => _infrastructure.ResolveMethod(
                                token,
                                scope,
                                methodHandle),
                            token =>
                                ResourceExceptionPathAnalyzer.ResolveCatchTypeRef(
                                    reader,
                                    MetadataTokens.EntityHandle(token),
                                    scope));
                }
            }
            LocalTypeDecodeResult localTypes =
                DecodeLocalTypesWithStatus(
                    body,
                    scope);
            MethodBodyAnalysisContext context =
                MethodBodyAnalysisContext.Create(
                caller,
                metadataBody,
                localTypes.Types,
                localTypes.DeclaredCount,
                localTypes.IncompleteReason);
            if (plan.IncludesResourceOccurrences)
                result.ResourceOccurrenceContext = context;
            MethodInstructions methodInstructions =
                context.Instructions;
            if (includeImplementationProfiles)
            {
                result.ImplementationProfile =
                    MethodImplementationProfileAnalysis.Measure(
                        context,
                        result.DeclaredMethod ?? caller,
                        il.Length,
                        asyncBody is not null);
            }
            // Build allocation's Layer-1 indexes before other topic producers,
            // then keep every result and query bound to this exact context.
            var allocationFacts =
                MethodAllocationFacts.Create(context);
            var methodAnalysisResolver =
                _infrastructure.CreateMethodAnalysisResolver(
                    scope,
                    caller,
                    methodInstructions);
            var localSafety =
                MethodSafetyAnalysis.InspectLocals(
                    context,
                    evidence);
            bool hasUnsafeLocals =
                localSafety.HasUnsafeLocals;
            // Discover allocation occurrences once. The main allocation output
            // needs escape classification, while Performance Triage's
            // optimization-opportunity pass reuses the same discovered
            // occurrences.
            if (includeAllocations)
                allocationFacts.Collect(methodAnalysisResolver);
            result.Allocations =
                allocationFacts.ClassifiedOccurrences;
            result.Unsafety =
                MethodSafetyAnalysis.CollectOccurrences(
                    context,
                    token => _infrastructure.CalliReturnDetail(
                        token,
                        scope));
            var signals = BodySignalAnalysis.Collect(
                context,
                token => _infrastructure
                    .IsAllocatingValueTypeBox(
                        token,
                        scope));
            if (signals.Newarr > 0
                || signals.Throws > 0
                || signals.Catches > 0
                || signals.Finallys > 0
                || signals.Boxes > 0)
            {
                result.Signals = signals;
                result.HasSignals = true;
            }
            bool opportunityScopeSelected =
                bodyTypeScope is null
                || opportunityDeclaredMethod is null
                || bodyTypeScope(
                    opportunityDeclaredMethod
                        .DeclaringType);
            bool collectOwnershipDerivedOpportunities =
                includeOpportunities
                && opportunityOwnershipResolved
                && opportunityScopeSelected;
            bool collectScopedAsyncSiblingOpportunities =
                includeAsyncSiblingOpportunities
                && opportunityOwnershipResolved
                && opportunityScopeSelected;
            bool collectBodyIntrinsicOpportunities =
                includeOpportunities
                && (!plan.IsScoped
                    || ((directlySelectedMethod
                            || directlySelectedType)
                        && (!requiresDeclaredOwner
                            || ownerResolution
                                    == DeclaredOwnerResolution
                                        .Unresolved))
                    || collectOwnershipDerivedOpportunities
                    || unresolvedOpportunityOwner
                            is { } unresolvedOwner
                        && (requestedMethodScope?.Contains(
                                unresolvedOwner.MetadataToken)
                                == true
                            || bodyTypeScope?.Invoke(
                                unresolvedOwner.DeclaringType)
                                == true));
            result.ScopeExcluded =
                includeOpportunities
                && !collectOwnershipDerivedOpportunities;
            try
            {
                MethodCallAnalysis.Collect(
                    context,
                    _infrastructure.CreateCallResolver(
                        scope,
                        caller),
                    offset => allocationFacts.MultiplicityAt(offset),
                    calls,
                    evidence,
                    includeIndirectOpcodes:
                        hasUnsafeApiMember
                        || hasUnsafeSignature
                        || hasUnsafeLocals,
                    includeCallValueFlow:
                        includeCallValueFlow,
                    privateReceiverSources:
                        stringReceiverSources,
                    resultSinks: resultSinks,
                    fieldStores: fieldStores,
                    fieldLoads: fieldLoads,
                    currentInstanceMutations:
                        currentInstanceMutations,
                    returnFlows: returnFlows,
                    localThrows: isReferenceAssembly ? null : localThrowSites,
                    qualifyExceptionType: localExceptionTypes is null
                        ? null : localExceptionTypes.Qualify);
                if (localThrowSites is not null && !isReferenceAssembly)
                {
                    result.LocalThrows = new MethodLocalThrowEvidence.Inspected(
                        caller, localThrowSites.ToImmutable());
                }
                result.FieldAccessCensusComplete =
                    result.RequiresCompleteFieldAccessCensus;
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                result.Diagnostic ??= new AnalysisDiagnostic(
                    MetadataTokens.GetToken(methodHandle),
                    MethodLabel(
                        typeHandle,
                        methodHandle),
                    $"{ex.GetType().Name}: {ex.Message}",
                    SourceMethodToken:
                        result.DeclaredSource?.MetadataToken,
                    DeclaringType: caller.DeclaringType,
                    SourceDeclaringType:
                        result.DeclaredSource?.DeclaringType);
            }
            if (includeOpportunities)
            {
                result.StringMaterializations =
                    StringMaterializationAnalysis.Collect(
                        calls,
                        stringReceiverSources);
            }
            if (asyncBody is not null
                && resultSinks is not null)
            {
                for (int index = 0; index < resultSinks.Count; index++)
                {
                    resultSinks[index] = resultSinks[index] with
                    {
                        AsyncBody = asyncBody,
                    };
                }
                if (fieldStores is not null
                    && fieldLoads is not null)
                {
                    MethodCallAnalysis
                        .AttachAsyncStateMachineFieldResultSources(
                            context,
                            asyncBody,
                            calls,
                            fieldStores,
                            fieldLoads,
                            currentInstanceMutations!,
                            resultSinks);
                }
            }
            if (includeOpportunities)
            {
                var methodAttributes =
                    methodDefinition.GetCustomAttributes();
                bool sourceFunction =
                    CompilerGeneratedNames.IsLocalFunctionOrLambda(
                        caller.Name);
                AuthenticatedSourceOwner sourceOwner = default;
                bool hasSourceOwner = sourceFunction
                    && _infrastructure.TryResolveLiftedSourceOwner(
                        methodHandle,
                        methodDefinition,
                        caller,
                        out sourceOwner,
                        bodyScope,
                        bodyTypeScope,
                        requestedMethodScope?.Contains(
                            caller.MetadataToken)
                            == true);
                bool sourceGenerated =
                    _infrastructure.HasGeneratedCodeAttribute(
                        methodAttributes)
                    || hasSourceOwner
                        && sourceOwner
                            .SuppressesOpportunities;
                bool ultimateSourceSuppressesOpportunities =
                    ultimateOwnerEvidence
                        ?.SuppressesOpportunities == true;
                bool compilerGenerated =
                    _infrastructure.HasCompilerGeneratedAttribute(
                        methodAttributes)
                    || sourceFunction;
                bool suppressOpportunities =
                    typeSourceGenerated
                    || sourceGenerated
                    || compilerGenerated
                    || IsBlazorRenderMethod(caller);
                result.Suppressed =
                    suppressOpportunities
                    || !collectBodyIntrinsicOpportunities;
                if (collectBodyIntrinsicOpportunities
                    && !suppressOpportunities)
                {
                    result.Opportunities =
                        OptimizationOpportunityAnalysis.Collect(
                            allocationFacts,
                            methodAnalysisResolver);
                    if (!collectOwnershipDerivedOpportunities
                        && requiresDeclaredOwner)
                    {
                        result.Opportunities =
                        [
                            .. result.Opportunities.Where(
                                static opportunity =>
                                    opportunity.Shape
                                        != "generic-parameter-object-box"),
                        ];
                    }
                }
                else if (collectOwnershipDerivedOpportunities
                    && opportunityOwnershipResolved
                    && result.DeclaredSource is { } opportunitySourceOwner
                    && !sourceGenerated
                    && !typeSourceGenerated
                    && compilerGenerated
                    && hasSourceOwner
                    && !ultimateSourceSuppressesOpportunities
                    && !IsBlazorRenderMethod(caller)
                    && !IsBlazorRenderMethod(
                        sourceOwner.Method)
                    && !IsBlazorRenderMethod(opportunitySourceOwner))
                {
                    result.Opportunities =
                    [
                        .. OptimizationOpportunityAnalysis.Collect(
                            allocationFacts,
                            methodAnalysisResolver)
                        .Where(static opportunity =>
                            opportunity.Shape
                                == "generic-parameter-object-box")
                        .Select(opportunity => opportunity with
                        {
                            SourceOwner = opportunitySourceOwner,
                        }),
                    ];
                }
            }

            if (collectScopedAsyncSiblingOpportunities
                && opportunityOwnershipResolved)
            {
                MethodIdentity? asyncSource = null;
                try
                {
                    ImmutableArray<OptimizationOpportunity>
                        asyncOpportunities =
                            _infrastructure
                                .CollectAsyncSiblingOpportunities(
                                    context,
                                    calls,
                                    methodDefinition,
                                    typeSourceGenerated,
                                    ref asyncSource);
                    if (!asyncOpportunities.IsDefaultOrEmpty)
                    {
                        result.Opportunities =
                            result.Opportunities.IsDefaultOrEmpty
                                ? asyncOpportunities
                                : result.Opportunities.AddRange(
                                    asyncOpportunities);
                    }
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    result.Diagnostic = new AnalysisDiagnostic(
                        MetadataTokens.GetToken(methodHandle),
                        MethodLabel(
                            typeHandle,
                            methodHandle),
                        $"{ex.GetType().Name}: {ex.Message}",
                        asyncSource?.MetadataToken,
                        caller.DeclaringType,
                        asyncSource?.DeclaringType);
                }
            }
            if (includeOwnershipFlow)
            {
                result.OwnershipFlow =
                    ArrayPoolOwnershipFlow.Analyze(
                        context,
                        calls.ToImmutable());
            }
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            result.Diagnostic = new AnalysisDiagnostic(
                MetadataTokens.GetToken(methodHandle),
                MethodLabel(
                    typeHandle,
                    methodHandle),
                $"{ex.GetType().Name}: {ex.Message}",
                SourceMethodToken:
                    result.DeclaredSource?.MetadataToken,
                DeclaringType: result.Caller?.DeclaringType,
                SourceDeclaringType:
                    result.DeclaredSource?.DeclaringType);
            if (includeLeakTriage
                && result.LeakTriage is null)
            {
                result.LeakTriage =
                    LeakTriageAnalyzer.Failed(
                        MetadataTokens.GetToken(methodHandle),
                        leakFailureKind,
                        ex.GetType().Name);
            }
        }
        finally
        {
            // Runs on every exit path so method-local evidence and calls
            // emitted before a recoverable failure remain visible.
            result.UnsafeEvidence = evidence.ToImmutable();
            result.Calls = calls.ToImmutable();
            result.ResultSinks = resultSinks?.ToImmutable() ?? [];
            result.FieldStores = fieldStores?.ToImmutable() ?? [];
            result.FieldLoads = fieldLoads?.ToImmutable() ?? [];
            result.CurrentInstanceMutations =
                currentInstanceMutations?.ToImmutable() ?? [];
            result.ReturnFlows = returnFlows?.ToImmutable() ?? [];
            if (result.LocalThrows is MethodLocalThrowEvidence.Unavailable
                { Reason: LocalThrowUnavailableReason.AnalysisFailed } failed)
            {
                result.LocalThrows = failed with
                {
                    Sites = localThrowSites?.ToImmutable() ?? [],
                    Detail = result.Diagnostic?.Message
                        ?? "Method analysis did not reach local-throw projection.",
                };
            }
        }
        return result;

        void SetLocalThrowUnavailable(LocalThrowUnavailableReason reason)
        {
            if (includeLocalThrows && !isReferenceAssembly)
            {
                result.LocalThrows = new MethodLocalThrowEvidence.Unavailable(
                    MetadataTokens.GetToken(methodHandle), reason, []);
            }
        }
    }

    LibraryMethodAnalysisResult AnalyzeLeakTriageMethod(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle)
    {
        var result = new LibraryMethodAnalysisResult();
        MetadataReader reader = _infrastructure.Reader;
        LeakTriageFailureKind leakFailureKind =
            LeakTriageFailureKind.MethodMetadata;
        try
        {
            var methodDefinition =
                reader.GetMethodDefinition(methodHandle);
            if (!HasManagedIlBody(
                    methodDefinition.ImplAttributes))
                return result;
            if (methodDefinition.RelativeVirtualAddress == 0)
                return result;

            var scope = _infrastructure.CreateScope(
                typeDefinition,
                methodDefinition);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    methodDefinition.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                result.LeakTriage =
                    LeakTriageAnalyzer.Failed(
                        MetadataTokens.GetToken(methodHandle),
                        LeakTriageFailureKind.MethodMetadata,
                        "SignatureLimit");
                return result;
            }

            var signature =
                methodDefinition.DecodeSignature(
                    TypeRefDecoder.Instance,
                    scope);
            var method = new MethodIdentity(
                _infrastructure.AssemblyName,
                _infrastructure.Mvid,
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    typeHandle,
                    0),
                reader.GetString(methodDefinition.Name),
                signature.ParameterTypes,
                signature.ReturnType,
                MetadataTokens.GetToken(methodHandle),
                (methodDefinition.Attributes
                    & MethodAttributes.Static) != 0)
            {
                SignatureHeader = signature.Header.RawValue,
                RequiredParameterCount =
                    signature.RequiredParameterCount,
                IsVirtualDispatchOpen =
                    _infrastructure.DispatchCanTargetOverride(
                        typeDefinition,
                        methodDefinition),
            };

            leakFailureKind =
                LeakTriageFailureKind.BodyAcquisition;
            MethodBodyData body = RequireMethodBody(
                _infrastructure.PeReader,
                method.MetadataToken);
            result.LeakTriage =
                LeakTriageAnalyzer.AnalyzeMethodDetailed(
                    method,
                    body,
                    token => _infrastructure.ResolveMethod(
                        token,
                        scope,
                        methodHandle),
                    token =>
                        ResourceExceptionPathAnalyzer.ResolveCatchTypeRef(
                            reader,
                            MetadataTokens.EntityHandle(token),
                            scope));
        }
        catch (Exception ex)
            when (LeakTriageAnalyzer.IsRecoverable(ex))
        {
            result.LeakTriage =
                LeakTriageAnalyzer.Failed(
                    MetadataTokens.GetToken(methodHandle),
                    leakFailureKind,
                    ex.GetType().Name);
        }

        return result;
    }

    internal static bool HasManagedIlBody(
        MethodImplAttributes attributes)
        => (attributes
                & MethodImplAttributes.CodeTypeMask)
            == MethodImplAttributes.IL
            && (attributes
                & MethodImplAttributes.ManagedMask)
            == MethodImplAttributes.Managed;

    internal static MethodInstructions DecodeBody(
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        // The substrate decode contract is BadImageFormatException for
        // malformed IL. Do not use MethodInstructions.Decode: its fail-closed
        // contract would hide the throw from the recoverable-method gate.
        var instructions = InstructionDecoder.Decode(il);
        return new MethodInstructions(
            instructions,
            BlockGraph.Build(
                il.Length,
                instructions,
                exceptionRegions));
    }

    static MethodBodyData RequireMethodBody(
        PEReader peReader,
        int methodToken) =>
        MethodBodySource.Read(peReader, methodToken) switch
        {
            MethodBodyReadResult.Available available =>
                available.Body,
            MethodBodyReadResult.NoBody =>
                throw new InvalidOperationException(
                    $"Method token 0x{methodToken:X8} has no managed IL body."),
            MethodBodyReadResult.Unavailable unavailable =>
                throw new BadImageFormatException(
                    $"Metadata method-body evidence is unavailable "
                    + $"({unavailable.Reason.GetType().Name})."),
            _ => throw new InvalidOperationException(
                "Unknown Metadata method-body result."),
        };

    ImmutableArray<TypeRef> DecodeLocalTypes(
        MethodBodyBlock body,
        GenericScope scope)
        => DecodeLocalTypesWithStatus(body, scope).Types;

    LocalTypeDecodeResult DecodeLocalTypesWithStatus(
        MethodBodyBlock body,
        GenericScope scope)
    {
        if (body.LocalSignature.IsNil)
            return new([], 0, null);
        MetadataReader reader = _infrastructure.Reader;
        var signature =
            reader.GetStandaloneSignature(body.LocalSignature);
        BlobReader blob = reader.GetBlobReader(signature.Signature);
        SignatureHeader header = blob.ReadSignatureHeader();
        if (header.Kind != SignatureKind.LocalVariables
            || header.RawValue != 0x07)
        {
            throw new BadImageFormatException(
                "A method body local signature does not have the local-variable signature kind.");
        }
        int declaredCount = blob.ReadCompressedInteger();
        if (declaredCount < 0)
        {
            throw new BadImageFormatException(
                "A method body local signature has an invalid local count.");
        }
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                signature.Signature,
                SignatureBlobGuard.Kind.LocalVariables))
        {
            return new(
                [],
                declaredCount,
                "The local signature exceeds the guarded decode policy.");
        }
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                signature.Signature,
                SignatureBlobGuard.Kind.LocalVariables))
        {
            return new(
                [],
                declaredCount,
                "The local signature is incomplete or contains trailing data.");
        }
        return new(
            signature.DecodeLocalSignature(
                TypeRefDecoder.Instance,
                scope),
            declaredCount,
            null);
    }

    readonly record struct LocalTypeDecodeResult(
        ImmutableArray<TypeRef> Types,
        int DeclaredCount,
        string? IncompleteReason);

    // Razor-generated render methods lack generated-code attributes. Trust-gate
    // RenderTreeBuilder identity (#1708) so lookalikes do not suppress findings.
    internal static bool IsBlazorRenderMethod(
        MethodIdentity caller)
    {
        foreach (var parameter in caller.ParameterTypes)
        {
            if (FrameworkIdentity.IsKnownFrameworkType(
                    parameter,
                    "Microsoft.AspNetCore.Components",
                    "Microsoft.AspNetCore.Components.Rendering",
                    "RenderTreeBuilder"))
            {
                return true;
            }
        }
        return false;
    }

    string MethodLabel(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle) =>
        MethodLabel(
            _infrastructure.Reader,
            typeHandle,
            methodHandle);

    internal static string MethodLabel(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle)
    {
        try
        {
            var typeDefinition =
                reader.GetTypeDefinition(typeHandle);
            string ns =
                reader.GetString(typeDefinition.Namespace);
            string typeName =
                reader.GetString(typeDefinition.Name);
            string methodName =
                reader.GetString(
                    reader.GetMethodDefinition(
                        methodHandle).Name);
            string fullTypeName =
                ns.Length == 0
                    ? typeName
                    : $"{ns}.{typeName}";
            return $"{fullTypeName}::{methodName}";
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return
                $"0x{MetadataTokens.GetToken(methodHandle):X8}";
        }
    }

    internal static bool IsRecoverableMethodFailure(
        Exception ex) =>
        ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or ArgumentOutOfRangeException
            or IndexOutOfRangeException;
}
