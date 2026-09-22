using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;
using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

public readonly record struct MetadataInterfaceImplementationRequest(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeIdentity Interface);

public readonly record struct MetadataInterfaceImplementationAddress(
    Guid ModuleVersionId,
    InterfaceImplementationHandle Handle)
{
    internal int Token => MetadataTokens.GetToken(Handle);
}

public readonly record struct MetadataInterfaceImplementationTargetAddress(
    Guid ModuleVersionId,
    EntityHandle Handle)
{
    internal int Token => MetadataTokens.GetToken(Handle);
}

public enum MetadataInterfaceImplementationFailureReason
{
    InvalidRequest,
    MalformedMetadata,
    Cycle,
    BudgetExceeded,
    UnsupportedShape,
}

public enum MetadataInterfaceImplementationStage
{
    RequestValidation,
    InterfaceImplementationScan,
    InterfaceDecode,
    ResultRetention,
}

public enum MetadataInterfaceImplementationMechanism
{
    ImageAdmission,
    AddressResolution,
    RowRead,
    HandleValidation,
    RelationshipTraversal,
    TypeSpecificationRoot,
    SignatureDecode,
    TextRetention,
}

public sealed record MetadataInterfaceImplementationFailure(
    MetadataInterfaceImplementationRequest Request,
    MetadataInterfaceImplementationFailureReason Reason,
    MetadataInterfaceImplementationStage Stage,
    MetadataInterfaceImplementationMechanism Mechanism,
    string Detail,
    InterfaceImplementationHandle? RelevantRow,
    EntityHandle RelevantHandle,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

internal readonly record struct MetadataInterfaceImplementationFailureSite(
    MetadataInterfaceImplementationStage Stage,
    MetadataInterfaceImplementationMechanism Mechanism,
    InterfaceImplementationHandle? RelevantRow = null,
    EntityHandle RelevantHandle = default);

public abstract record MetadataInterfaceImplementationResult
{
    private protected MetadataInterfaceImplementationResult(
        MetadataOperationCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        Counters = counters;
    }

    public MetadataOperationCounters Counters { get; }

    public sealed record Related
        : MetadataInterfaceImplementationResult
    {
        internal Related(
            ImmutableArray<MetadataInterfaceImplementationCertificate>
                relationships,
            MetadataOperationCounters counters)
            : base(counters)
        {
            if (relationships.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "A related result must carry at least one relationship.",
                    nameof(relationships));
            }
            Relationships = relationships;
        }

        public ImmutableArray<MetadataInterfaceImplementationCertificate>
            Relationships { get; }
    }

    public sealed record Absent
        : MetadataInterfaceImplementationResult
    {
        internal Absent(MetadataOperationCounters counters)
            : base(counters)
        {
        }
    }

    public sealed record Rejected
        : MetadataInterfaceImplementationResult
    {
        internal Rejected(
            MetadataInterfaceImplementationFailure failure,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public MetadataInterfaceImplementationFailure Failure { get; }
    }
}

public sealed record MetadataInterfaceImplementationCertificate(
    MetadataTypeDefinitionAddress Type,
    MetadataInterfaceImplementationAddress Relationship,
    MetadataInterfaceImplementationTargetAddress Target,
    MetadataTypeIdentity Interface);

internal sealed class MetadataInterfaceImplementationEvidenceOperation
{
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _context;
    readonly Func<
        Action,
        Action<TypeDefinitionHandle>,
        Action,
        Action<int>,
        MetadataTypeDefinitionIndex>
        _getTypeDefinitionIndex;
    readonly HashSet<TypeSpecificationHandle>
        _validatedTypeSpecifications = [];
    MetadataInterfaceImplementationRequest _request;
    TypeSpecificationHandle _activeTypeSpecification;
    CancellationToken _token;

    internal MetadataInterfaceImplementationEvidenceOperation(
        MetadataReader reader,
        MetadataOperationContext context,
        Func<
            Action,
            Action<TypeDefinitionHandle>,
            Action,
            Action<int>,
            MetadataTypeDefinitionIndex>
            getTypeDefinitionIndex)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(getTypeDefinitionIndex);
        _reader = reader;
        _context = context;
        _getTypeDefinitionIndex = getTypeDefinitionIndex;
    }

    internal MetadataInterfaceImplementationResult Relate(
        MetadataTypeDefinitionAddress type,
        MetadataTypeIdentity interfaceType,
        CancellationToken token)
    {
        _request = new(type, interfaceType);
        _token = token;
        try
        {
            token.ThrowIfCancellationRequested();
            var requestSite =
                new MetadataInterfaceImplementationFailureSite(
                    MetadataInterfaceImplementationStage
                        .RequestValidation,
                    MetadataInterfaceImplementationMechanism
                        .AddressResolution);
            string? requestFailure =
                MetadataTypeIdentityValidator
                    .ValidateInterfaceRequest(interfaceType);
            if (requestFailure is not null)
            {
                Reject(
                    requestSite,
                    MetadataInterfaceImplementationFailureReason
                        .InvalidRequest,
                    requestFailure);
            }
            if (!type.TryResolve(
                    _reader,
                    out TypeDefinitionHandle typeHandle))
            {
                Reject(
                    requestSite,
                    MetadataInterfaceImplementationFailureReason
                        .InvalidRequest,
                    "The requested TypeDef address does not resolve in this module.");
            }

            TypeDefinition definition = Read(
                requestSite,
                () => _reader.GetTypeDefinition(typeHandle));
            InterfaceImplementationHandleCollection rows = Read(
                requestSite,
                definition.GetInterfaceImplementations);
            GenericContext? genericContext = null;
            var relationships =
                ImmutableArray.CreateBuilder<
                    MetadataInterfaceImplementationCertificate>();
            foreach (InterfaceImplementationHandle row in rows)
            {
                token.ThrowIfCancellationRequested();
                var rowSite =
                    new MetadataInterfaceImplementationFailureSite(
                        MetadataInterfaceImplementationStage
                            .InterfaceImplementationScan,
                        MetadataInterfaceImplementationMechanism
                            .RowRead,
                        row);
                Charge(
                    rowSite,
                    MetadataOperationDimension
                        .InterfaceImplementationRows);
                _context.ObserveWork(
                    MetadataOperationWorkKind
                        .InterfaceImplementationRowRead);
                InterfaceImplementation implementation = Read(
                    rowSite,
                    () => _reader.GetInterfaceImplementation(row));
                var targetSite = rowSite with
                {
                    Mechanism =
                        MetadataInterfaceImplementationMechanism
                            .HandleValidation,
                };
                Charge(
                    targetSite,
                    MetadataOperationDimension.RelationshipEdges);
                EntityHandle target = Read(
                    targetSite,
                    () => implementation.Interface);
                targetSite = targetSite with
                {
                    RelevantHandle = target,
                };
                if (!IsValidType(target))
                {
                    Reject(
                        targetSite,
                        MetadataInterfaceImplementationFailureReason
                            .MalformedMetadata,
                        "An InterfaceImpl target does not identify a readable TypeDef, TypeRef, or TypeSpec row.");
                }

                genericContext ??= CreateGenericContext(
                    targetSite,
                    definition);
                TypeNode node = Decode(
                    target,
                    genericContext,
                    targetSite);
                EnsureCompleteType(node, targetSite);
                if (node is not NamedTypeNode
                    and not GenericTypeNode)
                {
                    Reject(
                        targetSite with
                        {
                            Stage =
                                MetadataInterfaceImplementationStage
                                    .InterfaceDecode,
                        },
                        MetadataInterfaceImplementationFailureReason
                            .UnsupportedShape,
                        "An InterfaceImpl target must decode to a named type or constructed named type.");
                }
                if (node is NamedTypeNode named
                    && MetadataStructuralTypeValidator
                        .DeclaredGenericArity(named.MetadataName) != 0)
                {
                    Reject(
                        targetSite with
                        {
                            Stage =
                                MetadataInterfaceImplementationStage
                                    .InterfaceDecode,
                        },
                        MetadataInterfaceImplementationFailureReason
                            .MalformedMetadata,
                        "An InterfaceImpl target references an unconstructed generic type.");
                }
                string? validation =
                    MetadataStructuralTypeValidator.Validate(
                        node,
                        genericContext.TypeParameters.Count,
                        methodParameterCount: 0,
                        "The InterfaceImpl target");
                if (validation is not null)
                {
                    Reject(
                        targetSite with
                        {
                            Stage =
                                MetadataInterfaceImplementationStage
                                    .InterfaceDecode,
                        },
                        MetadataInterfaceImplementationFailureReason
                            .MalformedMetadata,
                        validation);
                }

                MetadataTypeIdentity observed =
                    ProjectType(
                        node,
                        targetSite with
                        {
                            Stage =
                                MetadataInterfaceImplementationStage
                                    .ResultRetention,
                        });
                if (!Equals(observed, interfaceType))
                    continue;

                relationships.Add(
                    new(
                        type,
                        new MetadataInterfaceImplementationAddress(
                            type.ModuleVersionId,
                            row),
                        new MetadataInterfaceImplementationTargetAddress(
                            type.ModuleVersionId,
                            target),
                        observed));
            }

            MetadataOperationCounters counters = _context.Counters;
            return relationships.Count == 0
                ? new MetadataInterfaceImplementationResult.Absent(
                    counters)
                : new MetadataInterfaceImplementationResult.Related(
                    relationships.ToImmutable(),
                    counters);
        }
        catch (MetadataInterfaceImplementationRejectedException ex)
        {
            return new MetadataInterfaceImplementationResult.Rejected(
                new MetadataInterfaceImplementationFailure(
                    _request,
                    ex.Reason,
                    ex.Stage,
                    ex.Mechanism,
                    ex.Message,
                    ex.RelevantRow,
                    ex.RelevantHandle,
                    ex.BudgetDimension,
                    ex.BudgetLimit,
                    ex.AttemptedCharge),
                _context.Counters);
        }
    }

    TypeNode Decode(
        EntityHandle target,
        GenericContext context,
        MetadataInterfaceImplementationFailureSite site)
    {
        var decodeSite = site with
        {
            Stage =
                MetadataInterfaceImplementationStage.InterfaceDecode,
            Mechanism =
                MetadataInterfaceImplementationMechanism
                    .SignatureDecode,
        };
        TypeNodeProvider provider = CreateTypeProvider(decodeSite);
        return target.Kind switch
        {
            HandleKind.TypeDefinition =>
                Read(
                    decodeSite,
                    () => provider.GetTypeFromDefinition(
                        _reader,
                        (TypeDefinitionHandle)target,
                        rawTypeKind: 0x12)),
            HandleKind.TypeReference =>
                Read(
                    decodeSite,
                    () => provider.GetTypeFromReference(
                        _reader,
                        (TypeReferenceHandle)target,
                        rawTypeKind: 0x12)),
            HandleKind.TypeSpecification =>
                ReadTypeSpecification(
                    (TypeSpecificationHandle)target,
                    context,
                    decodeSite),
            _ => throw Rejection(
                decodeSite,
                MetadataInterfaceImplementationFailureReason
                    .UnsupportedShape,
                "An InterfaceImpl target is outside the admitted type shapes."),
        };
    }

    TypeNode ReadTypeSpecification(
        TypeSpecificationHandle handle,
        GenericContext context,
        MetadataInterfaceImplementationFailureSite site)
    {
        var rootSite = site with
        {
            Mechanism =
                MetadataInterfaceImplementationMechanism
                    .TypeSpecificationRoot,
            RelevantHandle = handle,
        };
        TypeSpecificationRootReadResult rootResult =
            TypeSpecificationRoot.Read(
                _reader,
                handle,
                beforeDecodeBytes: (active, bytes) =>
                    Charge(
                        rootSite with
                        {
                            RelevantHandle = active,
                        },
                        MetadataOperationDimension.SignatureBytes,
                        bytes),
                beforeDependencyEdge: dependency =>
                    Charge(
                        rootSite with
                        {
                            RelevantHandle = dependency,
                        },
                        MetadataOperationDimension.RelationshipEdges),
                graphValidated: validated =>
                    _validatedTypeSpecifications.Add(validated));
        if (rootResult is not TypeSpecificationRootReadResult.Read
            {
                Root.Kind:
                    TypeSpecificationRootKind.NamedType,
            })
        {
            throw TypeSpecificationRejection(
                rootSite,
                rootResult);
        }

        TypeNode node = Read(
            rootSite,
            () => GuardedProviderDecode.TypeSpec(
                _reader,
                handle,
                CreateTypeProvider(rootSite),
                context,
                fallback: (TypeNode)new DegradedTypeNode()));
        return node;
    }

    TypeNodeProvider CreateTypeProvider(
        MetadataInterfaceImplementationFailureSite site) =>
        new(
            beforeRetain: value =>
            {
                Charge(
                    ActiveTypeSpecificationSite(site),
                    MetadataOperationDimension.RetainedText,
                    value.Length);
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNodeTextRetention);
            },
            beforeMaterialize: amount =>
            {
                Charge(
                    ActiveTypeSpecificationSite(site),
                    MetadataOperationDimension.StructuredNodes,
                    amount);
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNodeMaterialization);
            },
            beforeNameMaterialize: () =>
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNameMaterialization),
            beforePublicKeyMaterialize: () =>
                _context.ObserveWork(
                    MetadataOperationWorkKind
                        .PublicKeyTokenMaterialization),
            beforeCreateNode: () =>
            {
                Charge(
                    ActiveTypeSpecificationSite(site),
                    MetadataOperationDimension.StructuredNodes);
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNodeCreation);
            },
            beforeTypeSpecificationDecode: (reader, handle) =>
                ValidateTypeSpecificationGraph(
                    site,
                    reader,
                    handle),
            retainExactScope: true,
            beforeTypeDefinitionResolve: (_, handle) =>
                Charge(
                    site with
                    {
                        RelevantHandle = handle,
                    },
                    MetadataOperationDimension.RelationshipEdges),
            beforeTypeReferenceResolve: (_, handle) =>
                Charge(
                    site with
                    {
                        RelevantHandle = handle,
                    },
                    MetadataOperationDimension.RelationshipEdges),
            typeSpecificationScope: EnterTypeSpecificationScope,
            relationshipRejected: rejection =>
                throw RelationshipRejection(site, rejection),
            beforeRelationshipFollow: handle =>
                Charge(
                    site with
                    {
                        RelevantHandle = handle,
                    },
                    MetadataOperationDimension.RelationshipEdges),
            getLocalTypeDefinitions: reader =>
                GetLocalTypeDefinitions(site, reader));

    MetadataTypeDefinitionIndex GetLocalTypeDefinitions(
        MetadataInterfaceImplementationFailureSite site,
        MetadataReader reader)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The TypeDef index route received a foreign metadata reader.");
        }
        try
        {
            return _getTypeDefinitionIndex(
                _token.ThrowIfCancellationRequested,
                handle =>
                    Charge(
                        site with
                        {
                            RelevantHandle = handle,
                        },
                        MetadataOperationDimension.RelationshipEdges),
                () =>
                {
                    Charge(
                        site,
                        MetadataOperationDimension.StructuredNodes);
                    _context.ObserveWork(
                        MetadataOperationWorkKind
                            .TypeDefinitionIndexMaterialization);
                },
                amount =>
                {
                    Charge(
                        site,
                        MetadataOperationDimension.RetainedText,
                        amount);
                    _context.ObserveWork(
                        MetadataOperationWorkKind
                            .TypeDefinitionIndexTextRetention);
                });
        }
        catch (MetadataTypeDefinitionIndexFailureException ex)
        {
            throw Rejection(
                site with
                {
                    RelevantHandle = ex.Subject,
                },
                ex.Kind switch
                {
                    MetadataTypeDefinitionIndexFailureKind.Cycle =>
                        MetadataInterfaceImplementationFailureReason
                            .Cycle,
                    MetadataTypeDefinitionIndexFailureKind
                        .BudgetExceeded =>
                        MetadataInterfaceImplementationFailureReason
                            .BudgetExceeded,
                    _ =>
                        MetadataInterfaceImplementationFailureReason
                            .MalformedMetadata,
                },
                ex.Message);
        }
    }

    GenericContext CreateGenericContext(
        MetadataInterfaceImplementationFailureSite site,
        TypeDefinition type)
    {
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes);
        _context.ObserveWork(
            MetadataOperationWorkKind.GenericContextConstruction);
        try
        {
            GenericContext context = Read(
                site,
                () => GenericContext
                    .ForTypeWithRelationshipObserver(
                        _reader,
                        type,
                        _ =>
                            _context.ObserveWork(
                                MetadataOperationWorkKind
                                    .GenericParameterNameMaterialization),
                        active =>
                            Charge(
                                site with
                                {
                                    RelevantHandle = active,
                                },
                                MetadataOperationDimension
                                    .RelationshipEdges)));
            foreach (string name in context.TypeParameters)
            {
                Charge(
                    site,
                    MetadataOperationDimension.RetainedText,
                    name.Length);
            }
            return context;
        }
        catch (GenericContextRelationshipRejectedException ex)
        {
            throw RelationshipRejection(
                site,
                ex.Rejection);
        }
        catch (GenericContextBudgetExceededException ex)
        {
            throw Rejection(
                site,
                MetadataInterfaceImplementationFailureReason
                    .BudgetExceeded,
                ex.Message);
        }
    }

    MetadataTypeIdentity ProjectType(
        TypeNode node,
        MetadataInterfaceImplementationFailureSite site) =>
        new MetadataTypeIdentityProjector(
            beforeCreateNode: () =>
                Charge(
                    site,
                    MetadataOperationDimension.StructuredNodes),
            retain: value => Retain(value, site),
            reject: detail =>
                Rejection(
                    site,
                    MetadataInterfaceImplementationFailureReason
                        .MalformedMetadata,
                    detail))
            .Project(node);

    InertString Retain(
        string value,
        MetadataInterfaceImplementationFailureSite site)
    {
        int encodedLength =
            VisualEncoder.MeasureEncodedLength(
                TextPolicy.Field,
                value);
        Charge(
            site with
            {
                Mechanism =
                    MetadataInterfaceImplementationMechanism
                        .TextRetention,
            },
            MetadataOperationDimension.RetainedText,
            encodedLength);
        return new InertString(TextPolicy.Field, value);
    }

    void ValidateTypeSpecificationGraph(
        MetadataInterfaceImplementationFailureSite site,
        MetadataReader reader,
        TypeSpecificationHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The TypeSpec validation route received a foreign metadata reader.");
        }
        var typeSpecSite = site with
        {
            Mechanism =
                MetadataInterfaceImplementationMechanism
                    .TypeSpecificationRoot,
            RelevantHandle = handle,
        };
        BlobHandle signature = Read(
            typeSpecSite,
            () => reader.GetTypeSpecification(handle).Signature);
        int decodeBytes = Read(
            typeSpecSite,
            () => reader.GetBlobReader(signature).Length);
        Charge(
            typeSpecSite,
            MetadataOperationDimension.SignatureBytes,
            decodeBytes);
        if (_validatedTypeSpecifications.Contains(handle))
            return;

        TypeSpecificationRootReadResult? failure =
            TypeSpecificationRoot.ValidateGraph(
                reader,
                handle,
                beforeDecodeBytes: (active, bytes) =>
                    Charge(
                        typeSpecSite with
                        {
                            RelevantHandle = active,
                        },
                        MetadataOperationDimension.SignatureBytes,
                        bytes),
                beforeDependencyEdge: dependency =>
                    Charge(
                        typeSpecSite with
                        {
                            RelevantHandle = dependency,
                        },
                        MetadataOperationDimension.RelationshipEdges),
                graphValidated: validated =>
                    _validatedTypeSpecifications.Add(validated));
        if (failure is not null)
        {
            throw TypeSpecificationRejection(
                typeSpecSite,
                failure);
        }
    }

    MetadataInterfaceImplementationRejectedException
        TypeSpecificationRejection(
            MetadataInterfaceImplementationFailureSite site,
            TypeSpecificationRootReadResult result) =>
        result switch
        {
            TypeSpecificationRootReadResult.Cycle cycle =>
                Rejection(
                    site with
                    {
                        RelevantHandle = cycle.Subject,
                    },
                    MetadataInterfaceImplementationFailureReason.Cycle,
                    cycle.Detail),
            TypeSpecificationRootReadResult.BudgetExceeded exceeded =>
                Rejection(
                    site with
                    {
                        RelevantHandle = exceeded.Subject,
                    },
                    MetadataInterfaceImplementationFailureReason
                        .BudgetExceeded,
                    exceeded.Detail),
            TypeSpecificationRootReadResult.Malformed malformed =>
                Rejection(
                    site with
                    {
                        RelevantHandle = malformed.Subject,
                    },
                    MetadataInterfaceImplementationFailureReason
                        .MalformedMetadata,
                    malformed.Detail),
            TypeSpecificationRootReadResult.Unsupported unsupported =>
                Rejection(
                    site with
                    {
                        RelevantHandle = unsupported.Subject,
                    },
                    MetadataInterfaceImplementationFailureReason
                        .UnsupportedShape,
                    unsupported.Detail),
            TypeSpecificationRootReadResult.Read =>
                Rejection(
                    site,
                    MetadataInterfaceImplementationFailureReason
                        .UnsupportedShape,
                    "The InterfaceImpl TypeSpec root is not a named type."),
            _ => throw new InvalidOperationException(
                "Unknown TypeSpec validation result."),
        };

    MetadataInterfaceImplementationRejectedException
        RelationshipRejection(
            MetadataInterfaceImplementationFailureSite site,
            RelationshipTraversalRejection rejection) =>
        Rejection(
            site with
            {
                Mechanism =
                    MetadataInterfaceImplementationMechanism
                        .RelationshipTraversal,
                RelevantHandle = rejection.Subject,
            },
            rejection.Kind switch
            {
                RelationshipTraversalRejectionKind.Cycle =>
                    MetadataInterfaceImplementationFailureReason.Cycle,
                RelationshipTraversalRejectionKind.NodeBudget
                    or RelationshipTraversalRejectionKind.NameBudget =>
                    MetadataInterfaceImplementationFailureReason
                        .BudgetExceeded,
                _ =>
                    MetadataInterfaceImplementationFailureReason
                        .MalformedMetadata,
            },
            rejection.Detail);

    MetadataInterfaceImplementationFailureSite
        ActiveTypeSpecificationSite(
            MetadataInterfaceImplementationFailureSite site) =>
        _activeTypeSpecification.IsNil
            ? site
            : site with
            {
                RelevantHandle = _activeTypeSpecification,
            };

    IDisposable EnterTypeSpecificationScope(
        MetadataReader reader,
        TypeSpecificationHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The TypeSpec accounting route received a foreign metadata reader.");
        }
        TypeSpecificationHandle prior = _activeTypeSpecification;
        _activeTypeSpecification = handle;
        return new TypeSpecificationAccountingScope(this, prior);
    }

    sealed class TypeSpecificationAccountingScope(
        MetadataInterfaceImplementationEvidenceOperation owner,
        TypeSpecificationHandle prior) : IDisposable
    {
        bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            owner._activeTypeSpecification = prior;
            _disposed = true;
        }
    }

    void EnsureCompleteType(
        TypeNode node,
        MetadataInterfaceImplementationFailureSite site)
    {
        if (node.IsDegraded)
        {
            Reject(
                site with
                {
                    Stage =
                        MetadataInterfaceImplementationStage
                            .InterfaceDecode,
                },
                MetadataInterfaceImplementationFailureReason
                    .MalformedMetadata,
                "An InterfaceImpl target could not be decoded completely.");
        }
    }

    bool IsValidType(EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                IsValid(
                    (TypeDefinitionHandle)handle,
                    TableIndex.TypeDef),
            HandleKind.TypeReference =>
                IsValid(
                    (TypeReferenceHandle)handle,
                    TableIndex.TypeRef),
            HandleKind.TypeSpecification =>
                IsValid(
                    (TypeSpecificationHandle)handle,
                    TableIndex.TypeSpec),
            _ => false,
        };

    bool IsValid(EntityHandle handle, TableIndex table)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0
            && row <= _reader.GetTableRowCount(table);
    }

    void Charge(
        MetadataInterfaceImplementationFailureSite site,
        MetadataOperationDimension dimension,
        long amount = 1)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            _context.Charge(dimension, amount);
        }
        catch (MetadataOperationBudgetExceededException ex)
        {
            throw Rejection(
                site,
                MetadataInterfaceImplementationFailureReason
                    .BudgetExceeded,
                $"The {ex.Dimension} budget cannot accept a charge of "
                    + $"{ex.AttemptedCharge} at limit {ex.Limit}.",
                ex.Dimension,
                ex.Limit,
                ex.AttemptedCharge);
        }
    }

    T Read<T>(
        MetadataInterfaceImplementationFailureSite site,
        Func<T> read)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            return read();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException)
        {
            throw Rejection(
                site,
                MetadataInterfaceImplementationFailureReason
                    .MalformedMetadata,
                ex.Message);
        }
    }

    void Reject(
        MetadataInterfaceImplementationFailureSite site,
        MetadataInterfaceImplementationFailureReason reason,
        string detail) =>
        throw Rejection(site, reason, detail);

    MetadataInterfaceImplementationRejectedException Rejection(
        MetadataInterfaceImplementationFailureSite site,
        MetadataInterfaceImplementationFailureReason reason,
        string detail,
        MetadataOperationDimension? budgetDimension = null,
        long? budgetLimit = null,
        long? attemptedCharge = null) =>
        new(
            reason,
            site.Stage,
            site.Mechanism,
            detail,
            site.RelevantRow,
            site.RelevantHandle,
            budgetDimension,
            budgetLimit,
            attemptedCharge);
}

internal sealed class MetadataInterfaceImplementationRejectedException(
    MetadataInterfaceImplementationFailureReason reason,
    MetadataInterfaceImplementationStage stage,
    MetadataInterfaceImplementationMechanism mechanism,
    string detail,
    InterfaceImplementationHandle? relevantRow,
    EntityHandle relevantHandle,
    MetadataOperationDimension? budgetDimension = null,
    long? budgetLimit = null,
    long? attemptedCharge = null) : Exception(detail)
{
    internal MetadataInterfaceImplementationFailureReason Reason { get; } =
        reason;
    internal MetadataInterfaceImplementationStage Stage { get; } = stage;
    internal MetadataInterfaceImplementationMechanism Mechanism { get; } =
        mechanism;
    internal InterfaceImplementationHandle? RelevantRow { get; } =
        relevantRow;
    internal EntityHandle RelevantHandle { get; } = relevantHandle;
    internal MetadataOperationDimension? BudgetDimension { get; } =
        budgetDimension;
    internal long? BudgetLimit { get; } = budgetLimit;
    internal long? AttemptedCharge { get; } = attemptedCharge;
}
