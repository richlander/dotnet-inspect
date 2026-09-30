using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// One incoming hierarchy occurrence selected by exact definition name.
/// The target is the request's definition; the row preserves the source and
/// physical declaration needed by later correspondence and projection.
/// </summary>
public readonly record struct MetadataHierarchyRelationAnalysisRow(
    MetadataTypeDefinitionAddress Source,
    MetadataTypeDefinitionName SourceType,
    MetadataHierarchyRelationKind Kind,
    ImmutableArray<int> MetadataTokens);

/// <summary>
/// A resolved forward closing that may stop hierarchy analysis after enough
/// exact-kind candidates have been produced.
/// </summary>
public sealed record MetadataHierarchyRelationForwardPlan
{
    public MetadataHierarchyRelationForwardPlan(int maximumCandidates)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumCandidates);
        MaximumCandidates = maximumCandidates;
    }

    public int MaximumCandidates { get; }
}

public sealed record MetadataHierarchyRelationAnalysisRequest
{
    public MetadataHierarchyRelationAnalysisRequest(
        MetadataHierarchyTargetSelection target,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        bool includeHidden = false,
        bool materializeRows = true,
        MetadataHierarchyRelationForwardPlan? forwardPlan = null)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        IncludeNonPublic = includeNonPublic;
        IncludeHidden = includeHidden;
        MaterializeRows = materializeRows;
        if (forwardPlan is not null && target.Kind is null)
        {
            throw new ArgumentException(
                "Forward hierarchy analysis requires one exact relation kind.",
                nameof(forwardPlan));
        }
        ForwardPlan = forwardPlan;
    }

    public MetadataHierarchyTargetSelection Target { get; }

    public MetadataOperationPolicy Policy { get; }

    public bool IncludeNonPublic { get; }

    public bool IncludeHidden { get; }

    public bool MaterializeRows { get; }

    public MetadataHierarchyRelationForwardPlan? ForwardPlan { get; }
}

public sealed record MetadataHierarchyRelationAnalysisResult(
    MetadataRelationInspectionReceipt Receipt,
    int CandidateCount,
    MetadataHierarchyRelationForwardPlan? ForwardPlan,
    bool WasStopped,
    MetadataRelationFamilyResult<MetadataHierarchyRelationAnalysisRow>
        Relations);

public abstract record MetadataHierarchyRelationAnalysisOutcome
{
    private protected MetadataHierarchyRelationAnalysisOutcome()
    {
    }

    public sealed record Available(
        MetadataHierarchyRelationAnalysisResult Result)
        : MetadataHierarchyRelationAnalysisOutcome;

    public sealed record Rejected(
        MetadataImageFormatResult Format,
        string Detail)
        : MetadataHierarchyRelationAnalysisOutcome;
}

/// <summary>
/// Product-owned result of applying hierarchy scope, target matching, budgets,
/// and optional row projection to one Type definition.
/// </summary>
public readonly record struct MetadataHierarchyRelationAnalysisUnit(
    bool IsExcluded,
    bool BaseMatched,
    bool InterfaceMatched,
    int ForwardCandidateCount,
    MetadataHierarchyRelationAnalysisRow? BaseRelation,
    MetadataHierarchyRelationAnalysisRow? InterfaceRelation,
    MetadataRelationDiagnostic? Diagnostic)
{
    public int CandidateCount =>
        (BaseMatched ? 1 : 0)
        + (InterfaceMatched ? 1 : 0);

    public bool IsUnavailable => Diagnostic is not null;
}

/// <summary>
/// One product-owned hierarchy-analysis pass. LINQ, NLinq, and Planner readers
/// use this pass so only their iteration and closing machinery differs.
/// </summary>
/// <remarks>
/// The source session must remain alive until this pass is disposed.
/// Construction consumes the session's retained admitted metadata reader;
/// producers do not repeat general image-format admission.
/// </remarks>
public sealed class MetadataHierarchyRelationAnalysisPass : IDisposable
{
    readonly MetadataReader _reader;
    readonly MetadataHierarchyRelationAnalysisRequest _request;
    readonly MetadataOperationContext _operation;
    readonly MetadataVisibilityResolver? _visibility;
    readonly bool _ownsOperation;

    public MetadataHierarchyRelationAnalysisPass(
        AssemblyInspectionSession session,
        MetadataHierarchyRelationAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        _request = request
            ?? throw new ArgumentNullException(nameof(request));
        _reader = session.GetAdmittedMetadataReader();
        _operation = new(request.Policy);
        _ownsOperation = true;
        try
        {
            if (_operation.AdmitImage(_reader)
                is MetadataImageAdmissionResult.Rejected rejected)
            {
                throw new InvalidOperationException(
                    "The metadata image exceeds the hierarchy-analysis row "
                        + $"budget ({rejected.Failure.ImageMetadataRows} > "
                        + $"{rejected.Failure.MaxMetadataRows}).");
            }
            _visibility = request.IncludeNonPublic
                ? null
                : new MetadataVisibilityResolver(_reader);
        }
        catch
        {
            _operation.Dispose();
            throw;
        }
    }

    internal MetadataHierarchyRelationAnalysisPass(
        MetadataHierarchyRelationAnalysisRequest request,
        MetadataOperationContext operation,
        MetadataReader reader)
    {
        _request = request
            ?? throw new ArgumentNullException(nameof(request));
        _operation = operation
            ?? throw new ArgumentNullException(nameof(operation));
        _reader = reader
            ?? throw new ArgumentNullException(nameof(reader));
        _visibility = request.IncludeNonPublic
            ? null
            : new MetadataVisibilityResolver(reader);
    }

    public MetadataOperationCounters Counters => _operation.Counters;

    public TypeDefinitionHandleCollection TypeDefinitions =>
        _reader.TypeDefinitions;

    public MetadataHierarchyRelationAnalysisUnit Analyze(
        TypeDefinitionHandle handle)
    {
        _operation.Charge(
            MetadataOperationDimension.DeclarationCandidates);

        try
        {
            TypeDefinition definition =
                _reader.GetTypeDefinition(handle);
            if ((!_request.IncludeNonPublic
                    && !_visibility!.IsExternallyVisible(
                        _reader,
                        handle,
                        _operation))
                || (!_request.IncludeHidden
                    && AttributeReader.HasHiddenAttribute(
                        _reader,
                        definition.GetCustomAttributes())))
            {
                return new(
                    IsExcluded: true,
                    BaseMatched: false,
                    InterfaceMatched: false,
                    ForwardCandidateCount: 0,
                    BaseRelation: null,
                    InterfaceRelation: null,
                    Diagnostic: null);
            }

            bool baseMatched = false;
            bool interfaceMatched = false;
            bool baseForwardCandidate = false;
            bool interfaceForwardCandidate = false;
            int baseToken = 0;
            ImmutableArray<int>.Builder? interfaceTokens = null;
            MetadataHierarchyTargetSelection target =
                _request.Target;

            if (target.Kind
                    is not MetadataHierarchyRelationKind.Interface
                && !definition.BaseType.IsNil)
            {
                baseToken = MetadataTokens.GetToken(handle);
                MetadataRelationDiagnostic? diagnostic =
                    Match(
                        _reader,
                        definition.BaseType,
                        baseToken,
                        out baseMatched,
                        out baseForwardCandidate);
                if (diagnostic is not null)
                    return Unavailable(diagnostic);
            }

            if (target.Kind
                is not MetadataHierarchyRelationKind.BaseType)
            {
                foreach (InterfaceImplementationHandle implementationHandle
                    in definition.GetInterfaceImplementations())
                {
                    _operation.Charge(
                        MetadataOperationDimension
                            .InterfaceImplementationRows);
                    InterfaceImplementation implementation =
                        _reader.GetInterfaceImplementation(
                            implementationHandle);
                    int token =
                        MetadataTokens.GetToken(implementationHandle);
                    MetadataRelationDiagnostic? diagnostic =
                        Match(
                            _reader,
                            implementation.Interface,
                            token,
                            out bool matches,
                            out bool forwardCandidate);
                    if (diagnostic is not null)
                        return Unavailable(diagnostic);
                    if (!matches)
                        continue;

                    interfaceMatched = true;
                    interfaceForwardCandidate |= forwardCandidate;
                    if (_request.MaterializeRows)
                    {
                        _operation.Charge(
                            MetadataOperationDimension.StructuredNodes);
                        (interfaceTokens ??=
                            ImmutableArray.CreateBuilder<int>())
                            .Add(token);
                    }
                }
            }

            if (!baseMatched && !interfaceMatched)
            {
                return new(
                    IsExcluded: false,
                    BaseMatched: false,
                    InterfaceMatched: false,
                    ForwardCandidateCount: 0,
                    BaseRelation: null,
                    InterfaceRelation: null,
                    Diagnostic: null);
            }

            if (!_request.MaterializeRows)
            {
                return new(
                    IsExcluded: false,
                    baseMatched,
                    interfaceMatched,
                    (baseForwardCandidate ? 1 : 0)
                        + (interfaceForwardCandidate ? 1 : 0),
                    BaseRelation: null,
                    InterfaceRelation: null,
                    Diagnostic: null);
            }

            MetadataTypeDefinitionNameReadResult read =
                MetadataTypeDefinitionNameReader.Read(
                    _reader,
                    handle,
                    beforeMaterialize: amount =>
                        _operation.Charge(
                            MetadataOperationDimension.StructuredNodes,
                            amount),
                    chargeChain: amount =>
                        _operation.Charge(
                            MetadataOperationDimension.RelationshipEdges,
                            amount),
                    chargeCharacters: amount =>
                        _operation.Charge(
                            MetadataOperationDimension.RetainedText,
                            amount));
            if (read
                is MetadataTypeDefinitionNameReadResult.Rejected rejected)
            {
                return Unavailable(
                    new(
                        MetadataRelationFamily.Hierarchy,
                        MetadataRelationDiagnosticKind.MalformedMetadata,
                        MetadataTokens.GetToken(handle),
                        rejected.Failure.Detail));
            }
            if (read
                is not MetadataTypeDefinitionNameReadResult.Read source)
            {
                throw new InvalidOperationException(
                    "Unknown metadata Type-name result.");
            }

            MetadataTypeDefinitionAddress address =
                MetadataTypeDefinitionAddress.FromHandle(
                    _reader,
                    handle);
            if (baseMatched)
            {
                _operation.Charge(
                    MetadataOperationDimension.StructuredNodes);
            }
            return new(
                IsExcluded: false,
                baseMatched,
                interfaceMatched,
                (baseForwardCandidate ? 1 : 0)
                    + (interfaceForwardCandidate ? 1 : 0),
                baseMatched
                    ? new(
                        address,
                        source.Name,
                        MetadataHierarchyRelationKind.BaseType,
                        [baseToken])
                    : null,
                interfaceMatched
                    ? new(
                        address,
                        source.Name,
                        MetadataHierarchyRelationKind.Interface,
                        interfaceTokens!.ToImmutable())
                    : null,
                Diagnostic: null);
        }
        catch (Exception exception)
            when (exception
                    is not MetadataVisibilityGraphException
                && exception is
                    (BadImageFormatException
                        or ArgumentException
                        or InvalidOperationException
                        or OverflowException))
        {
            return Unavailable(
                new(
                    MetadataRelationFamily.Hierarchy,
                    MetadataRelationDiagnosticKind.MalformedMetadata,
                    MetadataTokens.GetToken(handle),
                    exception.Message));
        }
    }

    MetadataRelationDiagnostic? Match(
        MetadataReader reader,
        EntityHandle candidateTarget,
        int occurrenceToken,
        out bool matched,
        out bool forwardCandidate)
    {
        _operation.Charge(
            MetadataOperationDimension.RelationshipEdges);
        MetadataTypeDefinitionNameMatchResult match =
            MetadataHierarchyRelationAnalysis.MatchTarget(
                reader,
                candidateTarget,
                _request.Target.Type,
                out string? failure);
        matched =
            match == MetadataTypeDefinitionNameMatchResult.Match;
        forwardCandidate =
            matched
            && DirectlyTargetsAssembly(
                reader,
                candidateTarget,
                _request.Target.Assembly);
        return match
                == MetadataTypeDefinitionNameMatchResult.Rejected
            ? new(
                MetadataRelationFamily.Hierarchy,
                MetadataRelationDiagnosticKind.UnsupportedShape,
                occurrenceToken,
                failure
                    ?? "The hierarchy target definition could not be "
                        + "analyzed safely.")
            : null;
    }

    static MetadataHierarchyRelationAnalysisUnit Unavailable(
        MetadataRelationDiagnostic diagnostic) =>
        new(
            IsExcluded: false,
            BaseMatched: false,
            InterfaceMatched: false,
            ForwardCandidateCount: 0,
            BaseRelation: null,
            InterfaceRelation: null,
            diagnostic);

    static bool DirectlyTargetsAssembly(
        MetadataReader reader,
        EntityHandle target,
        AssemblyReferenceIdentity? expected)
    {
        if (expected is null)
            return true;

        EntityHandle definition = target;
        if (definition.Kind == HandleKind.TypeSpecification)
        {
            BlobReader signature = reader.GetBlobReader(
                reader.GetTypeSpecification(
                    (TypeSpecificationHandle)definition).Signature);
            if (signature.ReadSignatureTypeCode()
                    != SignatureTypeCode.GenericTypeInstance
                || signature.ReadSignatureTypeCode()
                    != SignatureTypeCode.TypeHandle)
            {
                return false;
            }
            definition = signature.ReadTypeHandle();
        }

        if (definition.Kind == HandleKind.TypeDefinition)
        {
            return reader.IsAssembly
                && expected.IsEquivalentTo(
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        reader));
        }
        if (definition.Kind != HandleKind.TypeReference)
            return false;

        EntityHandle scope =
            reader.GetTypeReference(
                (TypeReferenceHandle)definition).ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference)
        {
            scope = reader.GetTypeReference(
                (TypeReferenceHandle)scope).ResolutionScope;
        }
        return scope.Kind switch
        {
            HandleKind.AssemblyReference =>
                expected.IsEquivalentTo(
                    AssemblyReferenceIdentity.From(
                        reader,
                        (AssemblyReferenceHandle)scope)),
            HandleKind.ModuleDefinition =>
                reader.IsAssembly
                && expected.IsEquivalentTo(
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        reader)),
            _ => false,
        };
    }

    public void Dispose()
    {
        if (_ownsOperation)
            _operation.Dispose();
    }
}

/// <summary>
/// Fast definition-level hierarchy analysis. It is exact for the canonical
/// TypeDef, TypeRef, and generic TypeSpec shapes emitted by Roslyn and remains
/// bounded for malformed metadata.
/// </summary>
public static class MetadataHierarchyRelationAnalysis
{
    internal static MetadataTypeDefinitionNameMatchResult MatchTarget(
        MetadataReader reader,
        EntityHandle target,
        MetadataTypeDefinitionName expected) =>
        MatchTarget(reader, target, expected, out _);

    internal static MetadataTypeDefinitionNameMatchResult MatchTarget(
        MetadataReader reader,
        EntityHandle target,
        MetadataTypeDefinitionName expected,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(expected);
        failure = null;

        try
        {
            EntityHandle definition = target;
            if (target.Kind == HandleKind.TypeSpecification)
            {
                BlobReader signature = reader.GetBlobReader(
                    reader.GetTypeSpecification(
                        (TypeSpecificationHandle)target).Signature);
                if (signature.ReadSignatureTypeCode()
                        != SignatureTypeCode.GenericTypeInstance
                    || signature.ReadSignatureTypeCode()
                        != SignatureTypeCode.TypeHandle)
                {
                    failure =
                        "The hierarchy TypeSpec is not a canonical generic instance.";
                    return MetadataTypeDefinitionNameMatchResult.Rejected;
                }

                definition = signature.ReadTypeHandle();
            }

            MetadataTypeNameFailure? nameFailure;
            MetadataTypeDefinitionNameMatchResult result =
                definition.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        MetadataTypeDefinitionName.Matches(
                            reader,
                            (TypeDefinitionHandle)definition,
                            expected,
                            out nameFailure),
                    HandleKind.TypeReference =>
                        MetadataTypeDefinitionName.Matches(
                            reader,
                            (TypeReferenceHandle)definition,
                            expected,
                            out nameFailure),
                    _ => RejectUnsupported(out nameFailure),
                };
            failure = nameFailure?.Detail;
            return result;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            failure = exception.Message;
            return MetadataTypeDefinitionNameMatchResult.Rejected;
        }

        static MetadataTypeDefinitionNameMatchResult RejectUnsupported(
            out MetadataTypeNameFailure? nameFailure)
        {
            nameFailure = null;
            return MetadataTypeDefinitionNameMatchResult.Rejected;
        }
    }
}

internal static partial class MetadataRelationInspection
{
    internal static MetadataHierarchyRelationAnalysisOutcome
        ExecuteHierarchyAnalysis(
            MetadataReader reader,
            MetadataHierarchyRelationAnalysisRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var relationRequest = new MetadataRelationInspectionRequest(
            [MetadataRelationFamily.Hierarchy],
            request.Policy,
            request.IncludeNonPublic,
            hierarchyTarget: request.Target)
        {
            IncludeHidden = request.IncludeHidden,
        };
        using var operation = new MetadataOperationContext(request.Policy);
        MetadataRelationReceiptIdentity receiptIdentity =
            ReadReceiptIdentity(reader, out string? receiptFailure);
        if (receiptIdentity.ModuleVersionId is not Guid)
        {
            var diagnostic = MalformedDiagnostic(
                MetadataRelationFamily.Hierarchy,
                null,
                receiptFailure
                    ?? "The metadata image has no usable module identity.");
            return Available(
                receiptIdentity,
                relationRequest,
                operation,
                0,
                request.ForwardPlan,
                wasStopped: false,
                relations: new(
                    true,
                    MetadataRelationFamilyDisposition.Failed,
                    new(1, 0, 0, 1, 0),
                    [],
                    [diagnostic]));
        }

        MetadataImageAdmissionResult admission =
            operation.AdmitImage(reader);
        if (admission is MetadataImageAdmissionResult.Rejected rejected)
        {
            var diagnostic = new MetadataRelationDiagnostic(
                MetadataRelationFamily.Hierarchy,
                MetadataRelationDiagnosticKind.Limit,
                null,
                "The metadata image exceeds the operation row budget.",
                MetadataOperationDimension.MetadataRows,
                rejected.Failure.MaxMetadataRows,
                rejected.Failure.ImageMetadataRows);
            return Available(
                receiptIdentity,
                relationRequest,
                operation,
                0,
                request.ForwardPlan,
                wasStopped: false,
                relations: new(
                    true,
                    MetadataRelationFamilyDisposition.Partial,
                    new(1, 0, 0, 0, 1),
                    [],
                    [diagnostic]));
        }

        MetadataRelationFamilyResult<MetadataHierarchyRelationAnalysisRow>
            relations = ScanHierarchyAnalysis(
                reader,
                relationRequest,
                operation,
                cancellationToken,
                request.MaterializeRows,
                request.ForwardPlan,
                out int candidateCount,
                out bool wasStopped);
        return Available(
            receiptIdentity,
            relationRequest,
            operation,
            candidateCount,
            request.ForwardPlan,
            wasStopped,
            relations);
    }

    private static MetadataHierarchyRelationAnalysisOutcome Available(
        MetadataRelationReceiptIdentity receiptIdentity,
        MetadataRelationInspectionRequest request,
        MetadataOperationContext operation,
        int candidateCount,
        MetadataHierarchyRelationForwardPlan? forwardPlan,
        bool wasStopped,
        MetadataRelationFamilyResult<MetadataHierarchyRelationAnalysisRow>
            relations) =>
        new MetadataHierarchyRelationAnalysisOutcome.Available(
            new(
                Receipt(receiptIdentity, request, operation),
                candidateCount,
                forwardPlan,
                wasStopped,
                relations));

    private static MetadataRelationFamilyResult<
        MetadataHierarchyRelationAnalysisRow> ScanHierarchyAnalysis(
            MetadataReader reader,
            MetadataRelationInspectionRequest request,
            MetadataOperationContext operation,
            CancellationToken cancellationToken,
            bool materializeRows,
            MetadataHierarchyRelationForwardPlan? forwardPlan,
            out int candidateCount,
            out bool wasStopped)
    {
        var rows =
            ImmutableArray.CreateBuilder<
                MetadataHierarchyRelationAnalysisRow>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        MetadataHierarchyTargetSelection target = request.HierarchyTarget
            ?? throw new ArgumentException(
                "Hierarchy analysis requires an exact target.",
                nameof(request));
        var analysisRequest =
            new MetadataHierarchyRelationAnalysisRequest(
                target,
                request.Policy,
                request.IncludeNonPublic,
                request.IncludeHidden,
                materializeRows,
                forwardPlan);
        int matched = 0;
        int forwardCandidates = 0;
        int considered = 0;
        int excluded = 0;
        int examined = 0;
        int unavailable = 0;
        bool limited = false;
        bool stopped = false;
        int sourceCandidateCount = request.TypeScope.IsEmpty
            ? reader.TypeDefinitions.Count
            : request.TypeScope.Length;

        try
        {
            using var pass =
                new MetadataHierarchyRelationAnalysisPass(
                    analysisRequest,
                    operation,
                    reader);
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.IncludesType(reader, handle))
                    continue;
                considered++;
                MetadataHierarchyRelationAnalysisUnit unit =
                    pass.Analyze(handle);
                if (unit.IsExcluded)
                {
                    excluded++;
                    continue;
                }
                if (unit.IsUnavailable)
                {
                    unavailable++;
                    diagnostics.Add(
                        unit.Diagnostic
                        ?? throw new InvalidOperationException(
                            "Unavailable hierarchy analysis requires a "
                                + "diagnostic."));
                    continue;
                }

                examined++;
                matched = checked(matched + unit.CandidateCount);
                forwardCandidates = checked(
                    forwardCandidates + unit.ForwardCandidateCount);
                if (unit.BaseRelation is { } baseRelation)
                    rows.Add(baseRelation);
                if (unit.InterfaceRelation is { } interfaceRelation)
                    rows.Add(interfaceRelation);
                if (analysisRequest.ForwardPlan is { } forward
                    && forwardCandidates >= forward.MaximumCandidates
                    && considered < sourceCandidateCount)
                {
                    stopped = true;
                    break;
                }
            }
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            limited = true;
            diagnostics.Add(
                LimitDiagnostic(
                    MetadataRelationFamily.Hierarchy,
                    exception));
        }
        catch (BadImageFormatException exception)
        {
            diagnostics.Add(
                MalformedDiagnostic(
                    MetadataRelationFamily.Hierarchy,
                    null,
                    exception.Message));
        }

        int remaining =
            considered - examined - excluded - unavailable;
        if (considered == 0 && diagnostics.Count != 0)
        {
            candidateCount = matched;
            wasStopped = false;
            return CompleteOrPartial(
                rows,
                diagnostics,
                new(1, 0, 0, 1, 0));
        }
        if (!limited)
            unavailable += remaining;
        candidateCount = matched;
        MetadataRelationFamilyResult<
            MetadataHierarchyRelationAnalysisRow> result =
                CompleteOrPartial(
                    rows,
                    diagnostics,
                    new(
                        considered,
                        examined,
                        excluded,
                        unavailable,
                        limited ? remaining : 0));
        if (stopped)
        {
            result = new(
                wasRequested: true,
                MetadataRelationFamilyDisposition.Partial,
                result.Coverage,
                result.Evidence,
                result.Diagnostics);
        }
        wasStopped = stopped;
        return result;
    }
}
