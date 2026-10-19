using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;

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
                - before.AsyncStateMachineTypeRows);

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
                + other.AsyncStateMachineTypeRows));
}

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

    internal MethodAnalysisLookup(
        string path,
        MetadataReader reader,
        PEReader peReader,
        Action<MethodDefinitionHandle>? stableReceiverGetterClassified = null,
        Action<MethodDefinitionHandle, int>? methodReferenceResolved = null,
        Action? asyncStateMachineTypesBuilt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(peReader);

        _reader = reader;
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
                });
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

    internal MethodAnalysisSupportWork RecordedWork =>
        new(
            Volatile.Read(ref _methodReferencesResolved),
            Interlocked.Read(ref _methodReferenceSignatureWork),
            Volatile.Read(ref _genericConstraintQueries),
            Volatile.Read(ref _stableReceiverQueries),
            Volatile.Read(ref _stableGetterBodiesRead),
            Interlocked.Read(ref _stableGetterEncodedIlBytes),
            Volatile.Read(ref _asyncStateMachineTypeRows));

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
    PEReader peReader)
{
    MethodAnalysisLookup? _lookup;

    public MethodAnalysisLookup Acquire(out bool constructed)
    {
        constructed = _lookup is null;
        return _lookup ??= new(path, reader, peReader);
    }
}
