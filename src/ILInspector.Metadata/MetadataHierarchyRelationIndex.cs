using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

public sealed record MetadataHierarchyRelationIndexReceipt(
    Guid? ModuleVersionId,
    AssemblyReferenceIdentity? Assembly,
    int SourceTypeCount,
    int PhysicalRelationCount,
    int IndexedTargetCount,
    MetadataRelationFamilyDisposition Disposition,
    MetadataOperationCounters Counters,
    ImmutableArray<MetadataRelationDiagnostic> Diagnostics);

public abstract record MetadataHierarchyRelationIndexPreparation
{
    private protected MetadataHierarchyRelationIndexPreparation()
    {
    }

    public sealed record Ready(MetadataHierarchyRelationIndex Index)
        : MetadataHierarchyRelationIndexPreparation;

    public sealed record Failed(
        MetadataHierarchyRelationIndexReceipt Receipt)
        : MetadataHierarchyRelationIndexPreparation;

    public sealed record Rejected(
        MetadataImageFormatResult Format,
        string Detail)
        : MetadataHierarchyRelationIndexPreparation;
}

/// <summary>
/// A target-independent hierarchy census and reverse lookup prepared over one
/// admitted immutable image.
/// </summary>
/// <remarks>
/// The issuing <see cref="AssemblyInspectionSession"/> must remain alive.
/// Preparation retains physical hierarchy facts; terminal execution applies
/// request policy and materializes source names only when Rows require them.
/// </remarks>
public sealed class MetadataHierarchyRelationIndex
{
    public const int DefaultMaximumRetainedRelations = 1_000_000;

    readonly AssemblyInspectionSession _session;
    readonly MetadataHierarchyRelationIndexData _data;

    internal MetadataHierarchyRelationIndex(
        AssemblyInspectionSession session,
        MetadataHierarchyRelationIndexData data)
    {
        _session = session;
        _data = data;
    }

    public MetadataHierarchyRelationIndexReceipt Receipt =>
        _data.Receipt;

    public MetadataHierarchyRelationAnalysisOutcome Analyze(
        MetadataHierarchyRelationAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return _session.SnapshotOperation(
            this,
            access => Analyze(
                access,
                request,
                cancellationToken));
    }

    static MetadataHierarchyRelationAnalysisOutcome Analyze(
        scoped AssemblyInspectionOperationAccess<
            MetadataHierarchyRelationIndex> access,
        MetadataHierarchyRelationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        MetadataHierarchyRelationIndexData data =
            access.Operation._data;
        return access.InspectImage(
            peReader => MetadataRelationInspection.ExecuteHierarchyIndex(
                peReader.GetMetadataReader(),
                data,
                request,
                cancellationToken));
    }
}

internal readonly record struct MetadataHierarchyRelationIndexKey(
    MetadataTypeDefinitionName Target,
    MetadataHierarchyRelationKind Kind);

internal readonly record struct MetadataHierarchyRelationIndexSource(
    TypeDefinitionHandle Handle,
    bool? IsExternallyVisible,
    string? VisibilityFailure,
    bool? IsHidden,
    string? HiddenFailure);

internal readonly record struct MetadataHierarchyRelationIndexCandidate(
    int SourceIndex,
    MetadataHierarchyRelationKind Kind,
    ImmutableArray<int> OccurrenceTokens);

internal sealed class MetadataHierarchyRelationIndexData
{
    internal MetadataHierarchyRelationIndexData(
        MetadataHierarchyRelationIndexReceipt receipt,
        ImmutableArray<MetadataHierarchyRelationIndexSource> sources,
        ImmutableArray<MetadataRelationDiagnostic> globalDiagnostics,
        ImmutableDictionary<
            MetadataHierarchyRelationIndexKey,
            ImmutableArray<MetadataHierarchyRelationIndexCandidate>> targets)
    {
        Receipt = receipt;
        Sources = sources;
        GlobalDiagnostics = globalDiagnostics;
        Targets = targets;
    }

    internal MetadataHierarchyRelationIndexReceipt Receipt { get; }

    internal ImmutableArray<MetadataHierarchyRelationIndexSource>
        Sources { get; }

    internal ImmutableArray<MetadataRelationDiagnostic>
        GlobalDiagnostics { get; }

    internal ImmutableDictionary<
        MetadataHierarchyRelationIndexKey,
        ImmutableArray<MetadataHierarchyRelationIndexCandidate>> Targets
    { get; }
}

internal static partial class MetadataRelationInspection
{
    internal static MetadataHierarchyRelationIndexPreparation
        PrepareHierarchyIndex(
            AssemblyInspectionSession session,
            MetadataReader reader,
            MetadataOperationPolicy policy,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        using var operation = new MetadataOperationContext(policy);
        MetadataRelationReceiptIdentity identity =
            ReadReceiptIdentity(reader, out string? receiptFailure);
        if (identity.ModuleVersionId is not Guid)
        {
            MetadataHierarchyRelationIndexReceipt failedReceipt =
                IndexReceipt(
                    identity,
                    operation,
                    sourceTypeCount: 0,
                    physicalRelationCount: 0,
                    indexedTargetCount: 0,
                    MetadataRelationFamilyDisposition.Failed,
                    [
                        MalformedDiagnostic(
                            MetadataRelationFamily.Hierarchy,
                            null,
                            receiptFailure
                                ?? "The metadata image has no usable module "
                                    + "identity."),
                    ]);
            return new MetadataHierarchyRelationIndexPreparation.Failed(
                failedReceipt);
        }

        MetadataImageAdmissionResult admission =
            operation.AdmitImage(reader);
        if (admission is MetadataImageAdmissionResult.Rejected rejected)
        {
            MetadataHierarchyRelationIndexReceipt failedReceipt =
                IndexReceipt(
                    identity,
                    operation,
                    sourceTypeCount: 0,
                    physicalRelationCount: 0,
                    indexedTargetCount: 0,
                    MetadataRelationFamilyDisposition.Partial,
                    [
                        new(
                            MetadataRelationFamily.Hierarchy,
                            MetadataRelationDiagnosticKind.Limit,
                            null,
                            "The metadata image exceeds the hierarchy-index "
                                + "row budget.",
                            MetadataOperationDimension.MetadataRows,
                            rejected.Failure.MaxMetadataRows,
                            rejected.Failure.ImageMetadataRows),
                    ]);
            return new MetadataHierarchyRelationIndexPreparation.Failed(
                failedReceipt);
        }

        var sources =
            ImmutableArray.CreateBuilder<
                MetadataHierarchyRelationIndexSource>(
                    reader.TypeDefinitions.Count);
        var candidateBuilders =
            new Dictionary<
                MetadataHierarchyRelationIndexKey,
                List<HierarchyCandidateBuilder>>();
        var targetNames =
            new Dictionary<EntityHandle, HierarchyTargetNameCacheEntry>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        var globalDiagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        var visibility = new MetadataVisibilityResolver(reader);
        int physicalRelationCount = 0;
        bool limited = false;

        foreach (TypeDefinitionHandle sourceHandle
            in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                TypeDefinition definition =
                    reader.GetTypeDefinition(sourceHandle);

                bool? isExternallyVisible = null;
                string? visibilityFailure = null;
                try
                {
                    isExternallyVisible =
                        visibility.IsExternallyVisible(
                            reader,
                            sourceHandle,
                            operation);
                }
                catch (MetadataVisibilityGraphException exception)
                {
                    visibilityFailure = exception.Message;
                    diagnostics.Add(
                        MalformedDiagnostic(
                            MetadataRelationFamily.Hierarchy,
                            MetadataTokens.GetToken(sourceHandle),
                            exception.Message));
                }

                bool? isHidden = null;
                string? hiddenFailure = null;
                try
                {
                    isHidden = AttributeReader.HasHiddenAttribute(
                        reader,
                        definition.GetCustomAttributes());
                }
                catch (Exception exception)
                    when (IsHierarchyArtifactFailure(exception))
                {
                    hiddenFailure = exception.Message;
                    diagnostics.Add(
                        MalformedDiagnostic(
                            MetadataRelationFamily.Hierarchy,
                            MetadataTokens.GetToken(sourceHandle),
                            exception.Message));
                }

                int sourceIndex = sources.Count;
                sources.Add(
                    new(
                        sourceHandle,
                        isExternallyVisible,
                        visibilityFailure,
                        isHidden,
                        hiddenFailure));

                if (!definition.BaseType.IsNil)
                {
                    IndexHierarchyOccurrence(
                        reader,
                        operation,
                        candidateBuilders,
                        targetNames,
                        diagnostics,
                        globalDiagnostics,
                        sourceIndex,
                        MetadataHierarchyRelationKind.BaseType,
                        definition.BaseType,
                        MetadataTokens.GetToken(sourceHandle));
                    physicalRelationCount++;
                }

                foreach (InterfaceImplementationHandle implementationHandle
                    in definition.GetInterfaceImplementations())
                {
                    operation.Charge(
                        MetadataOperationDimension
                            .InterfaceImplementationRows);
                    InterfaceImplementation implementation =
                        reader.GetInterfaceImplementation(
                            implementationHandle);
                    IndexHierarchyOccurrence(
                        reader,
                        operation,
                        candidateBuilders,
                        targetNames,
                        diagnostics,
                        globalDiagnostics,
                        sourceIndex,
                        MetadataHierarchyRelationKind.Interface,
                        implementation.Interface,
                        MetadataTokens.GetToken(implementationHandle));
                    physicalRelationCount++;
                }
            }
            catch (MetadataOperationBudgetExceededException exception)
            {
                limited = true;
                MetadataRelationDiagnostic diagnostic =
                    LimitDiagnostic(
                        MetadataRelationFamily.Hierarchy,
                        exception);
                diagnostics.Add(diagnostic);
                globalDiagnostics.Add(diagnostic);
                break;
            }
            catch (Exception exception)
                when (IsHierarchyArtifactFailure(exception))
            {
                MetadataRelationDiagnostic diagnostic =
                    MalformedDiagnostic(
                        MetadataRelationFamily.Hierarchy,
                        MetadataTokens.GetToken(sourceHandle),
                        exception.Message);
                diagnostics.Add(diagnostic);
                globalDiagnostics.Add(diagnostic);
            }
        }

        var targets = ImmutableDictionary.CreateBuilder<
            MetadataHierarchyRelationIndexKey,
            ImmutableArray<MetadataHierarchyRelationIndexCandidate>>();
        foreach ((
            MetadataHierarchyRelationIndexKey key,
            List<HierarchyCandidateBuilder> builders)
            in candidateBuilders)
        {
            targets.Add(
                key,
                [
                    .. builders.Select(
                        static builder => builder.Complete()),
                ]);
        }

        MetadataRelationFamilyDisposition disposition =
            limited || diagnostics.Count != 0
                ? MetadataRelationFamilyDisposition.Partial
                : MetadataRelationFamilyDisposition.Complete;
        MetadataHierarchyRelationIndexReceipt receipt =
            IndexReceipt(
                identity,
                operation,
                sources.Count,
                physicalRelationCount,
                targets.Count,
                disposition,
                diagnostics.ToImmutable());
        var data = new MetadataHierarchyRelationIndexData(
            receipt,
            sources.ToImmutable(),
            globalDiagnostics.ToImmutable(),
            targets.ToImmutable());
        return new MetadataHierarchyRelationIndexPreparation.Ready(
            new(session, data));
    }

    internal static MetadataHierarchyRelationAnalysisOutcome
        ExecuteHierarchyIndex(
            MetadataReader reader,
            MetadataHierarchyRelationIndexData data,
            MetadataHierarchyRelationAnalysisRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var operation = new MetadataOperationContext(request.Policy);
        var rows =
            ImmutableArray.CreateBuilder<
                MetadataHierarchyRelationAnalysisRow>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        diagnostics.AddRange(data.GlobalDiagnostics);
        ImmutableArray<MetadataHierarchyRelationIndexCandidate> candidates =
            IndexedCandidates(data, request.Target);

        int considered = 0;
        int examined = 0;
        int excluded = 0;
        int unavailable = 0;
        int matched = 0;
        bool stopped = false;

        foreach (MetadataHierarchyRelationIndexCandidate candidate
            in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            considered++;
            MetadataHierarchyRelationIndexSource source =
                data.Sources[candidate.SourceIndex];

            if (!request.IncludeNonPublic)
            {
                if (source.VisibilityFailure is { } visibilityFailure)
                {
                    unavailable++;
                    diagnostics.Add(
                        MalformedDiagnostic(
                            MetadataRelationFamily.Hierarchy,
                            MetadataTokens.GetToken(source.Handle),
                            visibilityFailure));
                    continue;
                }
                if (source.IsExternallyVisible is not true)
                {
                    excluded++;
                    continue;
                }
            }

            if (!request.IncludeHidden)
            {
                if (source.HiddenFailure is { } hiddenFailure)
                {
                    unavailable++;
                    diagnostics.Add(
                        MalformedDiagnostic(
                            MetadataRelationFamily.Hierarchy,
                            MetadataTokens.GetToken(source.Handle),
                            hiddenFailure));
                    continue;
                }
                if (source.IsHidden is true)
                {
                    excluded++;
                    continue;
                }
            }

            if (request.MaterializeRows)
            {
                MetadataTypeDefinitionNameReadResult read =
                    MetadataTypeDefinitionNameReader.Read(
                        reader,
                        source.Handle,
                        beforeMaterialize: amount =>
                            operation.Charge(
                                MetadataOperationDimension.StructuredNodes,
                                amount),
                        chargeChain: amount =>
                            operation.Charge(
                                MetadataOperationDimension.RelationshipEdges,
                                amount),
                        chargeCharacters: amount =>
                            operation.Charge(
                                MetadataOperationDimension.RetainedText,
                                amount));
                if (read
                    is MetadataTypeDefinitionNameReadResult.Rejected rejected)
                {
                    unavailable++;
                    diagnostics.Add(
                        MalformedDiagnostic(
                            MetadataRelationFamily.Hierarchy,
                            MetadataTokens.GetToken(source.Handle),
                            rejected.Failure.Detail));
                    continue;
                }
                var sourceName =
                    (MetadataTypeDefinitionNameReadResult.Read)read;
                operation.Charge(
                    MetadataOperationDimension.StructuredNodes);
                rows.Add(
                    new(
                        MetadataTypeDefinitionAddress.FromHandle(
                            reader,
                            source.Handle),
                        sourceName.Name,
                        candidate.Kind,
                        candidate.OccurrenceTokens));
            }

            examined++;
            matched++;
            if (request.ForwardPlan is { } forward
                && matched >= forward.MaximumCandidates
                && considered < candidates.Length)
            {
                stopped = true;
                break;
            }
        }

        MetadataRelationFamilyResult<
            MetadataHierarchyRelationAnalysisRow> relations =
                CompleteOrPartial(
                    rows,
                    diagnostics,
                    new(
                        considered,
                        examined,
                        excluded,
                        unavailable,
                        limited: 0));
        if (stopped)
        {
            relations = new(
                wasRequested: true,
                MetadataRelationFamilyDisposition.Partial,
                relations.Coverage,
                relations.Evidence,
                relations.Diagnostics);
        }

        return new MetadataHierarchyRelationAnalysisOutcome.Available(
            new(
                new(
                    data.Receipt.ModuleVersionId,
                    data.Receipt.Assembly,
                    [MetadataRelationFamily.Hierarchy],
                    operation.Counters),
                matched,
                request.ForwardPlan,
                stopped,
                relations));
    }

    static ImmutableArray<MetadataHierarchyRelationIndexCandidate>
        IndexedCandidates(
            MetadataHierarchyRelationIndexData data,
            MetadataHierarchyTargetSelection target)
    {
        if (target.Kind is MetadataHierarchyRelationKind exactKind)
        {
            return data.Targets.TryGetValue(
                new(target.Type, exactKind),
                out ImmutableArray<
                    MetadataHierarchyRelationIndexCandidate> exact)
                        ? exact
                        : [];
        }

        data.Targets.TryGetValue(
            new(
                target.Type,
                MetadataHierarchyRelationKind.BaseType),
            out ImmutableArray<
                MetadataHierarchyRelationIndexCandidate> bases);
        data.Targets.TryGetValue(
            new(
                target.Type,
                MetadataHierarchyRelationKind.Interface),
            out ImmutableArray<
                MetadataHierarchyRelationIndexCandidate> interfaces);
        if (bases.IsEmpty)
            return interfaces;
        if (interfaces.IsEmpty)
            return bases;

        var merged =
            ImmutableArray.CreateBuilder<
                MetadataHierarchyRelationIndexCandidate>(
                    bases.Length + interfaces.Length);
        int baseIndex = 0;
        int interfaceIndex = 0;
        while (baseIndex < bases.Length
            && interfaceIndex < interfaces.Length)
        {
            MetadataHierarchyRelationIndexCandidate baseCandidate =
                bases[baseIndex];
            MetadataHierarchyRelationIndexCandidate interfaceCandidate =
                interfaces[interfaceIndex];
            if (baseCandidate.SourceIndex
                <= interfaceCandidate.SourceIndex)
            {
                merged.Add(baseCandidate);
                baseIndex++;
            }
            else
            {
                merged.Add(interfaceCandidate);
                interfaceIndex++;
            }
        }
        merged.AddRange(bases.AsSpan()[baseIndex..]);
        merged.AddRange(interfaces.AsSpan()[interfaceIndex..]);
        return merged.ToImmutable();
    }

    static void IndexHierarchyOccurrence(
        MetadataReader reader,
        MetadataOperationContext operation,
        Dictionary<
            MetadataHierarchyRelationIndexKey,
            List<HierarchyCandidateBuilder>> candidateBuilders,
        Dictionary<EntityHandle, HierarchyTargetNameCacheEntry>
            targetNames,
        ImmutableArray<MetadataRelationDiagnostic>.Builder diagnostics,
        ImmutableArray<MetadataRelationDiagnostic>.Builder
            globalDiagnostics,
        int sourceIndex,
        MetadataHierarchyRelationKind kind,
        EntityHandle target,
        int occurrenceToken)
    {
        operation.Charge(
            MetadataOperationDimension.RelationshipEdges);
        if (!targetNames.TryGetValue(
                target,
                out HierarchyTargetNameCacheEntry cached))
        {
            bool wasRead = TryReadHierarchyTargetName(
                reader,
                operation,
                target,
                out MetadataTypeDefinitionName? targetName,
                out string? failure);
            cached = new(wasRead, targetName, failure);
            targetNames.Add(target, cached);
        }
        if (!cached.WasRead)
        {
            var diagnostic =
                new MetadataRelationDiagnostic(
                    MetadataRelationFamily.Hierarchy,
                    MetadataRelationDiagnosticKind.UnsupportedShape,
                    occurrenceToken,
                    cached.Failure
                        ?? "The hierarchy target definition could not be "
                            + "indexed safely.");
            diagnostics.Add(diagnostic);
            globalDiagnostics.Add(diagnostic);
            return;
        }

        operation.Charge(
            MetadataOperationDimension.RetainedHierarchyRelations);
        var key = new MetadataHierarchyRelationIndexKey(
            cached.Name!,
            kind);
        if (!candidateBuilders.TryGetValue(
                key,
                out List<HierarchyCandidateBuilder>? builders))
        {
            builders = [];
            candidateBuilders.Add(key, builders);
        }

        if (builders.Count != 0
            && builders[^1].SourceIndex == sourceIndex)
        {
            builders[^1].OccurrenceTokens.Add(occurrenceToken);
            return;
        }

        builders.Add(new(sourceIndex, kind, occurrenceToken));
    }

    static bool TryReadHierarchyTargetName(
        MetadataReader reader,
        MetadataOperationContext operation,
        EntityHandle target,
        out MetadataTypeDefinitionName? name,
        out string? failure)
    {
        name = null;
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
                        "The hierarchy TypeSpec is not a canonical generic "
                            + "instance.";
                    return false;
                }
                definition = signature.ReadTypeHandle();
            }

            MetadataTypeDefinitionNameReadResult result =
                definition.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            (TypeDefinitionHandle)definition,
                            beforeMaterialize: amount =>
                                operation.Charge(
                                    MetadataOperationDimension
                                        .StructuredNodes,
                                    amount),
                            chargeChain: amount =>
                                operation.Charge(
                                    MetadataOperationDimension
                                        .RelationshipEdges,
                                    amount),
                            chargeCharacters: amount =>
                                operation.Charge(
                                    MetadataOperationDimension
                                        .RetainedText,
                                    amount)),
                    HandleKind.TypeReference =>
                        MetadataTypeDefinitionNameReader.Read(
                            reader,
                            (TypeReferenceHandle)definition,
                            beforeMaterialize: amount =>
                                operation.Charge(
                                    MetadataOperationDimension
                                        .StructuredNodes,
                                    amount),
                            chargeChain: amount =>
                                operation.Charge(
                                    MetadataOperationDimension
                                        .RelationshipEdges,
                                    amount),
                            chargeCharacters: amount =>
                                operation.Charge(
                                    MetadataOperationDimension
                                        .RetainedText,
                                    amount)),
                    _ => throw new NotSupportedException(
                        "The hierarchy target is not a TypeDef, TypeRef, or "
                            + "canonical generic TypeSpec."),
                };
            if (result
                is MetadataTypeDefinitionNameReadResult.Rejected rejected)
            {
                failure = rejected.Failure.Detail;
                return false;
            }
            name =
                ((MetadataTypeDefinitionNameReadResult.Read)result).Name;
            return true;
        }
        catch (Exception exception)
            when (IsHierarchyArtifactFailure(exception))
        {
            failure = exception.Message;
            return false;
        }
    }

    static MetadataHierarchyRelationIndexReceipt IndexReceipt(
        MetadataRelationReceiptIdentity identity,
        MetadataOperationContext operation,
        int sourceTypeCount,
        int physicalRelationCount,
        int indexedTargetCount,
        MetadataRelationFamilyDisposition disposition,
        ImmutableArray<MetadataRelationDiagnostic> diagnostics) =>
        new(
            identity.ModuleVersionId,
            identity.Assembly,
            sourceTypeCount,
            physicalRelationCount,
            indexedTargetCount,
            disposition,
            operation.Counters,
            diagnostics);

    static bool IsHierarchyArtifactFailure(Exception exception) =>
        exception is BadImageFormatException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException
            or IndexOutOfRangeException;

    sealed class HierarchyCandidateBuilder
    {
        internal HierarchyCandidateBuilder(
            int sourceIndex,
            MetadataHierarchyRelationKind kind,
            int occurrenceToken)
        {
            SourceIndex = sourceIndex;
            Kind = kind;
            OccurrenceTokens.Add(occurrenceToken);
        }

        internal int SourceIndex { get; }

        internal MetadataHierarchyRelationKind Kind { get; }

        internal List<int> OccurrenceTokens { get; } = [];

        internal MetadataHierarchyRelationIndexCandidate Complete() =>
            new(
                SourceIndex,
                Kind,
                [.. OccurrenceTokens]);
    }

    readonly record struct HierarchyTargetNameCacheEntry(
        bool WasRead,
        MetadataTypeDefinitionName? Name,
        string? Failure);
}
