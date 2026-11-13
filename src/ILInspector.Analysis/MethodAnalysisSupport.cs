using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>Reader-owned work performed by scoped method analysis.</summary>
public sealed record MethodAnalysisResolverWork(
    int MethodReferencesResolved,
    long MethodReferenceSignatureWork,
    int GenericConstraintQueries,
    int StableReceiverQueries,
    int StableGetterBodiesRead,
    long StableGetterEncodedIlBytes,
    int AsyncStateMachineTypeRows)
{
    public int LocalExceptionTypeQueries { get; init; }

    public int LocalExceptionTypeDefinitionRows { get; init; }

    public int LocalExceptionTypeRelationshipNodes { get; init; }

    public long LocalExceptionTypeSignatureBytes { get; init; }

    public static MethodAnalysisResolverWork Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0);

    public MethodAnalysisResolverWork Since(
        MethodAnalysisResolverWork before) =>
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
        };

    public MethodAnalysisResolverWork Plus(
        MethodAnalysisResolverWork other) =>
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
        };
}

/// <summary>Reader-owned work performed by exact declared-source support.</summary>
public sealed record ExactDeclaredSourceWork(
    int CandidateDefinitions,
    int ProbeBodies,
    long ProbeEncodedIlBytes,
    int TypeDefinitionRows,
    int TypeAttributeRows,
    int RelationshipNodes,
    int ContractQueries,
    MemorySafetyMetadataWork ContractConstructionWork,
    MemorySafetyMetadataWork ContractQueryWork)
{
    public static ExactDeclaredSourceWork Empty { get; } =
        new(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            MemorySafetyMetadataWork.Empty,
            MemorySafetyMetadataWork.Empty);

    public ExactDeclaredSourceWork Since(ExactDeclaredSourceWork before) =>
        new(
            CandidateDefinitions - before.CandidateDefinitions,
            ProbeBodies - before.ProbeBodies,
            ProbeEncodedIlBytes - before.ProbeEncodedIlBytes,
            TypeDefinitionRows - before.TypeDefinitionRows,
            TypeAttributeRows - before.TypeAttributeRows,
            RelationshipNodes - before.RelationshipNodes,
            ContractQueries - before.ContractQueries,
            ContractConstructionWork.Since(
                before.ContractConstructionWork),
            ContractQueryWork.Since(before.ContractQueryWork));

    public ExactDeclaredSourceWork Plus(ExactDeclaredSourceWork other) =>
        new(
            checked(CandidateDefinitions + other.CandidateDefinitions),
            checked(ProbeBodies + other.ProbeBodies),
            checked(ProbeEncodedIlBytes + other.ProbeEncodedIlBytes),
            checked(TypeDefinitionRows + other.TypeDefinitionRows),
            checked(TypeAttributeRows + other.TypeAttributeRows),
            checked(RelationshipNodes + other.RelationshipNodes),
            checked(ContractQueries + other.ContractQueries),
            ContractConstructionWork.Plus(
                other.ContractConstructionWork),
            ContractQueryWork.Plus(other.ContractQueryWork));
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
public sealed record MethodAnalysisResolverCoverage(
    MethodAnalysisResolverWork QueryWork,
    int Queries,
    MethodDefinitionHandleCoverage QueryingMethods)
{
    public static MethodAnalysisResolverCoverage Empty { get; } =
        new(
            MethodAnalysisResolverWork.Empty,
            Queries: 0,
            MethodDefinitionHandleCoverage.Empty);
}

public sealed record ExactDeclaredSourceCoverage(
    ExactDeclaredSourceWork QueryWork,
    int Queries,
    MethodDefinitionHandleCoverage QueryingMethods)
{
    public static ExactDeclaredSourceCoverage Empty { get; } =
        new(
            ExactDeclaredSourceWork.Empty,
            Queries: 0,
            MethodDefinitionHandleCoverage.Empty);
}

public sealed record MethodGeneratedCodeEvidenceCoverage(
    MethodGeneratedCodeEvidenceWork QueryWork,
    int Queries,
    MethodDefinitionHandleCoverage QueryingMethods)
{
    public static MethodGeneratedCodeEvidenceCoverage Empty { get; } =
        new(
            MethodGeneratedCodeEvidenceWork.Empty,
            Queries: 0,
            MethodDefinitionHandleCoverage.Empty);
}

/// <summary>
/// Concern-specific method-analysis support work in one Method source receipt.
/// </summary>
public sealed record MethodAnalysisSupportCoverage(
    bool Constructed,
    MethodAnalysisResolverCoverage Resolver,
    ExactDeclaredSourceCoverage DeclaredSource,
    MethodGeneratedCodeEvidenceCoverage GeneratedCodeEvidence)
{
    public static MethodAnalysisSupportCoverage Empty { get; } =
        new(
            Constructed: false,
            MethodAnalysisResolverCoverage.Empty,
            ExactDeclaredSourceCoverage.Empty,
            MethodGeneratedCodeEvidenceCoverage.Empty);
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
    readonly object _supportWorkGate = new();
    readonly MemorySafetyContractSupport? _memorySafety;
    readonly MethodGeneratedCodeEvidenceLookup _generatedCodeEvidence;
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
        _generatedCodeEvidence = new(reader);
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

    internal MethodAnalysisResolverWork ResolverWork =>
        new(
            Volatile.Read(ref _methodReferencesResolved),
            Interlocked.Read(ref _methodReferenceSignatureWork),
            Volatile.Read(ref _genericConstraintQueries),
            Volatile.Read(ref _stableReceiverQueries),
            Volatile.Read(ref _stableGetterBodiesRead),
            Interlocked.Read(ref _stableGetterEncodedIlBytes),
            Volatile.Read(ref _asyncStateMachineTypeRows))
        {
            LocalExceptionTypeQueries =
                Volatile.Read(ref _localExceptionTypeQueries),
            LocalExceptionTypeDefinitionRows =
                Volatile.Read(
                    ref _localExceptionTypeDefinitionRows),
            LocalExceptionTypeRelationshipNodes =
                Volatile.Read(
                    ref _localExceptionTypeRelationshipNodes),
            LocalExceptionTypeSignatureBytes =
                Interlocked.Read(
                    ref _localExceptionTypeSignatureBytes),
        };

    internal ExactDeclaredSourceWork DeclaredSourceWork
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
                declaredSource.CandidateDefinitionsExamined.Count,
                declaredSource.ProbeBodiesAttempted.Count,
                declaredSource.ProbeEncodedIlBytes,
                Volatile.Read(
                    ref _declaredSourceTypeDefinitionRows),
                Volatile.Read(
                    ref _declaredSourceTypeAttributeRows),
                declaredSource.RelationshipNodes,
                Volatile.Read(ref _declaredSourceContractQueries),
                contractConstruction,
                contractQueries);
        }
    }

    internal MethodGeneratedCodeEvidenceWork GeneratedCodeEvidenceWork =>
        _generatedCodeEvidence.RecordedWork;

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
        DeclaredSourceAttribution attribution =
            _declaredSourceAttribution
            ??= new(
                _path,
                _reader,
                _peReader,
                generatedExpansionWork:
                    _declaredSourceWork,
                methodAnalysis: this,
                declaredSourceTypeDefinitionRead:
                    _ => Interlocked.Increment(
                        ref _declaredSourceTypeDefinitionRows),
                declaredSourceAttributeRead:
                    () => Interlocked.Increment(
                        ref _declaredSourceTypeAttributeRows),
                declaredSourceRelationshipRead:
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

    internal bool MethodHasGeneratedCodeAttribute(
        MethodDefinition methodDefinition) =>
        _generatedCodeEvidence.MethodHasGeneratedCodeAttribute(
            methodDefinition);

    internal bool MethodHasCompilerGeneratedAttribute(
        MethodDefinition methodDefinition) =>
        _generatedCodeEvidence.MethodHasCompilerGeneratedAttribute(
            methodDefinition);

    internal DeclaringTypeGeneratedCodeEvidence
        ReadDeclaringTypeGeneratedCodeEvidence(
            TypeDefinitionHandle typeHandle) =>
        _generatedCodeEvidence.ReadDeclaringTypeEvidence(typeHandle);

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
