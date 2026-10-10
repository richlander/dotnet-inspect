using System.Diagnostics.CodeAnalysis;
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

    IMethodCallResolver CreateCallResolver(
        GenericScope scope,
        MethodDefinitionHandle caller);

    (TypeRef DeclaringType, ImmutableArray<TypeRef> TypeArguments)
        ResolveMethodOwner(
            int token,
            GenericScope scope,
            int maximumMethodSignatureBytes,
            int unitToken);

    MethodSignatureOutcome MethodSignature(
        BlobHandle signature,
        int maximumMethodSignatureBytes,
        int unitToken);

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

    bool TryResolveLocalTypeDefinition(
        TypeRef type,
        out TypeDefinitionHandle handle);

    bool CanCanonicalizeCurrentModuleReference(TypeRef type);

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

    bool HasRejectedAsyncStateMachineAttribute(
        MethodDefinition methodDefinition);

    ImmutableArray<OptimizationOpportunity>
        CollectAsyncSiblingOpportunities(
            MethodBodyAnalysisContext context,
            ImmutableArray<DirectCall>.Builder calls,
            MethodDefinition methodDefinition,
            bool typeSourceGenerated,
            ref MethodIdentity? asyncSource);

    AsyncSiblingOpportunityAnalyzer AsyncSiblingAnalyzer { get; }

    bool IsSourceGeneratedTypeOrEnclosing(TypeDefinitionHandle handle);

    bool TryResolveAsyncSiblingSource(
        MethodIdentity method,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        [NotNullWhen(true)] ref MethodIdentity? asyncSource);

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

internal enum ExtensionDeclarationShape
{
    None,
    Skeleton,
    Unconfirmed,
}

internal enum MethodBodyAvailability
{
    Present,
    NoApplicableInput,
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
    // Owner-issued body availability from declaration flags; see
    // docs/design/unsafe-member-findings.md#body-availability.
    public MethodBodyAvailability BodyAvailability;
    public bool RequiresDeclaredOwner;
    public bool InScope;
    // A C# extension block's declaration copy in its grouping type; the
    // implementation method on the enclosing static class is the member.
    public bool IsExtensionDeclarationSkeleton;
    public bool OwnerResolutionFailed;
    public DeclaredOwnerResolution OwnerResolution;
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

    /// <summary>
    /// Offsets of compiler-emitted <c>ReadOnlySpan&lt;T&gt;(void*, int)</c> constructor
    /// calls (newobj or in-place call) over RVA constant data, which are
    /// lowering rather than source calls.
    /// </summary>
    public ImmutableHashSet<int> ConstantDataSpanConstructors = [];
    public ImmutableArray<OptimizationOpportunity> Opportunities;
    public bool Suppressed;
    public bool ScopeExcluded;
    public bool HasSignals;
    public BodySignals Signals;
    public MethodImplementationMetricEvidence? ImplementationMetrics;
    public AnalysisDiagnostic? ImplementationMetricDiagnostic;
    public MethodBodyImplementationMetrics? ImplementationProfile;
    public AnalysisDiagnostic? Diagnostic;
    public MethodIdentity? DeclaredSource;
    public MethodBodyAnalysisContext? ResourceOccurrenceContext;
}

/// <summary>The outcome of the declaration phase of unsafe-evidence presence.</summary>
internal enum UnsafePresenceDeclaration
{
    Evidence,
    NoManagedBody,
    BodyRequired,
}

/// <summary>
/// Per-unit state shared by the declaration and body phases of
/// unsafe-evidence presence; it lives only for one unit.
/// </summary>
internal sealed class UnsafePresenceUnit(
    TypeDefinitionHandle typeHandle,
    TypeDefinition typeDefinition,
    MethodDefinitionHandle methodHandle,
    MethodDefinition methodDefinition)
{
    public TypeDefinitionHandle TypeHandle => typeHandle;

    public TypeDefinition TypeDefinition => typeDefinition;

    public MethodDefinitionHandle MethodHandle => methodHandle;

    public MethodDefinition MethodDefinition => methodDefinition;

    public GenericScope? Scope { get; set; }

    public MethodIdentity? Caller { get; set; }
}

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
internal sealed partial class LibraryMethodAnalysisRunner(
    ILibraryMethodAnalysisInfrastructure infrastructure,
    LibraryBodyExceptionTypeClassifier? exceptionTypes = null,
    ImplementationMetricWorkBudget?
        implementationMetricWork = null,
    ImplementationMetricExecutionRecorder?
        implementationMetricRecorder = null,
    LibraryBodyAnalysisStageRecorder?
        stageRecorder = null)
{
    readonly ILibraryMethodAnalysisInfrastructure _infrastructure =
        infrastructure;
    readonly UnsafeSignatureMarkerCache _unsafeSignatureMarkers =
        new(infrastructure.Reader);
    readonly UnsafePresenceWorkBudget _unsafePresenceWork =
        new();
    readonly ImplementationMetricWorkBudget?
        _implementationMetricWork =
            implementationMetricWork;
    readonly ImplementationMetricExecutionRecorder?
        _implementationMetricRecorder =
            implementationMetricRecorder;
    readonly LibraryBodyAnalysisStageRecorder?
        _stageRecorder =
            stageRecorder;

    // Whether a field token names a field definition of this image that has
    // an RVA (static data in the image), the source of Roslyn's constant spans.
    bool IsSameImageFieldWithRva(int token)
    {
        MetadataReader reader = _infrastructure.Reader;
        if ((token >> 24) != 0x04
            || (token & 0x00FFFFFF) is var row
                && (row == 0 || row > reader.GetTableRowCount(TableIndex.Field)))
        {
            return false;
        }
        FieldDefinition field = reader.GetFieldDefinition(
            MetadataTokens.FieldDefinitionHandle(token & 0x00FFFFFF));
        return (field.Attributes & System.Reflection.FieldAttributes.HasFieldRVA) != 0;
    }

    /// <summary>
    /// Unsafe-evidence presence, declaration phase: checks the definition's
    /// unsafe API type and signature before any body is read.
    /// </summary>
    internal UnsafePresenceDeclaration ProbeUnsafeDeclaration(
        UnsafePresenceUnit unit)
    {
        MetadataReader reader = _infrastructure.Reader;
        MethodDefinition methodDefinition = unit.MethodDefinition;
        TypeDefinitionHandle typeHandle = unit.TypeHandle;
        MethodIdentity Caller() => PresenceCaller(unit);

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
            return UnsafePresenceDeclaration.Evidence;
        }
        if (methodDefinition.RelativeVirtualAddress == 0
            || !HasManagedIlBody(
                methodDefinition.ImplAttributes))
        {
            return UnsafePresenceDeclaration.NoManagedBody;
        }

        return UnsafePresenceDeclaration.BodyRequired;
    }

    /// <summary>
    /// Unsafe-evidence presence, body phase: the unsafe local-signature check
    /// and the instruction scan with its call probe, stopping at the first
    /// evidence.
    /// </summary>
    internal bool ProbeUnsafeBody(
        UnsafePresenceUnit unit,
        MethodBodyBlock body)
    {
        MetadataReader reader = _infrastructure.Reader;
        GenericScope Scope() => PresenceScope(unit);

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
                    return true;
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

        return hasEvidence;
    }

    GenericScope PresenceScope(UnsafePresenceUnit unit) =>
        unit.Scope ??= _infrastructure.CreatePresenceScope(
            unit.TypeDefinition,
            unit.MethodDefinition,
            _unsafePresenceWork);

    MethodIdentity PresenceCaller(UnsafePresenceUnit unit) =>
        unit.Caller ??= _infrastructure.CreatePresenceMethodIdentity(
            unit.TypeHandle,
            unit.MethodHandle,
            unit.MethodDefinition,
            PresenceScope(unit),
            _unsafePresenceWork);

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
        bool includeJsonWireContractFlow = plan.Includes(
            LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        bool includeCallValueFlow = plan.RequiresCallValueFlow;
        bool includeLocalThrows = plan.Includes(
            LibraryBodyAnalysisFeatures.LocalThrows);
        if (plan.ImplementationMetrics
                is { UsesFocusedExecution: true }
            && !includeMethodEvidence)
        {
            return AnalyzeFocusedImplementationMetrics(
                typeHandle,
                typeDefinition,
                typeSourceGenerated,
                methodHandle,
                plan);
        }
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
            return new LibraryMethodAnalysisResult();

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
        try
        {
            var methodDefinition =
                reader.GetMethodDefinition(methodHandle);
            result.Token = MetadataTokens.GetToken(methodHandle);
            result.HasBody =
                methodDefinition.RelativeVirtualAddress != 0
                && HasManagedIlBody(
                    methodDefinition.ImplAttributes);
            result.BodyAvailability =
                ClassifyBodyAvailability(methodDefinition);
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
            // A body needs an authenticated owner when its name is a lifted
            // or state-machine body, or when it is declared in a
            // compiler-generated type nested inside another type.
            ExtensionDeclarationShape extensionShape =
                ClassifyExtensionDeclaration(
                    reader,
                    typeDefinition,
                    methodDefinition);
            result.IsExtensionDeclarationSkeleton =
                extensionShape == ExtensionDeclarationShape.Skeleton;
            result.RequiresDeclaredOwner =
                CompilerGeneratedNames.RequiresDeclaredOwner(caller)
                || IsDeclaredInNestedCompilerGeneratedType(
                    reader,
                    typeDefinition)
                || extensionShape == ExtensionDeclarationShape.Unconfirmed;
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
                result.InScope =
                    (bodyScope is null
                        || bodyScope.Contains(caller.MetadataToken))
                    && (bodyTypeScope is null
                        || bodyTypeScope(caller.DeclaringType));
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
            result.InScope = true;
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
            result.RequiresDeclaredOwner |= requiresDeclaredOwner;
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
                result.OwnerResolution = ownerResolution;
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
                result.OwnerResolutionFailed = true;
                opportunityOwnershipResolved = false;
                result.Diagnostic = new AnalysisDiagnostic(
                    MetadataTokens.GetToken(methodHandle),
                    MethodLabel(
                        typeHandle,
                        methodHandle),
                    $"{ex.GetType().Name}: {ex.Message}",
                    DeclaringType: caller.DeclaringType);
            }
            if (plan.RequestedFeatures
                == LibraryBodyAnalysisFeatures.None)
            {
                _implementationMetricWork
                    ?.ThrowIfMetricWorkExhausted(
                        caller.MetadataToken);
            }
            using LibraryBodyAnalysisStageRecorder.StageAttempt?
                bodyAcquisitionStage = StartStage(
                    LibraryBodyAnalysisStage
                        .ManagedBodyAcquisition);
            using ImplementationMetricExecutionRecorder.StageAttempt?
                bodyAcquisition = StartMetricStage(
                    plan,
                    ImplementationMetricWorkStage
                        .ManagedBodyAcquisition);
            MethodBodyData metadataBody = RequireMethodBody(
                _infrastructure.PeReader,
                caller.MetadataToken);
            var body = _infrastructure.PeReader.GetMethodBody(
                methodDefinition.RelativeVirtualAddress);
            bodyAcquisition?.Complete();
            bodyAcquisitionStage?.Complete();
            bool metricBodyAdmitted = true;
            try
            {
                _implementationMetricWork?.AdmitMetricBody(
                    caller.MetadataToken,
                    metadataBody.IL.Length);
            }
            catch (ImplementationMetricWorkLimitExceededException ex)
                when (plan.RequestedFeatures
                    != LibraryBodyAnalysisFeatures.None)
            {
                metricBodyAdmitted = false;
                result.ImplementationMetricDiagnostic =
                    new AnalysisDiagnostic(
                        caller.MetadataToken,
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
            var il = metadataBody.IL.ToArray();
            if (plan.ImplementationMetrics
                    is { IncludesHeaderMetrics: true } metricPlan
                && metricBodyAdmitted)
            {
                result.ImplementationMetrics =
                    CreateHeaderMetrics(
                        metricPlan,
                        result.DeclaredMethod ?? caller,
                        caller,
                        metadataBody);
            }
            if (plan.ImplementationMetrics
                    is { RequiresDirectCallDiscovery: true }
                && metricBodyAdmitted)
            {
                using LibraryBodyAnalysisStageRecorder.StageAttempt?
                    directCallDiscoveryStage = StartStage(
                        LibraryBodyAnalysisStage
                            .DirectCallDiscovery);
                using ImplementationMetricExecutionRecorder.StageAttempt?
                    discovery = StartMetricStage(
                        plan,
                        ImplementationMetricWorkStage
                            .DirectCallDiscovery);
                MethodCallAnalysis.DiscoveryCounts counts =
                    MethodCallAnalysis.DiscoverCounts(body);
                discovery?.Complete();
                directCallDiscoveryStage?.Complete();
                result.ImplementationMetrics =
                    CreateDirectCallDiscoveryMetrics(
                        plan.ImplementationMetrics!,
                        result.ImplementationMetrics,
                        result.DeclaredMethod ?? caller,
                        caller,
                        counts);
            }
            using LibraryBodyAnalysisStageRecorder.StageAttempt?
                localDecodeStage = StartStage(
                    LibraryBodyAnalysisStage
                        .LocalSignatureDecode);
            using ImplementationMetricExecutionRecorder.StageAttempt?
                localDecode = StartMetricStage(
                    plan,
                    ImplementationMetricWorkStage
                        .LocalSignatureDecode);
            LocalTypeDecodeResult localTypes =
                DecodeLocalTypesWithStatus(
                    body,
                    scope);
            localDecode?.Complete();
            localDecodeStage?.Complete();
            if (plan.ImplementationMetrics
                    is { IncludesLocalMetric: true }
                && metricBodyAdmitted)
            {
                result.ImplementationMetrics =
                    CreateLocalMetrics(
                        result.ImplementationMetrics,
                        result.DeclaredMethod ?? caller,
                        caller,
                        localTypes);
            }
            using LibraryBodyAnalysisStageRecorder.StageAttempt?
                contextConstructionStage = StartStage(
                    LibraryBodyAnalysisStage
                        .CanonicalMethodContext);
            using ImplementationMetricExecutionRecorder.StageAttempt?
                contextConstruction = StartMetricStage(
                    plan,
                    ImplementationMetricWorkStage
                        .CanonicalMethodContext);
            MethodBodyAnalysisContext context =
                MethodBodyAnalysisContext.Create(
                caller,
                metadataBody,
                localTypes.Types,
                localTypes.DeclaredCount,
                localTypes.IncompleteReason,
                body.LocalVariablesInitialized);
            contextConstruction?.Complete();
            contextConstructionStage?.Complete();
            if (plan.IncludesResourceOccurrences)
                result.ResourceOccurrenceContext = context;
            MethodInstructions methodInstructions =
                context.Instructions;
            ImplementationMetricAnalysisPlan? implementationMetricPlan =
                plan.ImplementationMetrics;
            bool measureInstructionShape =
                includeImplementationProfiles
                || implementationMetricPlan
                    ?.IncludesInstructionShapeMetric == true;
            bool measureControlFlow =
                includeImplementationProfiles
                || implementationMetricPlan
                    ?.IncludesControlFlowMetric == true;
            MethodImplementationContextMeasurements?
                contextMeasurements = null;
            if (metricBodyAdmitted
                && (measureInstructionShape
                    || measureControlFlow))
            {
                contextMeasurements =
                    MethodImplementationProfileAnalysis
                        .MeasureContext(
                            context,
                            measureInstructionShape,
                            measureControlFlow);
            }
            if (implementationMetricPlan
                    is { IncludesFocusedContextMetrics: true }
                && contextMeasurements is { } focusedMeasurements
                && metricBodyAdmitted)
            {
                result.ImplementationMetrics =
                    CreateContextMetrics(
                        result.ImplementationMetrics,
                        result.DeclaredMethod ?? caller,
                        caller,
                        focusedMeasurements);
            }
            if (includeImplementationProfiles
                && metricBodyAdmitted)
            {
                result.ImplementationProfile =
                    MethodImplementationProfileAnalysis.Measure(
                        context,
                        result.DeclaredMethod ?? caller,
                        il.Length,
                        asyncBody is not null,
                        contextMeasurements
                        ?? throw new InvalidOperationException(
                            "Implementation profiles require context measurements."));
            }
            MethodAllocationFacts allocationFacts;
            ILibraryMethodAnalysisResolver methodAnalysisResolver;
            using (LibraryBodyAnalysisStageRecorder.StageAttempt?
                allocationStage = StartStage(
                    LibraryBodyAnalysisStage.AllocationAnalysis))
            {
                methodAnalysisResolver =
                    _infrastructure.CreateMethodAnalysisResolver(
                        scope,
                        caller,
                        methodInstructions);
                // Build allocation's Layer-1 indexes before other topic
                // producers, then keep every result and query bound to this
                // exact context.
                allocationFacts =
                    MethodAllocationFacts.Create(context);
                // Discover and classify allocation occurrences once.
                // Performance Triage consumes the allocation owner's
                // lifetime verdict rather than running a parallel escape
                // analysis.
                if (includeAllocations)
                    allocationFacts.Collect(methodAnalysisResolver);
                result.Allocations =
                    allocationFacts.ClassifiedOccurrences;
                allocationStage?.Complete();
            }
            bool hasUnsafeLocals;
            using (LibraryBodyAnalysisStageRecorder.StageAttempt?
                safetyStage = StartStage(
                    LibraryBodyAnalysisStage.SafetyAnalysis))
            {
                var localSafety =
                    MethodSafetyAnalysis.InspectLocals(
                        context,
                        evidence);
                hasUnsafeLocals =
                    localSafety.HasUnsafeLocals;
                result.Unsafety =
                    MethodSafetyAnalysis.CollectOccurrences(
                        context,
                        token => _infrastructure.CalliReturnDetail(
                            token,
                            scope),
                        token => ((IMethodAllocationResolver)
                            methodAnalysisResolver)
                            .ResolveMember(token));
                result.ConstantDataSpanConstructors =
                    SpanStackAllocations.RecognizeConstantDataSpans(
                        context,
                        token => ((IMethodAllocationResolver)
                            methodAnalysisResolver)
                            .ResolveMember(token),
                        IsSameImageFieldWithRva);
                safetyStage?.Complete();
            }
            BodySignals signals;
            using (LibraryBodyAnalysisStageRecorder.StageAttempt?
                bodySignalStage = StartStage(
                    LibraryBodyAnalysisStage.BodySignalAnalysis))
            {
                signals = BodySignalAnalysis.Collect(
                    context,
                    token => _infrastructure
                        .IsAllocatingValueTypeBox(
                            token,
                            scope));
                bodySignalStage?.Complete();
            }
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
                bool collectDirectCallFacts =
                    implementationMetricPlan
                        ?.RequiresDirectCallFacts == true
                    && metricBodyAdmitted;
                if (collectDirectCallFacts)
                {
                    result.ImplementationMetrics =
                        MarkDirectCallCollection(
                            result.ImplementationMetrics,
                            result.DeclaredMethod ?? caller,
                            caller,
                            complete: false);
                }
                using ImplementationMetricExecutionRecorder.StageAttempt?
                    directCallCollection = StartMetricStage(
                        plan,
                        ImplementationMetricWorkStage
                            .DirectCallCollection);
                using LibraryBodyAnalysisStageRecorder.StageAttempt?
                    callAnalysisStage = StartStage(
                        LibraryBodyAnalysisStage.CallAnalysis);
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
                directCallCollection?.Complete();
                callAnalysisStage?.Complete();
                if (collectDirectCallFacts)
                {
                    result.ImplementationMetrics =
                        MarkDirectCallCollection(
                            result.ImplementationMetrics,
                            result.DeclaredMethod ?? caller,
                            caller,
                            complete: true);
                }
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
                using LibraryBodyAnalysisStageRecorder.StageAttempt?
                    stringMaterializationStage = StartStage(
                        LibraryBodyAnalysisStage
                            .StringMaterializationAnalysis);
                result.StringMaterializations =
                    StringMaterializationAnalysis.Collect(
                        calls,
                        stringReceiverSources);
                stringMaterializationStage?.Complete();
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
                using LibraryBodyAnalysisStageRecorder.StageAttempt?
                    opportunityStage = StartStage(
                        LibraryBodyAnalysisStage
                            .OptimizationOpportunityAnalysis);
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
                opportunityStage?.Complete();
            }

            if (collectScopedAsyncSiblingOpportunities
                && opportunityOwnershipResolved)
            {
                MethodIdentity? asyncSource = null;
                try
                {
                    using LibraryBodyAnalysisStageRecorder.StageAttempt?
                        asyncSiblingStage = StartStage(
                            LibraryBodyAnalysisStage
                                .AsyncSiblingAnalysis);
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
                    asyncSiblingStage?.Complete();
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

    LibraryMethodAnalysisResult AnalyzeFocusedImplementationMetrics(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        bool typeSourceGenerated,
        MethodDefinitionHandle methodHandle,
        LibraryBodyAnalysisPlan plan)
    {
        var result = new LibraryMethodAnalysisResult();
        MetadataReader reader = _infrastructure.Reader;
        try
        {
            MethodDefinition methodDefinition =
                reader.GetMethodDefinition(methodHandle);
            result.Token = MetadataTokens.GetToken(methodHandle);
            result.HasBody =
                methodDefinition.RelativeVirtualAddress != 0
                && HasManagedIlBody(
                    methodDefinition.ImplAttributes);
            result.BodyAvailability =
                ClassifyBodyAvailability(methodDefinition);
            GenericScope scope = _infrastructure.CreateScope(
                typeDefinition,
                methodDefinition);
            MethodIdentity caller =
                _infrastructure.CreateMethodIdentity(
                    typeHandle,
                    methodHandle,
                    methodDefinition,
                    scope);
            result.HasCaller = true;
            result.Caller = caller;
            result.Token = caller.MetadataToken;
            if (!result.HasBody)
                return result;

            IReadOnlySet<int>? bodyScope = plan.MethodScope;
            Func<TypeRef, bool>? bodyTypeScope = plan.TypeScope;
            if (bodyScope is not null
                && !bodyScope.Contains(caller.MetadataToken))
            {
                return result;
            }
            bool directlySelectedType =
                bodyTypeScope?.Invoke(caller.DeclaringType)
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
                        || !sourceTypes.Any(bodyTypeScope)))
                {
                    return result;
                }
            }

            bool directlySelectedBody =
                directlySelectedType
                || plan.RequestedMethodScope?.Contains(
                    caller.MetadataToken)
                    == true;
            bool requiresDeclaredOwner =
                CompilerGeneratedNames.RequiresDeclaredOwner(
                    caller,
                    _infrastructure
                        .IsAuthenticatedAsyncStateMachineExecutionMethod(
                            methodHandle,
                            methodDefinition));
            result.RequiresDeclaredOwner |= requiresDeclaredOwner;
            Exception? asyncAttributionFailure = null;
            AsyncBodyAttribution? asyncBody = null;
            try
            {
                MethodIdentity? declaredMethod;
                try
                {
                    declaredMethod =
                        _infrastructure.ResolveDeclaredMethod(
                            methodHandle,
                            methodDefinition,
                            caller,
                            typeSourceGenerated,
                            bodyScope,
                            bodyTypeScope,
                            plan.RequestedMethodScope,
                            directlySelectedBody);
                }
                catch (Exception ex)
                    when (!requiresDeclaredOwner
                        && IsRecoverableMethodFailure(ex))
                {
                    declaredMethod = caller;
                    if (_infrastructure
                        .HasRejectedAsyncStateMachineAttribute(
                            methodDefinition))
                    {
                        asyncAttributionFailure = ex;
                    }
                }
                result.DeclaredMethod = declaredMethod;
                try
                {
                    asyncBody =
                        _infrastructure.ResolveAsyncBody(
                            caller,
                            methodDefinition,
                            typeSourceGenerated);
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    asyncBody = null;
                    if (_infrastructure
                        .HasRejectedAsyncStateMachineAttribute(
                            methodDefinition))
                    {
                        asyncAttributionFailure ??= ex;
                    }
                }
                DeclaredOwnerResolution ownerResolution =
                    declaredMethod is null
                        ? DeclaredOwnerResolution.None
                        : DeclaredOwnerResolution.Resolved;
                if (declaredMethod is not null
                    && CompilerGeneratedNames
                        .IsLocalFunctionOrLambda(
                            declaredMethod.Name))
                {
                    ownerResolution =
                        _infrastructure.ResolveUltimateDeclaredMethod(
                            methodHandle,
                            methodDefinition,
                            caller,
                            typeSourceGenerated,
                            out _,
                            out AuthenticatedSourceOwner?
                                ultimateOwner);
                    if (ownerResolution
                        == DeclaredOwnerResolution.Resolved)
                    {
                        result.DeclaredMethod =
                            ultimateOwner?.Method;
                    }
                }
                if (ownerResolution is
                    DeclaredOwnerResolution.Unresolved
                    or DeclaredOwnerResolution.Rejected)
                {
                    result.DeclaredMethod = null;
                }
                result.DeclaredSource =
                    result.DeclaredMethod;
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                if (!directlySelectedBody
                    && !plan.ProducesLibraryStructuralReport)
                {
                    throw;
                }
                result.DeclaredMethod = null;
                result.DeclaredSource = null;
                result.Diagnostic = new AnalysisDiagnostic(
                    MetadataTokens.GetToken(methodHandle),
                    MethodLabel(
                        typeHandle,
                        methodHandle),
                    $"{ex.GetType().Name}: {ex.Message}",
                    DeclaringType: caller.DeclaringType);
            }
            if (asyncAttributionFailure is not null)
            {
                MethodIdentity? diagnosticSource =
                    result.DeclaredSource is { } declaredSource
                    && declaredSource.MetadataToken
                        != caller.MetadataToken
                        ? declaredSource
                        : null;
                result.ImplementationMetricDiagnostic =
                    new AnalysisDiagnostic(
                        caller.MetadataToken,
                        MethodLabel(
                            typeHandle,
                            methodHandle),
                        $"{asyncAttributionFailure.GetType().Name}: "
                            + asyncAttributionFailure.Message,
                        SourceMethodToken:
                            diagnosticSource?.MetadataToken,
                        DeclaringType: caller.DeclaringType,
                        SourceDeclaringType:
                            diagnosticSource?.DeclaringType);
            }

            ImplementationMetricAnalysisPlan metricPlan =
                plan.ImplementationMetrics
                ?? throw new InvalidOperationException(
                    "Focused metric execution requires a metric plan.");
            bool? isAsync = null;
            if (metricPlan.IncludesAsyncMetric)
            {
                isAsync = asyncBody is not null;
            }
            _implementationMetricWork
                ?.ThrowIfMetricWorkExhausted(
                    caller.MetadataToken);
            using LibraryBodyAnalysisStageRecorder.StageAttempt?
                bodyAcquisitionStage = StartStage(
                    LibraryBodyAnalysisStage
                        .ManagedBodyAcquisition);
            using ImplementationMetricExecutionRecorder.StageAttempt?
                bodyAcquisition = StartMetricStage(
                    plan,
                    ImplementationMetricWorkStage
                        .ManagedBodyAcquisition);
            MethodBodyData metadataBody = RequireMethodBody(
                _infrastructure.PeReader,
                caller.MetadataToken);
            MethodBodyBlock? body =
                metricPlan.RequiresMethodBodyBlock
                    ? _infrastructure.PeReader.GetMethodBody(
                        methodDefinition.RelativeVirtualAddress)
                    : null;
            bodyAcquisition?.Complete();
            bodyAcquisitionStage?.Complete();
            _implementationMetricWork?.AdmitMetricBody(
                caller.MetadataToken,
                metadataBody.IL.Length);
            if (metricPlan.IncludesHeaderMetrics)
            {
                result.ImplementationMetrics =
                    CreateHeaderMetrics(
                        metricPlan,
                        result.DeclaredMethod ?? caller,
                        caller,
                        metadataBody);
            }
            if (isAsync is not null)
            {
                result.ImplementationMetrics =
                    SetAsyncMetric(
                        result.ImplementationMetrics,
                        result.DeclaredMethod ?? caller,
                        caller,
                        isAsync.Value);
            }
            if (plan.ProducesLibraryStructuralReport)
            {
                return AnalyzeLibraryStructuralMetrics(
                    result,
                    plan,
                    caller,
                    scope,
                    metadataBody,
                    body
                    ?? throw new InvalidOperationException(
                        "Library structural metrics require a method body."),
                    isAsync ?? false);
            }
            if (body is null)
                return result;

            if (metricPlan.RequiresDirectCallDiscovery)
            {
                try
                {
                    using LibraryBodyAnalysisStageRecorder.StageAttempt?
                        directCallDiscoveryStage = StartStage(
                            LibraryBodyAnalysisStage
                                .DirectCallDiscovery);
                    using ImplementationMetricExecutionRecorder.StageAttempt?
                        discovery = StartMetricStage(
                            plan,
                            ImplementationMetricWorkStage
                                .DirectCallDiscovery);
                    MethodCallAnalysis.DiscoveryCounts counts =
                        MethodCallAnalysis.DiscoverCounts(body);
                    discovery?.Complete();
                    directCallDiscoveryStage?.Complete();
                    result.ImplementationMetrics =
                        CreateDirectCallDiscoveryMetrics(
                            metricPlan,
                            result.ImplementationMetrics,
                            result.DeclaredMethod ?? caller,
                            caller,
                            counts);
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    result.ImplementationMetricDiagnostic =
                        new AnalysisDiagnostic(
                            caller.MetadataToken,
                            MethodLabel(
                                typeHandle,
                                methodHandle),
                            $"{ex.GetType().Name}: {ex.Message}",
                            SourceMethodToken:
                                result.DeclaredSource?.MetadataToken,
                            DeclaringType:
                                caller.DeclaringType,
                            SourceDeclaringType:
                                result.DeclaredSource?.DeclaringType);
                }
            }
            if (!metricPlan.RequiresLocalSignatureDecode)
                return result;

            LocalTypeDecodeResult localTypes;
            try
            {
                using LibraryBodyAnalysisStageRecorder.StageAttempt?
                    localDecodeStage = StartStage(
                        LibraryBodyAnalysisStage
                            .LocalSignatureDecode);
                using ImplementationMetricExecutionRecorder.StageAttempt?
                    localDecode = StartMetricStage(
                        plan,
                        ImplementationMetricWorkStage
                            .LocalSignatureDecode);
                localTypes =
                    DecodeLocalTypesWithStatus(
                        body,
                        scope);
                localDecode?.Complete();
                localDecodeStage?.Complete();
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                result.ImplementationMetricDiagnostic =
                    new AnalysisDiagnostic(
                        caller.MetadataToken,
                        MethodLabel(
                            typeHandle,
                            methodHandle),
                        $"{ex.GetType().Name}: {ex.Message}",
                        SourceMethodToken:
                            result.DeclaredSource?.MetadataToken,
                        DeclaringType:
                            caller.DeclaringType,
                        SourceDeclaringType:
                            result.DeclaredSource?.DeclaringType);
                return result;
            }
            if (metricPlan.IncludesLocalMetric)
            {
                result.ImplementationMetrics =
                    CreateLocalMetrics(
                        result.ImplementationMetrics,
                        result.DeclaredMethod ?? caller,
                        caller,
                        localTypes);
            }
            MethodBodyAnalysisContext? context = null;
            if (metricPlan.RequiresCanonicalContext)
            {
                try
                {
                    using LibraryBodyAnalysisStageRecorder.StageAttempt?
                        contextConstructionStage = StartStage(
                            LibraryBodyAnalysisStage
                                .CanonicalMethodContext);
                    using ImplementationMetricExecutionRecorder.StageAttempt?
                        contextConstruction = StartMetricStage(
                            plan,
                            ImplementationMetricWorkStage
                                .CanonicalMethodContext);
                    context =
                        MethodBodyAnalysisContext.Create(
                            caller,
                            metadataBody,
                            localTypes.Types,
                            localTypes.DeclaredCount,
                            localTypes.IncompleteReason,
                            body.LocalVariablesInitialized);
                    contextConstruction?.Complete();
                    contextConstructionStage?.Complete();
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    result.ImplementationMetricDiagnostic =
                        new AnalysisDiagnostic(
                            caller.MetadataToken,
                            MethodLabel(
                                typeHandle,
                                methodHandle),
                            $"{ex.GetType().Name}: {ex.Message}",
                            SourceMethodToken:
                                result.DeclaredSource?.MetadataToken,
                            DeclaringType:
                                caller.DeclaringType,
                            SourceDeclaringType:
                                result.DeclaredSource?.DeclaringType);
                }
            }
            if (context is not null
                && metricPlan.IncludesFocusedContextMetrics)
            {
                try
                {
                    MethodImplementationContextMeasurements measurements =
                        MethodImplementationProfileAnalysis
                            .MeasureContext(
                                context,
                                metricPlan
                                    .IncludesInstructionShapeMetric,
                                metricPlan
                                    .IncludesControlFlowMetric);
                    result.ImplementationMetrics =
                        CreateContextMetrics(
                            result.ImplementationMetrics,
                            result.DeclaredMethod ?? caller,
                            caller,
                            measurements) with
                        {
                            IncompleteReasons =
                                MethodImplementationProfileAnalysis
                                    .IncompleteReasons(context),
                        };
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    result.ImplementationMetricDiagnostic ??=
                        new AnalysisDiagnostic(
                            caller.MetadataToken,
                            MethodLabel(
                                typeHandle,
                                methodHandle),
                            $"{ex.GetType().Name}: {ex.Message}",
                            SourceMethodToken:
                                result.DeclaredSource?.MetadataToken,
                            DeclaringType:
                                caller.DeclaringType,
                            SourceDeclaringType:
                                result.DeclaredSource?.DeclaringType);
                }
            }
            if (context is not null
                && metricPlan.RequiresDirectCallFacts)
            {
                var calls =
                    ImmutableArray.CreateBuilder<DirectCall>();
                result.ImplementationMetrics =
                    MarkDirectCallCollection(
                        result.ImplementationMetrics,
                        result.DeclaredMethod ?? caller,
                        caller,
                        complete: false);
                try
                {
                    using LibraryBodyAnalysisStageRecorder.StageAttempt?
                        callAnalysisStage = StartStage(
                            LibraryBodyAnalysisStage
                                .CallAnalysis);
                    using ImplementationMetricExecutionRecorder.StageAttempt?
                        directCallCollection = StartMetricStage(
                            plan,
                            ImplementationMetricWorkStage
                                .DirectCallCollection);
                    MethodCallAnalysis.CollectDirectCalls(
                        context,
                        _infrastructure.CreateCallResolver(
                            scope,
                            caller),
                        calls);
                    directCallCollection?.Complete();
                    callAnalysisStage?.Complete();
                    result.ImplementationMetrics =
                        MarkDirectCallCollection(
                            result.ImplementationMetrics,
                            result.DeclaredMethod ?? caller,
                            caller,
                            complete: true);
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    result.ImplementationMetricDiagnostic ??=
                        new AnalysisDiagnostic(
                            caller.MetadataToken,
                            MethodLabel(
                                typeHandle,
                                methodHandle),
                            $"{ex.GetType().Name}: {ex.Message}",
                            SourceMethodToken:
                                result.DeclaredSource?.MetadataToken,
                            DeclaringType:
                                caller.DeclaringType,
                            SourceDeclaringType:
                                result.DeclaredSource?.DeclaringType);
                }
                finally
                {
                    result.Calls = calls.ToImmutable();
                }
            }
            if (context is not null
                && metricPlan.IncludesAllocationCountMetric)
            {
                try
                {
                    using LibraryBodyAnalysisStageRecorder.StageAttempt?
                        bodySignalStage = StartStage(
                            LibraryBodyAnalysisStage
                                .BodySignalAnalysis);
                    using ImplementationMetricExecutionRecorder.StageAttempt?
                        allocationSignals = StartMetricStage(
                            plan,
                            ImplementationMetricWorkStage
                                .AllocationSignalCollection);
                    BodySignals signals =
                        BodySignalAnalysis.Collect(
                            context,
                            token => _infrastructure
                                .IsAllocatingValueTypeBox(
                                    token,
                                    scope));
                    result.Signals = signals;
                    result.HasSignals =
                        signals.Newarr > 0
                        || signals.Boxes > 0;
                    allocationSignals?.Complete();
                    bodySignalStage?.Complete();
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    result.ImplementationMetricDiagnostic ??=
                        new AnalysisDiagnostic(
                            caller.MetadataToken,
                            MethodLabel(
                                typeHandle,
                                methodHandle),
                            $"{ex.GetType().Name}: {ex.Message}",
                            SourceMethodToken:
                                result.DeclaredSource?.MetadataToken,
                            DeclaringType:
                                caller.DeclaringType,
                            SourceDeclaringType:
                                result.DeclaredSource?.DeclaringType);
                }
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
        }
        return result;
    }

    LibraryMethodAnalysisResult AnalyzeLibraryStructuralMetrics(
        LibraryMethodAnalysisResult result,
        LibraryBodyAnalysisPlan plan,
        MethodIdentity caller,
        GenericScope scope,
        MethodBodyData metadataBody,
        MethodBodyBlock body,
        bool isAsync)
    {
        ImmutableArray<DecodedInstruction> instructions;
        var distinctOpcodes = new HashSet<ILOpCode>();
        var loopRegions = new HashSet<(int Start, int End)>();
        int branches = 0;
        int conditionalBranches = 0;
        int switches = 0;
        int switchTargets = 0;
        int directCalls = 0;
        int newArrays = 0;
        int boxes = 0;
        using (
            LibraryBodyAnalysisStageRecorder.StageAttempt?
                bodySignalStage = StartStage(
                    LibraryBodyAnalysisStage.BodySignalAnalysis))
        using (
            ImplementationMetricExecutionRecorder.StageAttempt?
                structuralScan = StartMetricStage(
                    plan,
                    ImplementationMetricWorkStage
                        .StructuralInstructionScan))
        using (
            ImplementationMetricExecutionRecorder.StageAttempt?
                allocationSignals = StartMetricStage(
                    plan,
                    ImplementationMetricWorkStage
                        .AllocationSignalCollection))
        {
            instructions =
                InstructionDecoder.Decode(metadataBody.IL.AsSpan());
            foreach (DecodedInstruction instruction
                in instructions)
            {
                ILOpCode opcode = instruction.OpCode;
                distinctOpcodes.Add(opcode);
                if (opcode is
                    ILOpCode.Call
                    or ILOpCode.Callvirt
                    or ILOpCode.Newobj)
                {
                    directCalls++;
                }
                if (opcode == ILOpCode.Newarr)
                {
                    newArrays++;
                }
                else if (opcode == ILOpCode.Box
                    && _infrastructure.IsAllocatingValueTypeBox(
                        MethodInstructionFacts.OperandInt32(
                            instruction),
                        scope))
                {
                    boxes++;
                }

                if (instruction.Branches)
                {
                    branches++;
                    if (!instruction.IsUnconditionalBranch)
                        conditionalBranches++;
                }
                if (opcode == ILOpCode.Switch)
                {
                    switches++;
                    switchTargets +=
                        instruction.BranchTargets.Length;
                    continue;
                }
                foreach (int target
                    in instruction.BranchTargets)
                {
                    bool redirectsThroughFinally =
                        instruction.LeavesRegion
                        && metadataBody.ExceptionRegionCatalog
                            .Clauses.Any(clause =>
                                clause.Kind
                                    == ExceptionRegionKind.Finally
                                && clause.ProtectedExtent.Contains(
                                    instruction.Offset)
                                && !clause.ProtectedExtent.Contains(
                                    target));
                    if (target < instruction.Offset
                        && !redirectsThroughFinally)
                    {
                        loopRegions.Add(
                            (target, instruction.Offset));
                    }
                }
            }
            allocationSignals?.Complete();
            structuralScan?.Complete();
            bodySignalStage?.Complete();
        }

        int catches = 0;
        int filters = 0;
        int finallys = 0;
        int faults = 0;
        foreach (MethodExceptionClause clause
            in metadataBody.ExceptionRegionCatalog.Clauses)
        {
            switch (clause.Kind)
            {
                case ExceptionRegionKind.Catch:
                    catches++;
                    break;
                case ExceptionRegionKind.Filter:
                    filters++;
                    break;
                case ExceptionRegionKind.Finally:
                    finallys++;
                    break;
                case ExceptionRegionKind.Fault:
                    faults++;
                    break;
            }
        }

        MethodIdentity method =
            result.DeclaredMethod ?? caller;
        result.ImplementationMetrics =
            new(
                method,
                caller,
                ILBytes: null,
                new(
                    catches,
                    filters,
                    finallys,
                    faults),
                Locals: null,
                new(
                    instructions.Length,
                    distinctOpcodes.Count),
                new(
                    BasicBlockCount: 0,
                    branches,
                    conditionalBranches,
                    switches,
                    switchTargets,
                    loopRegions.Count),
                new(directCalls),
                CallSiteCount: null,
                DirectCalls: null)
            {
                IsAsync = isAsync,
                DirectCallCollectionAttempted = true,
            };
        if (newArrays > 0 || boxes > 0)
        {
            result.Signals = new(
                newArrays,
                Throws: 0,
                Catches: 0,
                Finallys: 0,
                ArrayAllocOffsets: [],
                ThrowOffsets: [],
                boxes,
                BoxOffsets: []);
            result.HasSignals = true;
        }

        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        try
        {
            using LibraryBodyAnalysisStageRecorder.StageAttempt?
                callAnalysisStage = StartStage(
                    LibraryBodyAnalysisStage.CallAnalysis);
            using ImplementationMetricExecutionRecorder.StageAttempt?
                directCallCollection = StartMetricStage(
                    plan,
                    ImplementationMetricWorkStage
                        .DirectCallCollection);
            MethodCallAnalysis.CollectStructuralDirectCalls(
                caller,
                instructions,
                _infrastructure.CreateCallResolver(
                    scope,
                    caller),
                calls);
            directCallCollection?.Complete();
            callAnalysisStage?.Complete();
            result.ImplementationMetrics =
                result.ImplementationMetrics with
                {
                    DirectCallCollectionComplete = true,
                };
        }
        finally
        {
            result.Calls = calls.ToImmutable();
        }
        return result;
    }

    ImplementationMetricExecutionRecorder.StageAttempt?
        StartMetricStage(
            LibraryBodyAnalysisPlan plan,
            ImplementationMetricWorkStage stage) =>
        plan.ImplementationMetrics is null
            ? null
            : _implementationMetricRecorder?.Start(stage);

    LibraryBodyAnalysisStageRecorder.StageAttempt?
        StartStage(LibraryBodyAnalysisStage stage) =>
        _stageRecorder?.Start(stage);

    static MethodImplementationMetricEvidence CreateHeaderMetrics(
        ImplementationMetricAnalysisPlan plan,
        MethodIdentity method,
        MethodIdentity evidenceMethod,
        MethodBodyData body)
    {
        ImplementationMetricExceptionRegionCounts?
            exceptionRegions = null;
        if (plan.RequestedMetrics.HasFlag(
                ImplementationMetricKind
                    .ExceptionRegions))
        {
            int catches = 0;
            int filters = 0;
            int finallys = 0;
            int faults = 0;
            foreach (MethodExceptionClause clause
                in body.ExceptionRegionCatalog.Clauses)
            {
                switch (clause.Kind)
                {
                    case ExceptionRegionKind.Catch:
                        catches++;
                        break;
                    case ExceptionRegionKind.Filter:
                        filters++;
                        break;
                    case ExceptionRegionKind.Finally:
                        finallys++;
                        break;
                    case ExceptionRegionKind.Fault:
                        faults++;
                        break;
                }
            }
            exceptionRegions = new(
                catches,
                filters,
                finallys,
                faults);
        }

        return new(
            method,
            evidenceMethod,
            plan.RequestedMetrics.HasFlag(
                ImplementationMetricKind.BodySize)
                ? body.IL.Length
                : null,
            exceptionRegions,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    static MethodImplementationMetricEvidence
        CreateDirectCallDiscoveryMetrics(
            ImplementationMetricAnalysisPlan plan,
            MethodImplementationMetricEvidence? existing,
            MethodIdentity method,
            MethodIdentity evidenceMethod,
            MethodCallAnalysis.DiscoveryCounts counts)
    {
        ImplementationMetricDirectCallCount? invocationEvidence =
            plan.IncludesDirectCallCountMetric
                || plan.IncludesDirectCallMetric
                ? new(counts.InvocationCount)
                : null;
        ImplementationMetricCallSiteCount? callSiteEvidence =
            plan.IncludesCallSiteCountMetric
                ? new(counts.CallSiteCount)
                : null;
        return existing is null
            ? new(
                method,
                evidenceMethod,
                null,
                null,
                null,
                null,
                null,
                invocationEvidence,
                callSiteEvidence,
                null)
            : existing with
            {
                DirectCallCount = invocationEvidence,
                CallSiteCount = callSiteEvidence,
            };
    }

    static MethodImplementationMetricEvidence CreateLocalMetrics(
        MethodImplementationMetricEvidence? existing,
        MethodIdentity method,
        MethodIdentity evidenceMethod,
        LocalTypeDecodeResult locals)
    {
        ImplementationMetricLocalEvidence evidence =
            new(
                locals.DeclaredCount,
                locals.IncompleteReason);
        return existing is null
            ? new(
                method,
                evidenceMethod,
                null,
                null,
                evidence,
                null,
                null,
                null,
                null,
                null)
            : existing with { Locals = evidence };
    }

    static MethodImplementationMetricEvidence CreateContextMetrics(
        MethodImplementationMetricEvidence? existing,
        MethodIdentity method,
        MethodIdentity evidenceMethod,
        MethodImplementationContextMeasurements measurements)
    {
        return existing is null
            ? new(
                method,
                evidenceMethod,
                null,
                null,
                null,
                measurements.InstructionShape,
                measurements.ControlFlow,
                null,
                null,
                null)
            : existing with
            {
                InstructionShape = measurements.InstructionShape,
                ControlFlow = measurements.ControlFlow,
            };
    }

    static MethodImplementationMetricEvidence SetAsyncMetric(
        MethodImplementationMetricEvidence? existing,
        MethodIdentity method,
        MethodIdentity evidenceMethod,
        bool isAsync)
    {
        MethodImplementationMetricEvidence evidence =
            existing
            ?? new(
                method,
                evidenceMethod,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        return evidence with { IsAsync = isAsync };
    }

    static MethodImplementationMetricEvidence MarkDirectCallCollection(
        MethodImplementationMetricEvidence? existing,
        MethodIdentity method,
        MethodIdentity evidenceMethod,
        bool complete)
    {
        MethodImplementationMetricEvidence evidence =
            existing
            ?? new(
                method,
                evidenceMethod,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        return evidence with
        {
            DirectCallCollectionAttempted = true,
            DirectCallCollectionComplete = complete,
        };
    }

    // Matches Member Body comparison: a declaration that is abstract, a
    // P/Invoke, runtime-provided or internal-call, or an IL declaration without
    // a body (such as an extern UnsafeAccessor) has no body to inspect.
    internal static MethodBodyAvailability ClassifyBodyAvailability(
        MethodDefinition method)
        => (method.Attributes
                & (MethodAttributes.Abstract
                    | MethodAttributes.PinvokeImpl))
                != 0
            || !HasManagedIlBody(method.ImplAttributes)
            || (method.ImplAttributes
                & MethodImplAttributes.InternalCall)
                != 0
            || method.RelativeVirtualAddress == 0
                ? MethodBodyAvailability.NoApplicableInput
                : MethodBodyAvailability.Present;

    // Roslyn emits each extension-block member twice: an implementation on the
    // [Extension] static class, and a declaration copy with a throwing body in
    // a nested SpecialName [Extension] grouping type. Every method of a
    // confirmed grouping type is such a copy. An [ExtensionMarker] method
    // outside that shape is unconfirmed and must not stand as its own member.
    static ExtensionDeclarationShape ClassifyExtensionDeclaration(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method)
    {
        try
        {
            TypeDefinitionHandle enclosing = type.GetDeclaringType();
            bool groupingType =
                !enclosing.IsNil
                && (type.Attributes & TypeAttributes.SpecialName) != 0
                && AttributeReader.HasAttribute(
                    reader,
                    type.GetCustomAttributes(),
                    ExtensionAttributeName)
                && AttributeReader.HasAttribute(
                    reader,
                    reader.GetTypeDefinition(enclosing).GetCustomAttributes(),
                    ExtensionAttributeName);
            if (groupingType)
                return ExtensionDeclarationShape.Skeleton;
            return AttributeReader.TryGetExtensionMarkerName(
                    reader,
                    method.GetCustomAttributes(),
                    out _)
                ? ExtensionDeclarationShape.Unconfirmed
                : ExtensionDeclarationShape.None;
        }
        catch (BadImageFormatException)
        {
            return ExtensionDeclarationShape.Unconfirmed;
        }
    }

    const string ExtensionAttributeName =
        "System.Runtime.CompilerServices.ExtensionAttribute";

    // A compiler-generated type nested inside another type (closure, state
    // machine, or lifted helper container) holds bodies that belong to a
    // source owner; a top-level generated type has none.
    bool IsDeclaredInNestedCompilerGeneratedType(
        MetadataReader reader,
        TypeDefinition type)
    {
        TypeDefinition current = type;
        for (int depth = 0;
            depth < MetadataSafetyPolicy.MaxRelationshipNodes;
            depth++)
        {
            TypeDefinitionHandle enclosing = current.GetDeclaringType();
            if (enclosing.IsNil)
                return false;
            if (_infrastructure.HasCompilerGeneratedAttribute(
                    current.GetCustomAttributes()))
            {
                return true;
            }
            current = reader.GetTypeDefinition(enclosing);
        }
        return true;
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
