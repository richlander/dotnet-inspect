using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Applies the Metadata-owned hierarchy source gate, target predicate, guarded
/// decoding, budgets, and evidence projection to one metadata image.
/// </summary>
public sealed class MetadataHierarchyRelationAnalysis : IDisposable
{
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _operation;
    readonly MetadataVisibilityClassification? _visibility;
    readonly bool _ownsOperation;
    bool _disposed;

    public MetadataHierarchyRelationAnalysis(
        MetadataReader reader,
        MetadataOperationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(policy);
        _reader = reader;
        _operation = new(policy);
        _ownsOperation = true;
        if (_operation.AdmitImage(reader)
            is MetadataImageAdmissionResult.Rejected rejected)
        {
            _operation.Dispose();
            _disposed = true;
            throw new InvalidOperationException(
                "The metadata image exceeds the hierarchy-analysis row budget: "
                + $"{rejected.Failure.ImageMetadataRows} rows exceed "
                + $"{rejected.Failure.MaxMetadataRows}.");
        }
    }

    internal MetadataHierarchyRelationAnalysis(
        MetadataReader reader,
        MetadataOperationContext operation,
        MetadataVisibilityClassification? visibility)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(operation);
        _reader = reader;
        _operation = operation;
        _visibility = visibility;
    }

    public MetadataOperationCounters Counters
    {
        get
        {
            EnsureAlive();
            return _operation.Counters;
        }
    }

    public bool IncludesSource(
        TypeDefinitionHandle handle,
        bool includeNonPublic,
        bool includeHidden)
    {
        EnsureAlive();
        _operation.Charge(
            MetadataOperationDimension.DeclarationCandidates);
        if (!includeNonPublic
            && !(_visibility?.IsExternallyVisible(handle)
                ?? MetadataVisibility.IsExternallyVisible(
                    _reader,
                    handle)))
        {
            return false;
        }

        return includeHidden
            || !AttributeReader.HasHiddenAttribute(
                _reader,
                _reader.GetTypeDefinition(handle)
                    .GetCustomAttributes());
    }

    public InterfaceImplementation ReadInterfaceImplementation(
        InterfaceImplementationHandle handle)
    {
        EnsureAlive();
        _operation.Charge(
            MetadataOperationDimension.InterfaceImplementationRows);
        return _reader.GetInterfaceImplementation(handle);
    }

    public MetadataHierarchyRelationAnalysisResult Analyze(
        TypeDefinitionHandle sourceHandle,
        TypeDefinition sourceDefinition,
        EntityHandle target,
        MetadataHierarchyRelationKind kind,
        int metadataToken,
        MetadataHierarchyTargetSelection selection)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Kind is { } selectedKind
            && selectedKind != kind)
        {
            return MetadataHierarchyRelationAnalysisResult.NoMatch.Instance;
        }

        _operation.Charge(
            MetadataOperationDimension.RelationshipEdges);
        GenericContext? context = null;
        MetadataTypeIdentity? selectedTarget = null;
        MetadataTypeNameFailure? directFailure = null;
        MetadataTypeDefinitionNameMatchResult? directMatch =
            target.Kind switch
            {
                HandleKind.TypeDefinition =>
                    MetadataTypeDefinitionName.Matches(
                        _reader,
                        (TypeDefinitionHandle)target,
                        selection.Type,
                        out directFailure),
                HandleKind.TypeReference =>
                    MetadataTypeDefinitionName.Matches(
                        _reader,
                        (TypeReferenceHandle)target,
                        selection.Type,
                        out directFailure),
                HandleKind.TypeSpecification =>
                    PrefilterTypeSpecification(
                        (TypeSpecificationHandle)target,
                        selection.Type,
                        out directFailure),
                _ => null,
            };
        if (directMatch
            is MetadataTypeDefinitionNameMatchResult.Rejected)
        {
            return new MetadataHierarchyRelationAnalysisResult.Rejected(
                directFailure?.Detail
                    ?? "The hierarchy target name could not be read.");
        }
        if (directMatch
            is MetadataTypeDefinitionNameMatchResult.NoMatch)
        {
            return MetadataHierarchyRelationAnalysisResult.NoMatch.Instance;
        }
        if (directMatch is null)
        {
            context = GenericContext.ForType(
                _reader,
                sourceDefinition);
            MetadataTypeIdentityDecodeResult candidate =
                MetadataTypeIdentityDecoder.Decode(
                    _reader,
                    target,
                    context,
                    _operation);
            if (candidate
                is MetadataTypeIdentityDecodeResult.Rejected rejected)
            {
                return new MetadataHierarchyRelationAnalysisResult.Rejected(
                    rejected.Detail);
            }
            selectedTarget =
                ((MetadataTypeIdentityDecodeResult.Decoded)candidate)
                    .Identity;
            if (!MatchesTargetIdentity(
                    selectedTarget,
                    selection.Type))
            {
                return MetadataHierarchyRelationAnalysisResult.NoMatch.Instance;
            }
        }

        MetadataTypeDefinitionNameReadResult sourceRead =
            MetadataTypeDefinitionName.Read(
                _reader,
                sourceHandle);
        if (sourceRead
            is MetadataTypeDefinitionNameReadResult.Rejected rejectedSource)
        {
            return new MetadataHierarchyRelationAnalysisResult.Rejected(
                rejectedSource.Failure.Detail);
        }

        context ??= GenericContext.ForType(
            _reader,
            sourceDefinition);
        if (selectedTarget is null)
        {
            MetadataTypeIdentityDecodeResult decoded =
                MetadataTypeIdentityDecoder.Decode(
                    _reader,
                    target,
                    context,
                    _operation);
            if (decoded
                is MetadataTypeIdentityDecodeResult.Rejected rejectedTarget)
            {
                return new MetadataHierarchyRelationAnalysisResult.Rejected(
                    rejectedTarget.Detail);
            }
            selectedTarget =
                ((MetadataTypeIdentityDecodeResult.Decoded)decoded)
                    .Identity;
        }

        return new MetadataHierarchyRelationAnalysisResult.Selected(
            new(
                MetadataTypeDefinitionAddress.FromHandle(
                    _reader,
                    sourceHandle),
                ((MetadataTypeDefinitionNameReadResult.Read)sourceRead).Name,
                kind,
                selectedTarget,
                metadataToken));
    }

    public static bool MatchesTargetIdentity(
        MetadataTypeIdentity identity,
        MetadataTypeDefinitionName target)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(target);
        MetadataNamedTypeIdentity? named = identity switch
        {
            MetadataTypeIdentity.Named value => value.Definition,
            MetadataTypeIdentity.GenericInstance value =>
                value.Definition,
            _ => null,
        };
        return named is not null
            && MetadataTypeDefinitionName.Create(
                    named.Namespace.ToString(),
                    [.. named.Segments.Select(static segment =>
                        segment.ToString())])
                is MetadataTypeDefinitionNameResult.Valid valid
            && valid.Name == target;
    }

    MetadataTypeDefinitionNameMatchResult?
        PrefilterTypeSpecification(
            TypeSpecificationHandle handle,
            MetadataTypeDefinitionName target,
            out MetadataTypeNameFailure? failure)
    {
        failure = null;
        try
        {
            BlobReader signature =
                _reader.GetBlobReader(
                    _reader.GetTypeSpecification(handle).Signature);
            if (signature.ReadSignatureTypeCode()
                != SignatureTypeCode.GenericTypeInstance)
            {
                return null;
            }

            if (signature.ReadSignatureTypeCode()
                != SignatureTypeCode.TypeHandle)
            {
                return null;
            }

            EntityHandle definition = signature.ReadTypeHandle();
            return definition.Kind switch
            {
                HandleKind.TypeDefinition =>
                    MetadataTypeDefinitionName.Matches(
                        _reader,
                        (TypeDefinitionHandle)definition,
                        target,
                        out failure),
                HandleKind.TypeReference =>
                    MetadataTypeDefinitionName.Matches(
                        _reader,
                        (TypeReferenceHandle)definition,
                        target,
                        out failure),
                _ => null,
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsOperation)
            _operation.Dispose();
    }

    void EnsureAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public abstract record MetadataHierarchyRelationAnalysisResult
{
    private protected MetadataHierarchyRelationAnalysisResult()
    {
    }

    public sealed record Selected(
        MetadataHierarchyRelationEvidence Evidence)
        : MetadataHierarchyRelationAnalysisResult;

    public sealed record Rejected(string Detail)
        : MetadataHierarchyRelationAnalysisResult;

    public sealed record NoMatch
        : MetadataHierarchyRelationAnalysisResult
    {
        public static NoMatch Instance { get; } = new();

        private NoMatch()
        {
        }
    }
}
