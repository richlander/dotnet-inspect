using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Reader-owned work performed by method-analysis support.
/// </summary>
public sealed record MethodAnalysisSupportWork(
    int MethodReferencesResolved,
    long MethodReferenceSignatureWork,
    int GenericConstraintQueries,
    int StableReceiverQueries,
    int StableGetterBodiesRead,
    long StableGetterEncodedIlBytes,
    int AsyncStateMachineTypeRows)
{
    public int DeclaredSourceQueries { get; init; }

    public int DeclaredSourceCandidateDefinitions { get; init; }

    public int DeclaredSourceProbeBodies { get; init; }

    public long DeclaredSourceProbeEncodedIlBytes { get; init; }

    public int DeclaredSourceTypeDefinitionRows { get; init; }

    public int DeclaredSourceTypeAttributeRows { get; init; }

    public int DeclaredSourceRelationshipNodes { get; init; }

    public int DeclaredSourceContractQueries { get; init; }

    public MemorySafetyMetadataWork
        DeclaredSourceContractConstructionWork { get; init; } =
            MemorySafetyMetadataWork.Empty;

    public MemorySafetyMetadataWork
        DeclaredSourceContractQueryWork { get; init; } =
            MemorySafetyMetadataWork.Empty;

    public int LocalExceptionTypeQueries { get; init; }

    public int LocalExceptionTypeDefinitionRows { get; init; }

    public int LocalExceptionTypeRelationshipNodes { get; init; }

    public long LocalExceptionTypeSignatureBytes { get; init; }

    public int OptimizationSuppressionQueries { get; init; }

    public int OptimizationSuppressionMethodAttributeRows { get; init; }

    public int OptimizationSuppressionTypeDefinitionRows { get; init; }

    public int OptimizationSuppressionTypeAttributeRows { get; init; }

    public int OptimizationSuppressionRelationshipNodes { get; init; }

    public static MethodAnalysisSupportWork Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0);

    public MethodAnalysisSupportWork Since(MethodAnalysisSupportWork before) =>
        new(
            MethodReferencesResolved - before.MethodReferencesResolved,
            MethodReferenceSignatureWork
                - before.MethodReferenceSignatureWork,
            GenericConstraintQueries - before.GenericConstraintQueries,
            StableReceiverQueries - before.StableReceiverQueries,
            StableGetterBodiesRead - before.StableGetterBodiesRead,
            StableGetterEncodedIlBytes
                - before.StableGetterEncodedIlBytes,
            AsyncStateMachineTypeRows
                - before.AsyncStateMachineTypeRows)
        {
            DeclaredSourceQueries =
                DeclaredSourceQueries
                - before.DeclaredSourceQueries,
            DeclaredSourceCandidateDefinitions =
                DeclaredSourceCandidateDefinitions
                - before.DeclaredSourceCandidateDefinitions,
            DeclaredSourceProbeBodies =
                DeclaredSourceProbeBodies
                - before.DeclaredSourceProbeBodies,
            DeclaredSourceProbeEncodedIlBytes =
                DeclaredSourceProbeEncodedIlBytes
                - before.DeclaredSourceProbeEncodedIlBytes,
            DeclaredSourceTypeDefinitionRows =
                DeclaredSourceTypeDefinitionRows
                - before.DeclaredSourceTypeDefinitionRows,
            DeclaredSourceTypeAttributeRows =
                DeclaredSourceTypeAttributeRows
                - before.DeclaredSourceTypeAttributeRows,
            DeclaredSourceRelationshipNodes =
                DeclaredSourceRelationshipNodes
                - before.DeclaredSourceRelationshipNodes,
            DeclaredSourceContractQueries =
                DeclaredSourceContractQueries
                - before.DeclaredSourceContractQueries,
            DeclaredSourceContractConstructionWork =
                DeclaredSourceContractConstructionWork.Since(
                    before.DeclaredSourceContractConstructionWork),
            DeclaredSourceContractQueryWork =
                DeclaredSourceContractQueryWork.Since(
                    before.DeclaredSourceContractQueryWork),
            LocalExceptionTypeQueries =
                LocalExceptionTypeQueries
                - before.LocalExceptionTypeQueries,
            LocalExceptionTypeDefinitionRows =
                LocalExceptionTypeDefinitionRows
                - before.LocalExceptionTypeDefinitionRows,
            LocalExceptionTypeRelationshipNodes =
                LocalExceptionTypeRelationshipNodes
                - before.LocalExceptionTypeRelationshipNodes,
            LocalExceptionTypeSignatureBytes =
                LocalExceptionTypeSignatureBytes
                - before.LocalExceptionTypeSignatureBytes,
            OptimizationSuppressionQueries =
                OptimizationSuppressionQueries
                - before.OptimizationSuppressionQueries,
            OptimizationSuppressionMethodAttributeRows =
                OptimizationSuppressionMethodAttributeRows
                - before.OptimizationSuppressionMethodAttributeRows,
            OptimizationSuppressionTypeDefinitionRows =
                OptimizationSuppressionTypeDefinitionRows
                - before.OptimizationSuppressionTypeDefinitionRows,
            OptimizationSuppressionTypeAttributeRows =
                OptimizationSuppressionTypeAttributeRows
                - before.OptimizationSuppressionTypeAttributeRows,
            OptimizationSuppressionRelationshipNodes =
                OptimizationSuppressionRelationshipNodes
                - before.OptimizationSuppressionRelationshipNodes,
        };

    public MethodAnalysisSupportWork Plus(MethodAnalysisSupportWork other) =>
        new(
            checked(MethodReferencesResolved
                + other.MethodReferencesResolved),
            checked(MethodReferenceSignatureWork
                + other.MethodReferenceSignatureWork),
            checked(GenericConstraintQueries
                + other.GenericConstraintQueries),
            checked(StableReceiverQueries
                + other.StableReceiverQueries),
            checked(StableGetterBodiesRead
                + other.StableGetterBodiesRead),
            checked(StableGetterEncodedIlBytes
                + other.StableGetterEncodedIlBytes),
            checked(AsyncStateMachineTypeRows
                + other.AsyncStateMachineTypeRows))
        {
            DeclaredSourceQueries =
                checked(DeclaredSourceQueries
                    + other.DeclaredSourceQueries),
            DeclaredSourceCandidateDefinitions =
                checked(DeclaredSourceCandidateDefinitions
                    + other.DeclaredSourceCandidateDefinitions),
            DeclaredSourceProbeBodies =
                checked(DeclaredSourceProbeBodies
                    + other.DeclaredSourceProbeBodies),
            DeclaredSourceProbeEncodedIlBytes =
                checked(DeclaredSourceProbeEncodedIlBytes
                    + other.DeclaredSourceProbeEncodedIlBytes),
            DeclaredSourceTypeDefinitionRows =
                checked(DeclaredSourceTypeDefinitionRows
                    + other.DeclaredSourceTypeDefinitionRows),
            DeclaredSourceTypeAttributeRows =
                checked(DeclaredSourceTypeAttributeRows
                    + other.DeclaredSourceTypeAttributeRows),
            DeclaredSourceRelationshipNodes =
                checked(DeclaredSourceRelationshipNodes
                    + other.DeclaredSourceRelationshipNodes),
            DeclaredSourceContractQueries =
                checked(DeclaredSourceContractQueries
                    + other.DeclaredSourceContractQueries),
            DeclaredSourceContractConstructionWork =
                DeclaredSourceContractConstructionWork.Plus(
                    other.DeclaredSourceContractConstructionWork),
            DeclaredSourceContractQueryWork =
                DeclaredSourceContractQueryWork.Plus(
                    other.DeclaredSourceContractQueryWork),
            LocalExceptionTypeQueries =
                checked(LocalExceptionTypeQueries
                    + other.LocalExceptionTypeQueries),
            LocalExceptionTypeDefinitionRows =
                checked(LocalExceptionTypeDefinitionRows
                    + other.LocalExceptionTypeDefinitionRows),
            LocalExceptionTypeRelationshipNodes =
                checked(LocalExceptionTypeRelationshipNodes
                    + other.LocalExceptionTypeRelationshipNodes),
            LocalExceptionTypeSignatureBytes =
                checked(LocalExceptionTypeSignatureBytes
                    + other.LocalExceptionTypeSignatureBytes),
            OptimizationSuppressionQueries =
                checked(OptimizationSuppressionQueries
                    + other.OptimizationSuppressionQueries),
            OptimizationSuppressionMethodAttributeRows =
                checked(OptimizationSuppressionMethodAttributeRows
                    + other.OptimizationSuppressionMethodAttributeRows),
            OptimizationSuppressionTypeDefinitionRows =
                checked(OptimizationSuppressionTypeDefinitionRows
                    + other.OptimizationSuppressionTypeDefinitionRows),
            OptimizationSuppressionTypeAttributeRows =
                checked(OptimizationSuppressionTypeAttributeRows
                    + other.OptimizationSuppressionTypeAttributeRows),
            OptimizationSuppressionRelationshipNodes =
                checked(OptimizationSuppressionRelationshipNodes
                    + other.OptimizationSuppressionRelationshipNodes),
        };
}

/// <summary>
/// Authenticated declared owner of one exact physical MethodDef.
/// </summary>
public sealed record ExactMethodDeclaredSource(
    MethodIdentity? Owner,
    bool SuppressesOpportunities,
    bool IsAuthenticated,
    string? Failure);

/// <summary>
/// Exact method-analysis support work in one Method source receipt.
/// </summary>
public sealed record MethodAnalysisSupportCoverage(
    bool Constructed,
    MethodAnalysisSupportWork QueryWork,
    int Queries,
    MethodDefinitionHandleCoverage QueryingMethods)
{
    public static MethodAnalysisSupportCoverage Empty { get; } =
        new(
            Constructed: false,
            MethodAnalysisSupportWork.Empty,
            Queries: 0,
            MethodDefinitionHandleCoverage.Empty);
}

/// <summary>
/// The existing reader-backed judgments allocation and optimization analyses
/// share, composed without a Library Body Analysis builder.
/// </summary>
internal sealed class MethodAnalysisLookup
{
    readonly MetadataReader _reader;
    int _methodReferencesResolved;
    long _methodReferenceSignatureWork;
    int _genericConstraintQueries;
    int _stableReceiverQueries;
    int _stableGetterBodiesRead;
    long _stableGetterEncodedIlBytes;
    int _asyncStateMachineTypeRows;
    int _declaredSourceQueries;
    int _declaredSourceTypeDefinitionRows;
    int _declaredSourceTypeAttributeRows;
    int _declaredSourceContractQueries;
    MemorySafetyMetadataWork
        _declaredSourceContractConstructionWork =
            MemorySafetyMetadataWork.Empty;
    MemorySafetyMetadataWork
        _declaredSourceContractQueryWork =
            MemorySafetyMetadataWork.Empty;
    int _localExceptionTypeQueries;
    int _localExceptionTypeDefinitionRows;
    int _localExceptionTypeRelationshipNodes;
    long _localExceptionTypeSignatureBytes;
    int _optimizationSuppressionQueries;
    int _optimizationSuppressionMethodAttributeRows;
    int _optimizationSuppressionTypeDefinitionRows;
    int _optimizationSuppressionTypeAttributeRows;
    int _optimizationSuppressionRelationshipNodes;
    readonly object _supportWorkGate = new();
    readonly MemorySafetyContractSupport? _memorySafety;
    readonly string _path;
    readonly PEReader _peReader;
    readonly MethodDefinitionGeneratedExpansionWork
        _declaredSourceWork =
            new(MethodDefinitionGeneratedExpansionLimits.Default);
    DeclaredSourceAttribution? _declaredSourceAttribution;

    internal MethodAnalysisLookup(
        string path,
        MetadataReader reader,
        PEReader peReader,
        Action<MethodDefinitionHandle>? stableReceiverGetterClassified = null,
        Action<MethodDefinitionHandle, int>? methodReferenceResolved = null,
        Action? asyncStateMachineTypesBuilt = null,
        MemorySafetyContractSupport? memorySafety = null,
        bool exactLocalTypeEvidence = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(peReader);

        _path = path;
        _peReader = peReader;
        _reader = reader;
        _memorySafety = memorySafety;
        AssemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(path);
        Mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        MethodReferenceResolver =
            new(
                reader,
                (method, operandToken) =>
                {
                    Interlocked.Increment(
                        ref _methodReferencesResolved);
                    methodReferenceResolved?.Invoke(
                        method,
                        operandToken);
                },
                work => Interlocked.Add(
                    ref _methodReferenceSignatureWork,
                    work));
        GenericConstraintClassifier =
            new(reader);
        StableReceiverGetterClassifier =
            new(
                reader,
                peReader,
                stableReceiverGetterClassified,
                (_, encodedIlBytes) =>
                {
                    Interlocked.Increment(
                        ref _stableGetterBodiesRead);
                    Interlocked.Add(
                        ref _stableGetterEncodedIlBytes,
                        encodedIlBytes);
                });
        PrimaryMetadataResolver =
            new(
                reader,
                AssemblyName,
                Mvid,
                MethodReferenceResolver.ResolveMethod,
                MethodReferenceResolver.ResolvePresenceMethod,
                GenericParameterCanBeValueType,
                IsStableReceiverGetter,
                () =>
                {
                    Interlocked.Add(
                        ref _asyncStateMachineTypeRows,
                        _reader.TypeDefinitions.Count);
                    asyncStateMachineTypesBuilt?.Invoke();
                },
                methodIdentityUnsafeMode:
                    memorySafety is null
                        ? null
                        : ResolveMethodIdentityUnsafeMode,
                exactLocalTypeEvidence,
                localExceptionTypeQueried: () =>
                    Interlocked.Increment(
                        ref _localExceptionTypeQueries),
                localExceptionTypeDefinitionRead: () =>
                    Interlocked.Increment(
                        ref _localExceptionTypeDefinitionRows),
                localExceptionTypeRelationshipRead: () =>
                    Interlocked.Increment(
                        ref _localExceptionTypeRelationshipNodes),
                localExceptionTypeSignatureRead: bytes =>
                    Interlocked.Add(
                        ref _localExceptionTypeSignatureBytes,
                        bytes));
    }

    internal string AssemblyName { get; }

    internal Guid Mvid { get; }

    internal LibraryBodyMethodReferenceResolver MethodReferenceResolver
    { get; }

    internal LibraryBodyGenericConstraintClassifier
        GenericConstraintClassifier
    { get; }

    internal LibraryBodyStableReceiverGetterClassifier
        StableReceiverGetterClassifier
    { get; }

    internal LibraryBodyPrimaryMetadataResolver PrimaryMetadataResolver
    { get; }

    internal MethodAnalysisSupportWork RecordedWork
    {
        get
        {
            MethodDefinitionGeneratedExpansionCoverage declaredSource =
                _declaredSourceWork.Build();
            MemorySafetyMetadataWork contractConstruction;
            MemorySafetyMetadataWork contractQueries;
            lock (_supportWorkGate)
            {
                contractConstruction =
                    _declaredSourceContractConstructionWork;
                contractQueries =
                    _declaredSourceContractQueryWork;
            }
            return new(
                Volatile.Read(ref _methodReferencesResolved),
                Interlocked.Read(ref _methodReferenceSignatureWork),
                Volatile.Read(ref _genericConstraintQueries),
                Volatile.Read(ref _stableReceiverQueries),
                Volatile.Read(ref _stableGetterBodiesRead),
                Interlocked.Read(ref _stableGetterEncodedIlBytes),
                Volatile.Read(ref _asyncStateMachineTypeRows))
            {
                DeclaredSourceQueries =
                    Volatile.Read(ref _declaredSourceQueries),
                DeclaredSourceCandidateDefinitions =
                    declaredSource.CandidateDefinitionsExamined.Count,
                DeclaredSourceProbeBodies =
                    declaredSource.ProbeBodiesAttempted.Count,
                DeclaredSourceProbeEncodedIlBytes =
                    declaredSource.ProbeEncodedIlBytes,
                DeclaredSourceTypeDefinitionRows =
                    Volatile.Read(
                        ref _declaredSourceTypeDefinitionRows),
                DeclaredSourceTypeAttributeRows =
                    Volatile.Read(
                        ref _declaredSourceTypeAttributeRows),
                DeclaredSourceRelationshipNodes =
                    declaredSource.RelationshipNodes,
                DeclaredSourceContractQueries =
                    Volatile.Read(
                        ref _declaredSourceContractQueries),
                DeclaredSourceContractConstructionWork =
                    contractConstruction,
                DeclaredSourceContractQueryWork =
                    contractQueries,
                LocalExceptionTypeQueries =
                    Volatile.Read(
                        ref _localExceptionTypeQueries),
                LocalExceptionTypeDefinitionRows =
                    Volatile.Read(
                        ref _localExceptionTypeDefinitionRows),
                LocalExceptionTypeRelationshipNodes =
                    Volatile.Read(
                        ref _localExceptionTypeRelationshipNodes),
                LocalExceptionTypeSignatureBytes =
                    Interlocked.Read(
                        ref _localExceptionTypeSignatureBytes),
                OptimizationSuppressionQueries =
                    Volatile.Read(
                        ref _optimizationSuppressionQueries),
                OptimizationSuppressionMethodAttributeRows =
                    Volatile.Read(
                        ref _optimizationSuppressionMethodAttributeRows),
                OptimizationSuppressionTypeDefinitionRows =
                    Volatile.Read(
                        ref _optimizationSuppressionTypeDefinitionRows),
                OptimizationSuppressionTypeAttributeRows =
                    Volatile.Read(
                        ref _optimizationSuppressionTypeAttributeRows),
                OptimizationSuppressionRelationshipNodes =
                    Volatile.Read(
                        ref _optimizationSuppressionRelationshipNodes),
            };
        }
    }

    CallerUnsafeMode ResolveMethodIdentityUnsafeMode(
        MethodDefinitionHandle method)
    {
        MemorySafetyContractSupport support =
            _memorySafety ?? throw new InvalidOperationException(
                "Exact declared-source identity requires memory-safety "
                    + "contract support.");
        MemorySafetyContractLookup lookup =
            support.Acquire(out bool constructed);
        MemorySafetyMetadataWork before =
            lookup.RecordedWork;
        try
        {
            return CallerUnsafeModeFacts.FromContract(
                lookup.GetMemberContract(method));
        }
        finally
        {
            MemorySafetyMetadataWork queryWork =
                lookup.RecordedWork.Since(before);
            lock (_supportWorkGate)
            {
                if (constructed)
                {
                    _declaredSourceContractConstructionWork =
                        _declaredSourceContractConstructionWork.Plus(
                            lookup.ConstructionWork);
                }
                _declaredSourceContractQueryWork =
                    _declaredSourceContractQueryWork.Plus(
                        queryWork);
            }
            Interlocked.Increment(
                ref _declaredSourceContractQueries);
        }
    }

    internal ILibraryMethodAnalysisResolver CreateResolver(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition,
        MethodIdentity caller,
        MethodInstructions instructions) =>
        PrimaryMetadataResolver.CreateMethodAnalysisResolver(
            PrimaryMetadataResolver.CreateScope(
                typeDefinition,
                methodDefinition),
            caller,
            instructions);

    internal ExactMethodDeclaredSource ResolveDeclaredSource(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodIdentity method)
    {
        Interlocked.Increment(
            ref _declaredSourceQueries);
        DeclaredSourceAttribution attribution =
            _declaredSourceAttribution
            ??= new(
                _path,
                _reader,
                _peReader,
                generatedExpansionWork:
                    _declaredSourceWork,
                methodAnalysis: this,
                sourceGeneratedTypeDefinitionRead:
                    _ => Interlocked.Increment(
                        ref _declaredSourceTypeDefinitionRows),
                sourceGeneratedAttributeRead:
                    () => Interlocked.Increment(
                        ref _declaredSourceTypeAttributeRows),
                sourceGeneratedRelationshipRead:
                    handle => _declaredSourceWork
                        .RecordRelationshipNode(handle));
        DeclaredOwnerResolution resolution;
        AuthenticatedSourceOwner? ultimateOwner;
        try
        {
            bool typeSourceGenerated =
                attribution.GeneratedProvenanceClassifier
                    .IsSourceGeneratedTypeOrEnclosing(
                        methodDefinition.GetDeclaringType());
            resolution =
                attribution.DeclaredSourceResolver
                    .ResolveUltimateDeclaredMethod(
                        methodHandle,
                        methodDefinition,
                        method,
                        typeSourceGenerated,
                        out _,
                        out ultimateOwner,
                        exactAttribution: true,
                        exactAttributionWork:
                            _declaredSourceWork);
        }
        catch (MethodDefinitionGeneratedExpansionBoundException)
        {
            _declaredSourceWork.Settle();
            throw;
        }
        return resolution switch
        {
            DeclaredOwnerResolution.Resolved
                when ultimateOwner is { } owner =>
                new(
                    owner.Method,
                    owner.SuppressesOpportunities,
                    IsAuthenticated: true,
                    Failure: null),
            DeclaredOwnerResolution.None =>
                new(
                    Owner: null,
                    SuppressesOpportunities: false,
                    IsAuthenticated: true,
                    Failure: null),
            _ => new(
                Owner: null,
                SuppressesOpportunities: true,
                IsAuthenticated: false,
                Failure:
                    $"Declared owner resolution was {resolution}."),
        };
    }

    internal bool SuppressOptimizationOpportunities(
        MethodDefinition methodDefinition,
        TypeDefinitionHandle typeHandle,
        MethodIdentity method)
    {
        Interlocked.Increment(
            ref _optimizationSuppressionQueries);
        CustomAttributeHandleCollection attributes =
            methodDefinition.GetCustomAttributes();
        if (LibraryBodyPrimaryMetadataResolver
                .HasGeneratedCodeAttribute(
                    _reader,
                    attributes,
                    RecordMethodSuppressionAttribute)
            || !CompilerGeneratedNames.IsLocalFunctionOrLambda(
                    method.Name)
                && LibraryBodyPrimaryMetadataResolver
                    .HasCompilerGeneratedAttribute(
                        _reader,
                        attributes,
                        RecordMethodSuppressionAttribute)
            || LibraryMethodAnalysisRunner.IsBlazorRenderMethod(
                method))
        {
            return true;
        }

        TypeDefinitionHandle current = typeHandle;
        int remaining =
            MetadataSafetyPolicy.MaxRelationshipNodes;
        while (!current.IsNil && remaining-- > 0)
        {
            Interlocked.Increment(
                ref _optimizationSuppressionTypeDefinitionRows);
            TypeDefinition type =
                _reader.GetTypeDefinition(current);
            if (LibraryBodyPrimaryMetadataResolver
                .HasGeneratedCodeAttribute(
                    _reader,
                    type.GetCustomAttributes(),
                    RecordTypeSuppressionAttribute))
            {
                return true;
            }

            Interlocked.Increment(
                ref _optimizationSuppressionRelationshipNodes);
            current = type.GetDeclaringType();
        }

        return !current.IsNil;
    }

    bool GenericParameterCanBeValueType(
        TypeRef genericParameter,
        MethodIdentity caller)
    {
        Interlocked.Increment(ref _genericConstraintQueries);
        return GenericConstraintClassifier.GenericParameterCanBeValueType(
            genericParameter,
            caller);
    }

    bool IsStableReceiverGetter(DecodedInstruction instruction)
    {
        Interlocked.Increment(ref _stableReceiverQueries);
        return StableReceiverGetterClassifier.IsStableReceiverGetter(
            instruction);
    }

    void RecordMethodSuppressionAttribute() =>
        Interlocked.Increment(
            ref _optimizationSuppressionMethodAttributeRows);

    void RecordTypeSuppressionAttribute() =>
        Interlocked.Increment(
            ref _optimizationSuppressionTypeAttributeRows);
}

/// <summary>
/// Declared execution-scoped support that constructs its lookup at the first
/// query, so a declaring execution that never queries builds nothing.
/// </summary>
internal sealed class MethodAnalysisSupport(
    string path,
    MetadataReader reader,
    PEReader peReader,
    MemorySafetyContractSupport? memorySafety = null)
{
    MethodAnalysisLookup? _lookup;

    public MethodAnalysisLookup Acquire(out bool constructed)
    {
        constructed = _lookup is null;
        return _lookup ??= new(
            path,
            reader,
            peReader,
            memorySafety: memorySafety,
            exactLocalTypeEvidence: memorySafety is not null);
    }
}
